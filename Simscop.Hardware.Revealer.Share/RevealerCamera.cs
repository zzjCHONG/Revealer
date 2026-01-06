using EyeCam.Shared;
using OpenCvSharp;
using Simscop.Spindisk.Core.Interfaces;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Simscop.Spindisk.Hardware.Revealer
{
    public class RevealerCamera : ICamera
    {
        private EyeCam.Shared.Revealer? _camera;
        private int _deviceIndex;
        private bool _isInitialized = false;
        private bool _isCapturing = false;
        private Mat? _latestFrame;
        private readonly object _lockObj = new();
        private static bool _sdkInitialized = false;
        private static readonly object _sdkLock = new();

        // FPS计算
        private int _frameCount = 0;
        private readonly Stopwatch _fpsStopwatch = new();

        public event Action<Mat>? FrameReceived;
        public event Action<bool>? OnDisConnectState;

        public RevealerCamera(int deviceIndex = 1)
        {
            _deviceIndex = deviceIndex;
        }

        #region 初始化和释放

        public bool Init()
        {
            try
            {
                // 初始化SDK（全局一次）
                lock (_sdkLock)
                {
                    if (!_sdkInitialized)
                    {
                        EyeCam.Shared.Revealer.Initialize(2, ".", 10 * 1024 * 1024, 1);
                        _sdkInitialized = true;
                    }
                }

                // 创建相机实例
                _camera = new EyeCam.Shared.Revealer(_deviceIndex);
                _camera.Open();

                InitDefaultSettings();
                _isInitialized = true;

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Camera initialization failed: {ex.Message}");
                return false;
            }
        }

        private void InitDefaultSettings()
        {
            if (_camera == null) return;

            try
            {
                // 设置默认参数
                SetImageProcessingEnabled(ImageProcessingFeature.Brightness, false);
                SetImageProcessingEnabled(ImageProcessingFeature.Contrast, false);
                SetImageProcessingEnabled(ImageProcessingFeature.Gamma, false);
                SetImageProcessingEnabled(ImageProcessingFeature.PseudoColor, false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to set default settings: {ex.Message}");
            }
        }

        public bool FreeDevice()
        {
            try
            {
                if (_isCapturing)
                    StopCapture();

                _camera?.Close();
                _camera?.Dispose();
                _camera = null;

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to free device: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 设备信息

        public Dictionary<InfoEnum, string> InfoDirectory
        {
            get
            {
                var info = new Dictionary<InfoEnum, string>();

                if (_camera == null) return info;

                try
                {
                    var deviceInfo = _camera.GetDeviceInfo();

                    info[InfoEnum.Model] = deviceInfo.ModelName ?? "Unknown";
                    info[InfoEnum.SerialNumber] = deviceInfo.SerialNumber ?? "Unknown";
                    info[InfoEnum.Version] = EyeCam.Shared.Revealer.GetVersion();
                    info[InfoEnum.FirmwareVersion] = deviceInfo.DeviceVersion ?? "Unknown";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to get device info: {ex.Message}");
                }

                return info;
            }
        }

        #endregion

        #region 采集控制

        public bool StartCapture()
        {
            if (_camera == null || _isCapturing)
                return false;

            try
            {
                // 设置缓冲区
                _camera.SetBufferCount(3);

                // 注册帧回调
                _camera.AttachProcessedGrabbing(OnFrameCallback);

                // 开始采集
                _camera.StartGrabbing();

                _isCapturing = true;
                _fpsStopwatch.Restart();
                _frameCount = 0;

                // 首次启动后启用自动色阶
                if (_isInitialized)
                {
                    IsAutoLevel = true;
                    _isInitialized = false;
                }

                Console.WriteLine("[INFO] Capture started");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to start capture: {ex.Message}");
                return false;
            }
        }

        public bool StopCapture()
        {
            if (_camera == null || !_isCapturing)
                return false;

            try
            {
                _camera.StopGrabbing();
                _isCapturing = false;

                lock (_lockObj)
                {
                    _latestFrame?.Dispose();
                    _latestFrame = null;
                }

                _fpsStopwatch.Stop();

                Console.WriteLine("[INFO] Capture stopped");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to stop capture: {ex.Message}");
                return false;
            }
        }

        private void OnFrameCallback(ImageFrame frame)
        {
            try
            {
                Mat? mat = ConvertFrameToMat(frame);
                if (mat == null || mat.Empty())
                    return;

                // 更新最新帧
                lock (_lockObj)
                {
                    _latestFrame?.Dispose();
                    _latestFrame = mat.Clone();
                }

                // 触发事件
                FrameReceived?.Invoke(mat.Clone());

                // 计算FPS
                _frameCount++;
                if (_fpsStopwatch.Elapsed.TotalSeconds >= 2)
                {
                    FpsCal = _frameCount / _fpsStopwatch.Elapsed.TotalSeconds;
                    _frameCount = 0;
                    _fpsStopwatch.Restart();
                }

                mat.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Frame callback exception: {ex.Message}");
            }
        }

        public bool Capture(out Mat? img)
        {
            img = null;

            if (_camera == null)
                return false;

            try
            {
                if (_isCapturing)
                {
                    // 连续采集模式：返回最新帧的副本
                    lock (_lockObj)
                    {
                        if (_latestFrame != null && !_latestFrame.Empty())
                        {
                            img = _latestFrame.Clone();
                            return true;
                        }
                    }
                    return false;
                }
                else
                {
                    // 单次采集模式
                    if (!StartCapture())
                        return false;

                    // 等待一帧
                    Thread.Sleep(Math.Max(50, (int)Exposure + 50));

                    var frame = _camera.GetProcessedFrame(5000);
                    img = ConvertFrameToMat(frame);

                    StopCapture();

                    return img != null && !img.Empty();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Capture failed: {ex.Message}");
                return false;
            }
        }

        public async Task<Mat?> CaptureAsync()
        {
            return await Task.Run(() =>
            {
                if (Capture(out Mat? mat))
                    return mat;
                return null;
            });
        }

        /// <summary>
        /// 将ImageFrame转换为OpenCV Mat（安全版本）
        /// </summary>
        private static Mat? ConvertFrameToMat(ImageFrame frame)
        {
            if (frame == null || frame.Data == null || frame.Data.Length == 0)
                return null;

            Mat? mat = null;
            try
            {
                // 根据像素格式确定Mat类型和通道数
                (MatType matType, int depth, int channels) = GetMatTypeFromPixelFormat(frame.PixelFormat);

                // 创建空Mat
                mat = new Mat(frame.Height, frame.Width, matType);

                // 验证数据大小
                int expectedSize = frame.Height * frame.Width * channels * (depth / 8);
                int actualSize = Math.Min(frame.DataSize, frame.Data.Length);

                if (actualSize < expectedSize)
                {
                    Console.WriteLine($"[WARNING] Data size mismatch: expected {expectedSize}, got {actualSize}");
                }

                // 将byte数组数据复制到Mat的内部缓冲区
                Marshal.Copy(frame.Data, 0, mat.Data, Math.Min(expectedSize, actualSize));

                return mat;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Frame conversion failed: {ex.Message}");
                mat?.Dispose();
                return null;
            }
        }

        /// <summary>
        /// 根据像素格式获取Mat类型、位深度和通道数
        /// </summary>
        private static (MatType matType, int depth, int channels) GetMatTypeFromPixelFormat(int pixelFormat)
        {
            // 像素格式参考：https://www.emva.org/wp-content/uploads/GenICam_SFNC_v2_7.pdf
            // 格式编码：0xAABBCCDD
            // AA: 颜色空间 (01=Mono, 02=RGB, 03=YUV等)
            // BB: 位深度
            // CC: 通道数
            // DD: 格式ID

            return pixelFormat switch
            {
                // === Mono 格式 ===
                0x01080001 => (MatType.CV_8UC1, 8, 1),      // Mono8
                0x01100003 => (MatType.CV_16UC1, 16, 1),    // Mono16
                0x010C0047 => (MatType.CV_16UC1, 12, 1),    // Mono12 (存储为16bit)
                0x01100005 => (MatType.CV_16UC1, 16, 1),    // Mono10 (存储为16bit)
                0x01140007 => (MatType.CV_16UC1, 16, 1),    // Mono14 (存储为16bit)

                // === RGB 格式 ===
                0x02180014 => (MatType.CV_8UC3, 8, 3),      // RGB8
                0x02180015 => (MatType.CV_8UC3, 8, 3),      // BGR8
                0x02300016 => (MatType.CV_16UC3, 16, 3),    // RGB16

                // === RGBA 格式 ===
                0x02200016 => (MatType.CV_8UC4, 8, 4),      // RGBA8
                0x02200017 => (MatType.CV_8UC4, 8, 4),      // BGRA8

                // === YUV 格式 ===
                0x0210001F => (MatType.CV_8UC2, 8, 2),      // YUV422_8

                // === Bayer 格式（需要解拜耳处理）===
                0x010800C5 => (MatType.CV_8UC1, 8, 1),      // BayerRG8
                0x010800C6 => (MatType.CV_8UC1, 8, 1),      // BayerGB8
                0x010800C7 => (MatType.CV_8UC1, 8, 1),      // BayerGR8
                0x010800C8 => (MatType.CV_8UC1, 8, 1),      // BayerBG8
                0x011000CD => (MatType.CV_16UC1, 16, 1),    // BayerRG16
                0x011000CE => (MatType.CV_16UC1, 16, 1),    // BayerGB16
                0x011000CF => (MatType.CV_16UC1, 16, 1),    // BayerGR16
                0x011000D0 => (MatType.CV_16UC1, 16, 1),    // BayerBG16

                // === 默认：假设为Mono8 ===
                _ => (MatType.CV_8UC1, 8, 1)
            };
        }

        #endregion

        #region 曝光控制

        public (double Min, double Max) ExposureRange
        {
            get
            {
                if (_camera == null)
                    return (0, 0);

                try
                {
                    double min = _camera.GetFloatFeatureMin("ExposureTime");
                    double max = _camera.GetFloatFeatureMax("ExposureTime");
                    return (min / 1000.0, max / 1000.0); // 转换为毫秒
                }
                catch
                {
                    return (0.1, 1000.0); // 默认范围
                }
            }
        }

        public double Exposure
        {
            get
            {
                if (_camera == null)
                    return 0;

                try
                {
                    return _camera.GetFloatFeature("ExposureTime") / 1000.0; // 转换为毫秒
                }
                catch
                {
                    return 0;
                }
            }
            set
            {
                if (_camera == null || IsAutoExposure)
                    return;

                try
                {
                    _camera.SetFloatFeature("ExposureTime", value * 1000.0); // 转换为微秒
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to set exposure: {ex.Message}");
                }
            }
        }

        public bool IsAutoExposure
        {
            get
            {
                if (_camera == null)
                    return false;

                try
                {
                    // 通过自动曝光模式判断：0=中央, 1=右侧, 2=关闭
                    // 需要一个标志位来记住状态，或者通过其他方式判断
                    return _isAutoExposureEnabled;
                }
                catch
                {
                    return false;
                }
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    if (value)
                    {
                        // 启用自动曝光，默认使用中央模式
                        _camera.SetAutoExposureParam(0, 128);
                        _camera.ExecuteAutoExposure();
                    }
                    else
                    {
                        // 关闭自动曝光
                        _camera.SetAutoExposureParam(2, -1);
                    }
                    _isAutoExposureEnabled = value;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to set auto exposure: {ex.Message}");
                }
            }
        }

        private bool _isAutoExposureEnabled = false;

        #endregion

        #region 图像处理参数

        public double Gamma
        {
            get
            {
                if (_camera == null)
                    return 100;

                try
                {
                    return _camera.GetImageProcessingValue((int)ImageProcessingFeature.Gamma);
                }
                catch
                {
                    return 100;
                }
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    double clampedValue = Math.Clamp(value, 0, 255);
                    _camera.SetImageProcessingValue((int)ImageProcessingFeature.Gamma, (int)clampedValue);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to set gamma: {ex.Message}");
                }
            }
        }

        public double Contrast
        {
            get
            {
                if (_camera == null)
                    return 128;

                try
                {
                    return _camera.GetImageProcessingValue((int)ImageProcessingFeature.Contrast);
                }
                catch
                {
                    return 128;
                }
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    double clampedValue = Math.Clamp(value, 0, 255);
                    _camera.SetImageProcessingValue((int)ImageProcessingFeature.Contrast, (int)clampedValue);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to set contrast: {ex.Message}");
                }
            }
        }

        public double Brightness
        {
            get
            {
                if (_camera == null)
                    return 128;

                try
                {
                    return _camera.GetImageProcessingValue((int)ImageProcessingFeature.Brightness);
                }
                catch
                {
                    return 128;
                }
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    double clampedValue = Math.Clamp(value, 0, 255);
                    _camera.SetImageProcessingValue((int)ImageProcessingFeature.Brightness, (int)clampedValue);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to set brightness: {ex.Message}");
                }
            }
        }

        #endregion

        #region 增益控制

        public ushort Gain
        {
            get
            {
                if (_camera == null)
                    return 0;

                try
                {
                    return (ushort)_camera.GetIntFeature("Gain");
                }
                catch
                {
                    return 0;
                }
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    _camera.SetIntFeature("Gain", value);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to set gain: {ex.Message}");
                }
            }
        }

        public (ushort Min, ushort Max) GainRange
        {
            get
            {
                if (_camera == null)
                    return (0, 100);

                try
                {
                    long min = _camera.GetIntFeatureMin("Gain");
                    long max = _camera.GetIntFeatureMax("Gain");
                    return ((ushort)min, (ushort)max);
                }
                catch
                {
                    return (0, 100);
                }
            }
        }

        public List<string> GainList => new() { "Low", "Medium", "High" };

        #endregion

        #region 自动色阶

        public bool IsAutoLevel
        {
            get
            {
                if (_camera == null)
                    return false;

                try
                {
                    int mode = _camera.GetAutoLevels();
                    return mode > 0;
                }
                catch
                {
                    return false;
                }
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    // 0=关闭, 1=右, 2=左, 3=左右
                    _camera.SetAutoLevels(value ? 3 : 0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to set auto levels: {ex.Message}");
                }
            }
        }

        public (int Left, int Right) CurrentLevel
        {
            get
            {
                if (_camera == null)
                    return (0, 65535);

                try
                {
                    int left = _camera.GetAutoLevelValue(2);  // 2=左色阶
                    int right = _camera.GetAutoLevelValue(1); // 1=右色阶
                    return (left, right);
                }
                catch
                {
                    return (0, 65535);
                }
            }
            set
            {
                if (_camera == null || IsAutoLevel)
                    return;

                try
                {
                    _camera.SetAutoLevelValue(2, value.Left);  // 2=左色阶
                    _camera.SetAutoLevelValue(1, value.Right); // 1=右色阶
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to set levels: {ex.Message}");
                }
            }
        }

        public (int Min, int Max) LevelRange
        {
            get
            {
                // 根据图像深度返回范围
                int maxValue = ImageDetph switch
                {
                    8 => 255,
                    12 => 4095,
                    16 => 65535,
                    _ => 65535
                };
                return (0, maxValue);
            }
        }

        #endregion

        #region 图像属性

        public double FrameRate => FpsCal;
        public double FpsCal { get; private set; }

        public bool IsFlipHorizontally
        {
            get
            {
                if (_camera == null)
                    return false;

                try
                {
                    return _camera.GetImageProcessingEnabled((int)ImageProcessingFeature.Flip);
                }
                catch
                {
                    return false;
                }
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    _camera.SetImageProcessingEnabled((int)ImageProcessingFeature.Flip, value);
                    // 如果SDK支持，还需要设置翻转方向
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to set horizontal flip: {ex.Message}");
                }
            }
        }

        public bool IsFlipVertially { get; set; } // 需要软件实现或SDK支持

        public int ClockwiseRotation
        {
            get
            {
                if (_camera == null)
                    return 0;

                try
                {
                    return _camera.GetImageProcessingValue((int)ImageProcessingFeature.Rotation);
                }
                catch
                {
                    return 0;
                }
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    _camera.SetImageProcessingValue((int)ImageProcessingFeature.Rotation, value);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to set rotation: {ex.Message}");
                }
            }
        }

        public int ImageDetph
        {
            get
            {
                if (_camera == null)
                    return 8;

                try
                {
                    // 从PixelFormat推断位深度
                    string pixelFormat = _camera.GetEnumFeatureSymbol("PixelFormat");
                    if (pixelFormat.Contains("16"))
                        return 16;
                    else if (pixelFormat.Contains("12"))
                        return 12;
                    else
                        return 8;
                }
                catch
                {
                    return 8;
                }
            }
            set => throw new NotImplementedException("Cannot set image depth directly");
        }

        public Size ImageSize
        {
            get
            {
                if (_camera == null)
                    return new Size(0, 0);

                try
                {
                    long width = _camera.GetIntFeature("Width");
                    long height = _camera.GetIntFeature("Height");
                    return new Size((int)width, (int)height);
                }
                catch
                {
                    return new Size(0, 0);
                }
            }
        }

        public int PseudoColor
        {
            get
            {
                if (_camera == null)
                    return 0;

                try
                {
                    return _camera.GetPseudoColorMap();
                }
                catch
                {
                    return 0;
                }
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    _camera.SetPseudoColorMap(value);
                    _camera.SetImageProcessingEnabled((int)ImageProcessingFeature.PseudoColor, value > 0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to set pseudo color: {ex.Message}");
                }
            }
        }

        #endregion

        #region 分辨率和ROI

        public (uint Width, uint Height) Resolution
        {
            get
            {
                var size = ImageSize;
                return ((uint)size.Width, (uint)size.Height);
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    bool wasCapturing = _isCapturing;
                    if (wasCapturing)
                        StopCapture();

                    _camera.SetIntFeature("Width", value.Width);
                    _camera.SetIntFeature("Height", value.Height);

                    if (wasCapturing)
                        StartCapture();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to set resolution: {ex.Message}");
                }
            }
        }

        public List<(uint Width, uint Height)> Resolutions
        {
            get => _availableResolutions;
            set => _availableResolutions = value;
        }

        private List<(uint Width, uint Height)> _availableResolutions = new()
        {
            (2048, 2048),
            (1920, 1080),
            (1280, 1024),
            (1024, 1024),
            (640, 480)
        };

        public bool SetResolution(int resolution)
        {
            if (resolution < 0 || resolution >= ResolutionsList.Count)
                return false;

            try
            {
                bool wasCapturing = _isCapturing;
                if (wasCapturing)
                    StopCapture();

                var res = _availableResolutions[resolution];
                Resolution = res;

                if (wasCapturing)
                    StartCapture();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to set resolution: {ex.Message}");
                return false;
            }
        }

        public List<string> ResolutionsList
        {
            get
            {
                return _availableResolutions
                    .Select(r => $"{r.Width} x {r.Height}")
                    .ToList();
            }
        }

        public void SetROI(int width, int height, int offsetX, int offsetY)
        {
            if (_camera == null)
                return;

            try
            {
                bool wasCapturing = _isCapturing;
                if (wasCapturing)
                    StopCapture();

                _camera.SetROI(width, height, offsetX, offsetY);

                if (wasCapturing)
                    StartCapture();

                Console.WriteLine($"[INFO] ROI set: {width}x{height} at ({offsetX}, {offsetY})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to set ROI: {ex.Message}");
            }
        }

        public bool DisableROI()
        {
            if (_camera == null)
                return false;

            try
            {
                bool wasCapturing = _isCapturing;
                if (wasCapturing)
                    StopCapture();

                // 恢复最大分辨率
                if (_availableResolutions.Count > 0)
                {
                    var maxRes = _availableResolutions[0];
                    _camera.SetROI((int)maxRes.Width, (int)maxRes.Height, 0, 0);
                }

                if (wasCapturing)
                    StartCapture();

                Console.WriteLine("[INFO] ROI disabled");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to disable ROI: {ex.Message}");
                return false;
            }
        }

        public List<string> ROIList => new()
        {
            "2048 x 2048",
            "1024 x 1024",
            "512 x 512",
            "256 x 256",
            "自定义ROI",
            "全画幅"
        };

        #endregion

        #region 图像模式和复合模式

        public bool SetImageMode(int imageMode)
        {
            // Revealer相机可能没有这个概念，根据实际情况实现
            Console.WriteLine("[WARNING] SetImageMode not implemented for Revealer camera");
            return false;
        }

        public List<string> ImageModesList => new() { "Standard" };

        public bool SetCompositeMode(int mode)
        {
            // Revealer相机可能没有这个概念，根据实际情况实现
            Console.WriteLine("[WARNING] SetCompositeMode not implemented for Revealer camera");
            return false;
        }

        public List<string> CompositeModeList => new() { "Standard" };

        #endregion

        #region 白平衡和色温

        public bool AutoWhiteBlanceOnce()
        {
            // 需要根据SDK实际支持情况实现
            Console.WriteLine("[WARNING] AutoWhiteBalance not implemented");
            return false;
        }

        public (double Min, double Max) TemperatureRange => (2000, 10000);

        public double Temperature
        {
            get => 6500; // 默认色温
            set => Console.WriteLine("[WARNING] Temperature control not implemented");
        }

        public (double Min, double Max) TintRange => (-150, 150);

        public double Tint
        {
            get => 0;
            set => Console.WriteLine("[WARNING] Tint control not implemented");
        }

        #endregion

        #region 保存图像

        public bool SaveImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                Mat? imageToSave = null;

                if (_isCapturing)
                {
                    lock (_lockObj)
                    {
                        if (_latestFrame != null && !_latestFrame.Empty())
                        {
                            imageToSave = _latestFrame.Clone();
                        }
                    }
                }
                else
                {
                    if (!Capture(out imageToSave) || imageToSave == null)
                    {
                        Console.WriteLine("[ERROR] Failed to capture image for saving");
                        return false;
                    }
                }

                if (imageToSave == null || imageToSave.Empty())
                {
                    Console.WriteLine("[ERROR] No image to save");
                    return false;
                }

                // 确保目录存在
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                bool success = Cv2.ImWrite(path, imageToSave);
                imageToSave.Dispose();

                if (success)
                {
                    Console.WriteLine($"[INFO] Image saved to: {path}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] SaveImage exception: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 辅助方法

        private void SetImageProcessingEnabled(ImageProcessingFeature feature, bool enabled)
        {
            if (_camera == null) return;

            try
            {
                _camera.SetImageProcessingEnabled((int)feature, enabled);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to set image processing: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理相机异常并显示中文错误信息
        /// </summary>
        private void HandleCameraException(Exception ex, string operation)
        {
            if (ex is CameraException cameraEx)
            {
                string errorMsg = CameraErrorCodeHelper.GetChineseMessage(cameraEx.ErrorCode);
                Console.WriteLine($"[ERROR] {operation}失败: {errorMsg} (错误码: {cameraEx.ErrorCode})");
            }
            else
            {
                Console.WriteLine($"[ERROR] {operation}失败: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// 图像处理功能枚举
    /// </summary>
    public enum ImageProcessingFeature
    {
        Brightness = 0,
        Contrast = 1,
        Gamma = 2,
        PseudoColor = 3,
        Rotation = 4,
        Flip = 5
    }
}