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
        private readonly int _deviceIndex;
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
        private int _currentReadoutMode = 7; 

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

                //注册连接状态回调
                _camera.AttachConnectCallback(OnConnectionStateChanged);

                //注册参数更新回调
                _camera.AttachParamUpdateCallback(OnParameterUpdated);

                // 获取设备信息
                var deviceInfo = _camera.GetDeviceInfo();
                Console.WriteLine($"[INFO] Camera opened: {deviceInfo.ModelName} (SN: {deviceInfo.SerialNumber})");

                // 初始化默认设置
                InitDefaultSettings();

                return true;
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "相机初始化");
                return false;
            }
        }

        private void OnParameterUpdated(string featureName)
        {
            try
            {
                Console.WriteLine($"[INFO] Parameter updated: {featureName}");

                // 可以在这里处理参数变化
                // 例如：当Width变化时，自动更新其他相关参数
                switch (featureName)
                {
                    case "Width":
                    case "Height":
                        // 图像尺寸变化，可能影响帧率
                        Console.WriteLine($"[INFO] Image size changed, current: {ImageSize.Width}x{ImageSize.Height}");
                        break;
                    case "OffsetX":
                    case "OffsetY":
                        // ROI偏移变化
                        Console.WriteLine($"[INFO] ROI offset changed");
                        break;
                    case "AcquisitionFrameRate":
                        // 帧率变化（注意：此参数可能不会触发回调）
                        Console.WriteLine($"[INFO] Frame rate changed: {FrameRate:F2} fps");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Parameter update callback exception: {ex.Message}");
            }
        }

        private void OnConnectionStateChanged(bool isConnected, string cameraKey)
        {
            try
            {
                Console.WriteLine($"[INFO] Camera connection state changed: {(isConnected ? "Connected" : "Disconnected")} (Key: {cameraKey})");

                if (!isConnected)
                {
                    Console.WriteLine("[WARNING] Camera disconnected!");

                    if (_isCapturing)
                    {
                        _isCapturing = false;
                        _fpsStopwatch.Stop();
                    }

                    OnDisConnectState?.Invoke(false);
                }
                else
                {
                    Console.WriteLine("[INFO] Camera reconnected!");
                    OnDisConnectState?.Invoke(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Connection state callback exception: {ex.Message}");
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

                //// 1. 设置ROI为最大值（全传感器区域）
                //_camera.ResetROI();
                //Console.WriteLine($"[INFO] Sensor size: {_camera.SensorWidth}x{_camera.SensorHeight}");

                //// 2. 设置读出模式为高动态（16位）- 使用索引7
                //_camera.ReadoutMode = 7; // 16位高动态范围
                //_currentReadoutMode = 7;
                //Console.WriteLine($"[INFO] Readout mode: {EyeCam.Shared.Revealer.ReadoutModeList[7]}");

                //// 3. 设置Binning模式为1x1（全分辨率）- 使用索引0
                //_camera.BinningMode = 0;
                //Console.WriteLine($"[INFO] Binning mode: {EyeCam.Shared.Revealer.BinningModeList[0]}");

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

                //// 5. 设置曝光时间（默认10ms）
                //_camera.ExposureTime = 50000; // 10ms = 10000us
                //Console.WriteLine($"[INFO] Exposure time: {_camera.ExposureTime / 1000.0}ms");

                //// 6. 禁用帧率限制（最大速度采集）
                //_camera.FrameRateEnable = false;
                //Console.WriteLine("[INFO] Frame rate control: Disabled (Max speed)");

                // 7. 设置为连续采集模式（关闭触发）- 使用索引0
                _camera.TriggerInType = 0; // Off
                Console.WriteLine($"[INFO] Trigger mode: {EyeCam.Shared.Revealer.TriggerInTypeList[0]}");

                // 8. 禁用图像处理功能（使用原始数据）
                for (int i = 0; i < 6; i++)
                {
                    _camera.SetImageProcessingEnabled(i, false);
                }
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
                    Console.WriteLine(_camera.PixelFormatSymbol);
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
                _camera.SetBufferCount(10);

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

        private void OnFrameCallback(Mat mat) // ✅ 参数改为 Mat
        {
            try
            {
                // ✅ 直接使用Mat，无需转换
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
                if (_fpsStopwatch.Elapsed.TotalSeconds >= 1.0)
                {
                    _calculatedFps = _frameCount / _fpsStopwatch.Elapsed.TotalSeconds;
                    _frameCount = 0;
                    _fpsStopwatch.Restart();
                }

                // ✅ 注意：mat会在Revealer的回调中自动释放，这里不需要手动释放
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
                    var previousMode = _camera.TriggerInType;

                    // 配置软件触发 - 使用索引5
                    _camera.TriggerInType = 5; // Software
                    //_camera.SetBufferCount(3);
                    _camera.StartGrabbing();

                    // 发送软件触发
                    _camera.SoftwareTrigger();

                    // ✅ 直接获取Mat（带超时）
                    img = _camera.GetProcessedFrame(5000);

                    // 停止采集
                    _camera.StopGrabbing();

                    // 恢复之前的触发模式
                    _camera.TriggerInType = previousMode;

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
                        // 启用自动曝光：中央模式，目标灰度128
                        _camera.SetAutoExposureParam(0, 128); // 0 = Center
                        int actualGray = _camera.ExecuteAutoExposure();
                        Console.WriteLine($"[INFO] Auto exposure executed, target gray: {actualGray}");
                    }
                    else
                    {
                        // 关闭自动曝光
                        _camera.SetAutoExposureParam(2, -1); // 2 = Off
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
                    if (!_camera.GetImageProcessingEnabled(2)) return 56;
                    return _camera.GetImageProcessingValue(2); // 2 = Gamma
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
                    if (clampedValue == 56)
                    {
                        _camera.SetImageProcessingEnabled(2, false);
                    }
                    else
                    {
                        _camera.SetImageProcessingEnabled(2, true);
                        _camera.SetImageProcessingValue(2, clampedValue); // 2 = Gamma
                    }

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
                    if (!_camera.GetImageProcessingEnabled(1)) return 50;
                    return _camera.GetImageProcessingValue(1); // 1 = Contrast
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

                    if (clampedValue == 50)
                    {
                        _camera.SetImageProcessingEnabled(1, false);
                    }
                    else
                    {
                        _camera.SetImageProcessingEnabled(1, true);
                        _camera.SetImageProcessingValue(1, clampedValue); // 1 = Contrast
                    }
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
                    if (!_camera.GetImageProcessingEnabled(0)) return 50;
                    return _camera.GetImageProcessingValue(0); // 0 = Brightness
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
                    if (clampedValue == 50)
                    {
                        _camera.SetImageProcessingEnabled(0, false);
                    }
                    else
                    {
                        _camera.SetImageProcessingEnabled(0, true);
                        _camera.SetImageProcessingValue(0, clampedValue); 
                    }
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置亮度");
                }
            }
        }

        #endregion

        #region 增益控制

        public ushort Gain { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public (ushort Min, ushort Max) GainRange => throw new NotImplementedException();

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
                    return mode > 0; // 0=Off, 其他为自动
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
                    _camera.SetAutoLevels(value ? 3 : 0); // 3=LeftRight, 0=Off

                    if (value)
                    {
                        Console.WriteLine($"[INFO] Auto levels enabled: {EyeCam.Shared.Revealer.AutoLevelModeList[3]}");
                    }
                    else
                    {
                        Console.WriteLine($"[INFO] Auto levels disabled: {EyeCam.Shared.Revealer.AutoLevelModeList[0]}");
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

        public bool FrameRateEnable
        {
            get
            {
                if (_camera == null)
                    return false;

                try
                {
                    return _camera.FrameRateEnable;
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
                    _camera.FrameRateEnable = value;
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置帧率限制");
                }
            }
        }

        public double FrameRateLimit
        {
            get
            {
                if (_camera == null) return 0;
                return _camera.AcquisitionFrameRate;
            }
            set
            {
                if (_camera == null) return;

                if (value > _camera.AcquisitionFrameRate)
                    value = _camera.AcquisitionFrameRate;

                _camera.AcquisitionFrameRate = value;
            }
        }

        public double FpsCal => _calculatedFps;

        public List<string > FlipList => EyeCam.Shared.Revealer.FlipModeList;

        public int FlipIndex
        {
            get
            {
                if (_camera == null)
                    return 0;
                try
                {
                    // 检查翻转是否启用
                    bool isEnabled = _camera.GetImageProcessingEnabled(5);
                    if (!isEnabled)
                        return 0; // 默认，无翻转

                    // 获取翻转模式值
                    int flipValue = _camera.GetImageProcessingValue(5);
                    // API值映射：0->垂直, 1->水平, 2->垂直+水平
                    // 列表索引：0->默认, 1->垂直, 2->水平, 3->垂直+水平
                    return flipValue + 1; // API值+1就是列表索引
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
                    // 列表索引：0->默认, 1->垂直, 2->水平, 3->垂直+水平
                    switch (value)
                    {
                        case 0: // 默认，无翻转
                            _camera.SetImageProcessingEnabled(5, false);
                            break;
                        case 1: // 垂直翻转
                            _camera.SetImageProcessingEnabled(5, true);
                            _camera.SetImageProcessingValue(5, 0);
                            break;
                        case 2: // 水平翻转
                            _camera.SetImageProcessingEnabled(5, true);
                            _camera.SetImageProcessingValue(5, 1);
                            break;
                        case 3: // 垂直+水平翻转
                            _camera.SetImageProcessingEnabled(5, true);
                            _camera.SetImageProcessingValue(5, 2);
                            break;
                        default:
                            _camera.SetImageProcessingEnabled(5, false);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置翻转模式");
                }
            }
        }

        public bool IsFlipHorizontally
        {
            get
            {
                if (_camera == null)
                    return false;

                try
                {
                  
                    return _camera.GetImageProcessingValue(5)==1; // 5 = Flip
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
                    _camera.SetImageProcessingEnabled(5, value); // 5 = Flip

                    if (value)
                    {
                        _camera.SetImageProcessingValue(5, 1); // 1 = Horizontal
                    }
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置水平翻转");
                }
            }
        }

        public bool IsFlipVertially
        {
            get => _camera!.GetImageProcessingValue(5) == 0;
            set
            {
                if (_camera == null)
                    return;

                try
                {
                    _camera.SetImageProcessingEnabled(5, value); // 5 = Flip

                    if (value)
                    {
                        _camera.SetImageProcessingValue(5, 0); // 0 = Vertical
                    }
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置垂直翻转");
                }
            }
        }

        public int ClockwiseRotation
        {
            get
            {
                if (_camera == null)
                    return 0;

                try
                {
                    int modeValue = _camera.GetImageProcessingValue(4); // 4 = Rotation
                    return modeValue switch
                    {
                        0 => 0,
                        1 => 90,
                        2 => 180,
                        3 => 270,
                        _ => 0
                    };
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
                    _camera.SetImageProcessingEnabled(4, true);

                    int rotation = value % 360;
                    if (rotation < 0) rotation += 360;

                    int mode = rotation switch
                    {
                        0 => 0,
                        90 => 1,
                        180 => 2,
                        270 => 3,
                        _ => 0
                    };

                    _camera.SetImageProcessingValue(4, mode); // 4 = Rotation

                    if (mode == 0) _camera.SetImageProcessingEnabled(4, false);

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
                    return 0;//无伪彩

                try
                {
                    if (!_camera.GetImageProcessingEnabled(3)) return 0;
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
                    if (value == 0)
                    {
                        _camera.SetImageProcessingEnabled(3, false); //设置为该项使能关闭
                    }
                    else
                    {
                        _camera.SetImageProcessingEnabled(3, true); // 3 = PseudoColor
                        int mode = Math.Clamp(value, 0, EyeCam.Shared.Revealer.PseudoColorMapList.Count) - 1;//第一个是新增的“默认选项”
                        _camera.SetPseudoColorMap(mode);
                    }
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置伪彩");
                }
            }
        }

        public List<string> PseudoColorList => EyeCam.Shared.Revealer.PseudoColorMapList;

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
            if (_camera == null) return false;

            if (resolution < 0 || resolution >= EyeCam.Shared.Revealer.BinningModeList.Count)
                return false;

            try
            {
                bool wasCapturing = _isCapturing;
                if (wasCapturing)
                    StopCapture();

                // 设置Binning模式
                _camera!.BinningMode = (ulong)resolution;

                if (wasCapturing)
                    StartCapture();

                Console.WriteLine($"[INFO] Binning mode set to: {EyeCam.Shared.Revealer.BinningModeList[resolution]}");
                return true;
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "设置分辨率模式");
                return false;
            }
        }

        public List<string> ResolutionsList => EyeCam.Shared.Revealer.BinningModeList;

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
            // 索引映射：0->0, 1->1, 2->6, 3->7
            int[] modeMap = { 0, 1, 6, 7 };

            if (imageMode < 0 || imageMode >= modeMap.Length)
                return false;

            try
            {
                bool wasCapturing = _isCapturing;
                if (wasCapturing)
                    StopCapture();

                int actualMode = modeMap[imageMode];
                _camera!.ReadoutMode = (ulong)actualMode;
                _currentReadoutMode = actualMode;

                if (wasCapturing)
                    StartCapture();

                Console.WriteLine($"[INFO] Image mode set to: {EyeCam.Shared.Revealer.ReadoutModeList[actualMode]}");
                return true;
            }
            catch (Exception ex)
            {
                HandleCameraException(ex, "设置图像模式");
                return false;
            }
        }

        public int ImageModeIndex
        {
            get
            {
                if (_camera == null) return 0;
                // 索引映射：0->0, 1->1, 2->6, 3->7
                int[] modeMap = { 0, 1, 6, 7 };
                int actualMode = (int)_camera.ReadoutMode;
                int index = Array.IndexOf(modeMap, actualMode);
                return index >= 0 ? index : 0; // 如果找不到，返回默认值0
            }
            set
            {
                // 索引映射：0->0, 1->1, 2->6, 3->7
                int[] modeMap = { 0, 1, 6, 7 };
                try
                {
                    bool wasCapturing = _isCapturing;
                    if (wasCapturing)   StopCapture();
                     
                    int actualMode = modeMap[value];
                    _camera!.ReadoutMode = (ulong)actualMode;
                    _currentReadoutMode = actualMode;

                    if (wasCapturing)
                        StartCapture();

                    Console.WriteLine($"[INFO] Image mode set to: {EyeCam.Shared.Revealer.ReadoutModeList[actualMode]}");
                }
                catch (Exception ex)
                {
                    HandleCameraException(ex, "设置图像模式");
                }
            }
        }

        public List<string> ImageModesList => new()
        {
            EyeCam.Shared.Revealer.ReadoutModeList[0],  // 11位高速低增益
            EyeCam.Shared.Revealer.ReadoutModeList[1],  // 11位高速高增益
            EyeCam.Shared.Revealer.ReadoutModeList[6],  // 12位低噪声
            EyeCam.Shared.Revealer.ReadoutModeList[7]   // 16位高动态
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

        public List<string> GainList => throw new NotImplementedException();

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
}