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
        private double _calculatedFps = 0;

        // 状态标志
        private bool _isAutoExposureEnabled = false;
        private int _currentReadoutMode = 7; // 默认：高动态模式
        private int _currentBinningMode = 0; // 默认：1x1

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
                        EyeCam.Shared.Revealer.Initialize(logLevel: 2, logPath: ".", fileSize: 10 * 1024 * 1024, fileNum: 1);
                        _sdkInitialized = true;
                        Console.WriteLine($"[INFO] SDK initialized, version: {EyeCam.Shared.Revealer.GetVersion()}");
                    }
                }

                // 枚举设备
                var devices = EyeCam.Shared.Revealer.EnumerateDevices();
                if (devices.Count == 0)
                {
                    Console.WriteLine("[ERROR] No camera devices found");
                    return false;
                }

                Console.WriteLine($"[INFO] Found {devices.Count} camera(s):");
                foreach (var dev in devices)
                {
                    Console.WriteLine($"  [{dev.Index}] {dev.Name}");
                }

                // 验证设备索引
                if (_deviceIndex < 0 || _deviceIndex >= devices.Count)
                {
                    Console.WriteLine($"[ERROR] Invalid device index: {_deviceIndex}");
                    return false;
                }

                // 创建相机实例
                _camera = new EyeCam.Shared.Revealer(_deviceIndex);
                _camera.Open();

                // 获取设备信息
                var deviceInfo = _camera.GetDeviceInfo();
                Console.WriteLine($"[INFO] Camera opened: {deviceInfo.ModelName} (SN: {deviceInfo.SerialNumber})");

                // 初始化默认设置
                InitDefaultSettings();
                _isInitialized = true;

                return true;
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "相机初始化");
                return false;
            }
        }

        private void InitDefaultSettings()
        {
            if (_camera == null) return;

            try
            {
                // 停止采集（如果正在采集）
                if (_camera.IsGrabbing)
                {
                    _camera.StopGrabbing();
                }

                // 1. 设置ROI为最大值（全传感器区域）
                _camera.ResetROI();
                Console.WriteLine($"[INFO] Sensor size: {_camera.SensorWidth}x{_camera.SensorHeight}");

                // 2. 设置读出模式为高动态（16位）
                _camera.ReadoutMode = 7; // bit16_From11
                _currentReadoutMode = 7;
                Console.WriteLine("[INFO] Readout mode: bit16_From11 (High Dynamic Range)");

                // 3. 设置Binning模式为1x1（全分辨率）
                _camera.BinningMode = 0; // OneByOne
                _currentBinningMode = 0;
                Console.WriteLine("[INFO] Binning mode: 1x1 (Full Resolution)");

                // 4. 设置像素格式为Mono16
                try
                {
                    _camera.PixelFormatSymbol = "Mono16";
                    Console.WriteLine("[INFO] Pixel format: Mono16");
                }
                catch
                {
                    Console.WriteLine("[WARNING] Failed to set pixel format to Mono16");
                }

                // 5. 设置曝光时间（默认10ms）
                _camera.ExposureTime = 10000; // 10ms = 10000us
                Console.WriteLine($"[INFO] Exposure time: {_camera.ExposureTime / 1000.0}ms");

                // 6. 禁用帧率限制（最大速度采集）
                _camera.FrameRateEnable = false;
                Console.WriteLine("[INFO] Frame rate control: Disabled (Max speed)");

                // 7. 设置为连续采集模式（关闭触发）
                _camera.ConfigureContinuousMode();
                Console.WriteLine("[INFO] Trigger mode: Continuous");

                // 8. 禁用图像处理功能（使用原始数据）
                SetImageProcessingEnabled(ImageProcessingFeature.Brightness, false);
                SetImageProcessingEnabled(ImageProcessingFeature.Contrast, false);
                SetImageProcessingEnabled(ImageProcessingFeature.Gamma, false);
                SetImageProcessingEnabled(ImageProcessingFeature.PseudoColor, false);
                SetImageProcessingEnabled(ImageProcessingFeature.Rotation, false);
                SetImageProcessingEnabled(ImageProcessingFeature.Flip, false);
                Console.WriteLine("[INFO] Image processing: Disabled");

                // 9. 设置温度控制
                try
                {
                    _camera.FanSwitch = true;
                    Console.WriteLine($"[INFO] Fan: Enabled, Current temp: {_camera.DeviceTemperature}°C");
                }
                catch
                {
                    Console.WriteLine("[WARNING] Temperature control not available");
                }

                Console.WriteLine("[INFO] Default settings applied successfully");
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "设置默认参数");
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

                Console.WriteLine("[INFO] Camera device freed");
                return true;
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "释放设备");
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
 
                    Console.WriteLine(deviceInfo.ManufacturerInfo);
                    Console.WriteLine(deviceInfo.CameraName);
                    Console.WriteLine(_camera.SensorWidth);
                    Console.WriteLine(_camera.SensorHeight);
                    Console.WriteLine($"{_camera.DeviceTemperature:F1}°C");

                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "获取设备信息");
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
                // 设置缓冲区数量
                _camera.SetBufferCount(5);

                // 注册帧回调
                _camera.AttachProcessedGrabbing(OnFrameCallback);

                // 开始采集
                _camera.StartGrabbing();

                _isCapturing = true;
                _fpsStopwatch.Restart();
                _frameCount = 0;

                Console.WriteLine("[INFO] Capture started");
                return true;
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "开始采集");
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

                Console.WriteLine($"[INFO] Capture stopped (Final FPS: {_calculatedFps:F2})");
                return true;
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "停止采集");
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
                if (_fpsStopwatch.Elapsed.TotalSeconds >= 2.0)
                {
                    _calculatedFps = _frameCount / _fpsStopwatch.Elapsed.TotalSeconds;
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
                    // 单帧采集模式（软件触发）
                    bool wasInContinuousMode = _camera.TriggerInType == 0;

                    // 配置软件触发
                    _camera.ConfigureSoftwareTrigger();
                    _camera.SetBufferCount(3);
                    _camera.StartGrabbing();

                    // 发送软件触发
                    _camera.SoftwareTrigger();

                    // 获取帧（带超时）
                    var frame = _camera.GetProcessedFrame(5000);
                    img = ConvertFrameToMat(frame);

                    // 停止采集
                    _camera.StopGrabbing();

                    // 恢复连续模式
                    if (wasInContinuousMode)
                    {
                        _camera.ConfigureContinuousMode();
                    }

                    return img != null && !img.Empty();
                }
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "单帧采集");
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
        /// 将ImageFrame转换为OpenCV Mat
        /// </summary>
        private static Mat? ConvertFrameToMat(ImageFrame frame)
        {
            if (frame == null || frame.Data == null || frame.Data.Length == 0)
                return null;

            Mat? mat = null;
            try
            {
                // 根据像素格式确定Mat类型
                (MatType matType, int depth, int channels) = GetMatTypeFromPixelFormat(frame.PixelFormat);

                // 创建Mat
                mat = new Mat(frame.Height, frame.Width, matType);

                // 验证数据大小
                int expectedSize = frame.Height * frame.Width * channels * (depth / 8);
                int actualSize = Math.Min(frame.DataSize, frame.Data.Length);

                if (actualSize < expectedSize)
                {
                    Console.WriteLine($"[WARNING] Data size mismatch: expected {expectedSize}, got {actualSize}");
                }

                // 复制数据到Mat
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
        /// 根据像素格式获取Mat类型
        /// </summary>
        private static (MatType matType, int depth, int channels) GetMatTypeFromPixelFormat(int pixelFormat)
        {
            return pixelFormat switch
            {
                // Mono格式
                0x01080001 => (MatType.CV_8UC1, 8, 1),      // Mono8
                0x01100003 => (MatType.CV_16UC1, 16, 1),    // Mono16
                0x010C0047 => (MatType.CV_16UC1, 12, 1),    // Mono12
                0x01100005 => (MatType.CV_16UC1, 10, 1),    // Mono10

                // RGB格式
                0x02180014 => (MatType.CV_8UC3, 8, 3),      // RGB8
                0x02180015 => (MatType.CV_8UC3, 8, 3),      // BGR8
                0x02300016 => (MatType.CV_16UC3, 16, 3),    // RGB16

                // RGBA格式
                0x02200016 => (MatType.CV_8UC4, 8, 4),      // RGBA8
                0x02200017 => (MatType.CV_8UC4, 8, 4),      // BGRA8

                // Bayer格式
                0x010800C5 or 0x010800C6 or 0x010800C7 or 0x010800C8 => (MatType.CV_8UC1, 8, 1),
                0x011000CD or 0x011000CE or 0x011000CF or 0x011000D0 => (MatType.CV_16UC1, 16, 1),

                // 默认：Mono8
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
                    var range = _camera.GetFloatFeatureRange("ExposureTime");
                    return (range.Min / 1000.0, range.Max / 1000.0); // 转换为毫秒
                }
                catch
                {
                    return (0.01, 10000.0); // 默认范围
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
                    return _camera.ExposureTime / 1000.0; // 转换为毫秒
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
                    // 防止在自动曝光模式下手动设置
                    if (_isAutoExposureEnabled)
                    {
                        Console.WriteLine("[WARNING] Cannot set exposure manually in auto exposure mode");
                        return;
                    }

                    double exposureUs = value * 1000.0; // 转换为微秒
                    double clampedValue = Math.Clamp(exposureUs, ExposureRange.Min * 1000.0, ExposureRange.Max * 1000.0);

                    _camera.ExposureTime = clampedValue;
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置曝光时间");
                }
            }
        }

        public bool IsAutoExposure
        {
            get => _isAutoExposureEnabled;
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    if (value)
                    {
                        // 启用自动曝光：模式0=中央，目标灰度128
                        _camera.SetAutoExposureParam(mode: 0, targetGray: 128);
                        int actualGray = _camera.ExecuteAutoExposure();
                        Console.WriteLine($"[INFO] Auto exposure executed, target gray: {actualGray}");
                    }
                    else
                    {
                        // 关闭自动曝光：模式2=关闭
                        _camera.SetAutoExposureParam(mode: 2, targetGray: -1);
                        Console.WriteLine("[INFO] Auto exposure disabled");
                    }

                    _isAutoExposureEnabled = value;
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置自动曝光");
                }
            }
        }

        #endregion

        #region 图像处理参数

        public double Gamma
        {
            get
            {
                if (_camera == null)
                    return 56;

                try
                {
                    return _camera.GetImageProcessingValue((int)ImageProcessingFeature.Gamma);
                }
                catch
                {
                    return 56;
                }
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    int clampedValue = (int)Math.Clamp(value, 0, 100);
                    _camera.SetImageProcessingValue((int)ImageProcessingFeature.Gamma, clampedValue);
                    _camera.SetImageProcessingEnabled((int)ImageProcessingFeature.Gamma, true);
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置Gamma");
                }
            }
        }

        public double Contrast
        {
            get
            {
                if (_camera == null)
                    return 50;

                try
                {
                    return _camera.GetImageProcessingValue((int)ImageProcessingFeature.Contrast);
                }
                catch
                {
                    return 50;
                }
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    int clampedValue = (int)Math.Clamp(value, 0, 100);
                    _camera.SetImageProcessingValue((int)ImageProcessingFeature.Contrast, clampedValue);
                    _camera.SetImageProcessingEnabled((int)ImageProcessingFeature.Contrast, true);
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置对比度");
                }
            }
        }

        public double Brightness
        {
            get
            {
                if (_camera == null)
                    return 50;

                try
                {
                    return _camera.GetImageProcessingValue((int)ImageProcessingFeature.Brightness);
                }
                catch
                {
                    return 50;
                }
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    int clampedValue = (int)Math.Clamp(value, -100, 100);
                    _camera.SetImageProcessingValue((int)ImageProcessingFeature.Brightness, clampedValue);
                    _camera.SetImageProcessingEnabled((int)ImageProcessingFeature.Brightness, true);
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置亮度");
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
                    if (_camera.IsFeatureAvailable("Gain"))
                    {
                        return (ushort)_camera.GetIntFeature("Gain");
                    }
                }
                catch { }

                return 0;
            }
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    if (_camera.IsFeatureAvailable("Gain") && _camera.IsFeatureWriteable("Gain"))
                    {
                        _camera.SetIntFeature("Gain", value);
                    }
                    else
                    {
                        Console.WriteLine("[WARNING] Gain control not available");
                    }
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置增益");
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
                    if (_camera.IsFeatureAvailable("Gain"))
                    {
                        var range = _camera.GetIntFeatureRange("Gain");
                        return ((ushort)range.Min, (ushort)range.Max);
                    }
                }
                catch { }

                return (0, 100);
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

                    if (value)
                    {
                        Console.WriteLine("[INFO] Auto levels enabled (Left & Right)");
                    }
                    else
                    {
                        Console.WriteLine("[INFO] Auto levels disabled");
                    }
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置自动色阶");
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
                    return (0, LevelRange.Max);
                }
            }
            set
            {
                if (_camera == null)
                    return;

                // 不能在自动色阶模式下手动设置
                if (IsAutoLevel)
                {
                    Console.WriteLine("[WARNING] Cannot set levels manually in auto level mode");
                    return;
                }

                try
                {
                    int left = Math.Clamp(value.Left, LevelRange.Min, LevelRange.Max);
                    int right = Math.Clamp(value.Right, LevelRange.Min, LevelRange.Max);

                    _camera.SetAutoLevelValue(2, left);  // 2=左色阶
                    _camera.SetAutoLevelValue(1, right); // 1=右色阶

                    Console.WriteLine($"[INFO] Levels set: Left={left}, Right={right}");
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置色阶");
                }
            }
        }

        public (int Min, int Max) LevelRange
        {
            get
            {
                // 根据ReadoutMode返回范围
                int maxValue = _currentReadoutMode switch
                {
                    0 or 1 => 2047,  // 11位：0-2047
                    6 => 4095,       // 12位：0-4095
                    7 => 65535,      // 16位：0-65535
                    _ => 65535
                };
                return (0, maxValue);
            }
        }

        #endregion

        #region 图像属性

        public double FrameRate
        {
            get
            {
                if (_camera == null)
                    return 0;

                try
                {
                    // 如果启用了帧率控制，返回设定的帧率
                    if (_camera.FrameRateEnable)
                    {
                        return _camera.AcquisitionFrameRate;
                    }
                    // 否则返回计算的实际帧率
                    return _calculatedFps;
                }
                catch
                {
                    return _calculatedFps;
                }
            }
        }

        public double FpsCal => _calculatedFps;

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
                    // 可能需要设置翻转方向（水平/垂直）
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置水平翻转");
                }
            }
        }

        public bool IsFlipVertially { get; set; } // 需要软件实现

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
                    int rotation = value % 360;
                    if (rotation < 0) rotation += 360;

                    _camera.SetImageProcessingValue((int)ImageProcessingFeature.Rotation, rotation);
                    _camera.SetImageProcessingEnabled((int)ImageProcessingFeature.Rotation, true);
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置旋转");
                }
            }
        }

        public int ImageDetph
        {
            get
            {
                // 根据ReadoutMode返回位深度
                return _currentReadoutMode switch
                {
                    0 or 1 => 11,  // bit11
                    6 => 12,       // bit12
                    7 => 16,       // bit16
                    _ => 16
                };
            }
            set => throw new NotImplementedException("Cannot set image depth directly, use SetImageMode instead");
        }

        public Size ImageSize
        {
            get
            {
                if (_camera == null)
                    return new Size(0, 0);

                try
                {
                    return new Size((int)_camera.Width, (int)_camera.Height);
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
                    _camera.SetImageProcessingEnabled((int)ImageProcessingFeature.PseudoColor,true);
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置伪彩");
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

                    _camera.Width = value.Width;
                    _camera.Height = value.Height;

                    if (wasCapturing)
                        StartCapture();

                    Console.WriteLine($"[INFO] Resolution set to: {value.Width}x{value.Height}");
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置分辨率");
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
            (640, 480),
            (512, 512),
            (256, 256)
        };

        public bool SetResolution(int resolution)
        {
            if (resolution < 0 || resolution >= BinningModeList.Count)
                return false;

            try
            {
                bool wasCapturing = _isCapturing;
                if (wasCapturing)
                    StopCapture();

                // 设置Binning模式
                _camera.BinningMode = (ulong)resolution;
                _currentBinningMode = resolution;

                if (wasCapturing)
                    StartCapture();

                Console.WriteLine($"[INFO] Binning mode set to: {BinningModeList[resolution]}");
                return true;
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "设置分辨率模式");
                return false;
            }
        }

        public List<string> ResolutionsList => BinningModeList;

        /// <summary>
        /// Binning模式列表
        /// </summary>
        private static readonly List<string> BinningModeList = new()
        {
            "1 x 1 Bin (Full Resolution)",
            "2 x 2 Bin (1/4 Resolution, 4x Brightness)",
            "4 x 4 Bin (1/16 Resolution, 16x Brightness)"
        };

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

                // 读取实际设置的值（可能被调整）
                var actualROI = _camera.GetROI();
                Console.WriteLine($"[INFO] ROI set: {actualROI.Width}x{actualROI.Height} at ({actualROI.OffsetX}, {actualROI.OffsetY})");

                if (wasCapturing)
                    StartCapture();
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "设置ROI");
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

                _camera.ResetROI();

                if (wasCapturing)
                    StartCapture();

                Console.WriteLine("[INFO] ROI reset to full sensor area");
                return true;
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "重置ROI");
                return false;
            }
        }

        public List<string> ROIList => new()
        {
            "2048 x 2048 (Full Sensor)",
            "1024 x 1024 (Center)",
            "512 x 512 (Center)",
            "256 x 256 (Center)",
            "自定义ROI"
        };

        #endregion

        #region 图像模式（ReadoutMode）

        public bool SetImageMode(int imageMode)
        {
            if (imageMode < 0 || imageMode >= ImageModesList.Count)
                return false;

            try
            {
                bool wasCapturing = _isCapturing;
                if (wasCapturing)
                    StopCapture();

                // 映射到ReadoutMode
                ulong readoutMode = imageMode switch
                {
                    0 => 0, // bit11_HS_Low
                    1 => 1, // bit11_HS_High
                    2 => 6, // bit12_CMS
                    3 => 7, // bit16_From11
                    _ => 7
                };

                _camera.ReadoutMode = readoutMode;
                _currentReadoutMode = (int)readoutMode;

                if (wasCapturing)
                    StartCapture();

                Console.WriteLine($"[INFO] Image mode set to: {ImageModesList[imageMode]}");
                return true;
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "设置图像模式");
                return false;
            }
        }

        public List<string> ImageModesList => new()
        {
            "11-bit High Speed Low Gain (Bright Scenes)",
            "11-bit High Speed High Gain (Low Light)",
            "12-bit Low Noise Mode (High Quality)",
            "16-bit High Dynamic Range (High Contrast)"
        };

        public bool SetCompositeMode(int mode)
        {
            // Revealer相机没有复合模式的概念
            Console.WriteLine("[INFO] Composite mode not applicable for Revealer camera");
            return true;
        }

        public List<string> CompositeModeList => new() { "Standard" };

        #endregion

        #region 温度控制

        public (double Min, double Max) TemperatureRange
        {
            get
            {
                if (_camera == null)
                    return (-30, 25);

                try
                {
                    if (_camera.IsFeatureAvailable("DeviceTemperatureTarget"))
                    {
                        var range = _camera.GetIntFeatureRange("DeviceTemperatureTarget");
                        return (range.Min, range.Max);
                    }
                }
                catch { }

                return (-30, 25);
            }
        }

        public double Temperature
        {
            get
            {
                if (_camera == null)
                    return 0;

                try
                {
                    return _camera.DeviceTemperature;
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
                    if (_camera.IsFeatureAvailable("DeviceTemperatureTarget") &&
                        _camera.IsFeatureWriteable("DeviceTemperatureTarget"))
                    {
                        int targetTemp = (int)Math.Clamp(value, TemperatureRange.Min, TemperatureRange.Max);
                        _camera.DeviceTemperatureTarget = targetTemp;
                        Console.WriteLine($"[INFO] Target temperature set to: {targetTemp}°C");
                    }
                    else
                    {
                        Console.WriteLine("[WARNING] Temperature control not available");
                    }
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置目标温度");
                }
            }
        }

        /// <summary>
        /// 获取或设置风扇状态
        /// </summary>
        public bool FanEnabled
        {
            get
            {
                if (_camera == null)
                    return false;

                try
                {
                    return _camera.FanSwitch;
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
                    _camera.FanSwitch = value;
                    Console.WriteLine($"[INFO] Fan {(value ? "enabled" : "disabled")}");
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置风扇");
                }
            }
        }

        #endregion

        #region 白平衡（不支持）

        public bool AutoWhiteBlanceOnce()
        {
            Console.WriteLine("[WARNING] Auto white balance not supported on Revealer camera");
            return false;
        }

        public (double Min, double Max) TintRange => (0, 0);

        public double Tint
        {
            get => 0;
            set => Console.WriteLine("[WARNING] Tint control not supported");
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
                else
                {
                    Console.WriteLine($"[ERROR] Failed to save image to: {path}");
                }

                return success;
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "保存图像");
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
                Console.WriteLine($"[WARNING] Failed to set image processing feature {feature}: {ex.Message}");
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

            // 触发断开连接事件
            OnDisConnectState?.Invoke(false);
        }

        /// <summary>
        /// 下载GenICam XML配置文件（用于调试）
        /// </summary>
        public bool DownloadGenICamXML(string filePath)
        {
            if (_camera == null)
                return false;

            try
            {
                _camera.DownloadGenICamXML(filePath);
                Console.WriteLine($"[INFO] GenICam XML downloaded to: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "下载GenICam XML");
                return false;
            }
        }

        #endregion
    }

    /// <summary>
    /// 图像处理功能枚举
    /// </summary>
    public enum ImageProcessingFeature
    {
        Brightness = 0,    // 亮度
        Contrast = 1,      // 对比度
        Gamma = 2,         // Gamma
        PseudoColor = 3,   // 伪彩
        Rotation = 4,      // 旋转
        Flip = 5           // 翻转
    }
}