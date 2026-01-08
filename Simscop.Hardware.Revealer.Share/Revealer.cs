using EyeCam.Shared.Native;
using OpenCvSharp;
using System.Diagnostics;
using System.Text;

namespace EyeCam.Shared
{
    /// <summary>
    /// 相机类 - 基于 Revealer.dll 的完整封装
    /// </summary>
    public class Revealer : IDisposable
    {
        private IntPtr _handle = IntPtr.Zero;
        private bool _disposed = false;
        private bool _isGrabbing = false;
        private static bool _sdkInitialized = false;
        private static readonly object _sdkLock = new object();

        // 回调委托需要保持引用，防止被GC回收
        private NativeMethods.ConnectCallBackDelegate? _connectCallback;
        private NativeMethods.ParamUpdateCallBackDelegate? _paramUpdateCallback;
        private NativeMethods.ExportEventCallBackDelegate? _exportCallback;
        private NativeMethods.FrameCallBackDelegate? _frameCallback;

        #region 常量定义

        /// <summary>
        /// 读出模式列表
        /// 0 = bit11_HS_Low    - 11位高速低增益（适合明亮场景）
        /// 1 = bit11_HS_High   - 11位高速高增益（适合弱光场景）
        /// 6 = bit12_CMS       - 12位低噪声模式（高质量）
        /// 7 = bit16_From11    - 16位高动态范围（高对比度场景）
        /// </summary>
        public static readonly List<string> ReadoutModeList = new()
        {
            "11位高速低增益",      // 0
            "11位高速高增益",      // 1
            "",                   // 2 - 未使用
            "",                   // 3 - 未使用
            "",                   // 4 - 未使用
            "",                   // 5 - 未使用
            "12位低噪声模式",      // 6
            "16位高动态范围"       // 7
         };

        /// <summary>
        /// Binning模式列表
        /// </summary>
        public static readonly List<string> BinningModeList = new()
        {
            "1 x 1 Bin (全分辨率)",                    // 0
            "2 x 2 Bin (1/4分辨率, 4倍亮度)",         // 1
            "4 x 4 Bin (1/16分辨率, 16倍亮度)"        // 2
        };

        /// <summary>
        /// 伪彩映射模式列表
        /// </summary>
        public static readonly List<string> PseudoColorMapList = new()
        {
            //"HSV",                // 0: HSV色彩映射
            //"Jet (Matlab风格)",   // 1: Jet色彩映射
            //"红色渐变",           // 2: 红色渐变
            //"绿色渐变",           // 3: 绿色渐变
            //"蓝色渐变"            // 4: 蓝色渐变

            //实测伪彩列表

            "灰度-无伪彩",  //额外添加-5
            "HSV",                // 0: HSV色彩映射
            "Jet (Matlab风格)",   // 1: Jet色彩映射
            "蓝色渐变",            // 2: 蓝色渐变 
            "绿色渐变",           // 3: 绿色渐变
            "红色渐变",           // 4: 红色渐变 
           
        };

        /// <summary>
        /// 自动曝光模式列表
        /// </summary>
        public static readonly List<string> AutoExposureModeList = new()
        {
            "中央区域",           // 0: 中央模式
            "右侧区域",           // 1: 右侧模式
            "关闭"                // 2: 关闭自动曝光
        };

        /// <summary>
        /// 自动色阶模式列表
        /// </summary>
        public static readonly List<string> AutoLevelModeList = new()
        {
            "关闭",               // 0: 关闭
            "右色阶",             // 1: 右色阶
            "左色阶",             // 2: 左色阶
            "左右色阶"            // 3: 左右色阶
        };

        /// <summary>
        /// 触发输入类型列表
        /// </summary>
        public static readonly List<string> TriggerInTypeList = new()
        {
            "关闭触发",                   // 0
            "外部边缘触发",               // 1
            "外部开始触发",               // 2
            "外部电平触发",               // 3
            "同步读出",                   // 4
            "软件触发"                    // 5
        };

        /// <summary>
        /// 触发激活方式列表
        /// </summary>
        public static readonly List<string> TriggerActivationList = new()
        {
            "上升沿",            // 0
            "下降沿",            // 1
            "高电平",            // 2
            "低电平"             // 3
        };

        /// <summary>
        /// 旋转模式列表
        /// </summary>
        public static readonly List<string> RotationModeList = new()
        {
            "0度",               // 0
            "90度",              // 1
            "180度",             // 2
            "270度"              // 3
        };

        /// <summary>
        /// 翻转模式列表
        /// </summary>
        public static readonly List<string> FlipModeList = new()
        {
            "默认",//新增，默认无设置的状态
            "垂直翻转",          // 0
            "水平翻转",          // 1
            "垂直+水平翻转"              // 2
        };

        /// <summary>
        /// 录像格式列表
        /// </summary>
        public static readonly List<string> RecordFormatList = new()
        {
            "TIFF (多文件)",             // 0
            "BMP (暂不支持)",            // 1
            "SCD",                      // 2
            "TIFF Video (单文件)"       // 3
        };

        /// <summary>
        /// 像素格式列表（常用）
        /// </summary>
        public static readonly Dictionary<string, int> PixelFormatMap = new()
        {
            // 单色格式 (Mono)
            ["Mono8"] = 0x01080001,  // 8位单色  (17301505)
            ["Mono10"] = 0x01100003,  // 10位单色 (17891331) - unpacked
            ["Mono12"] = 0x01100005,  // 12位单色 (17891333) - unpacked
            ["Mono12p"] = 0x010C0047,  // 12位单色 (17498183) - packed
            ["Mono16"] = 0x01100007,  // 16位单色 (17825799) ✅ 修正

            // 彩色格式 (RGB/BGR)
            ["RGB8"] = 0x02180014,  // 24位RGB (35389460)
            ["BGR8"] = 0x02180015,  // 24位BGR (35389461)

            // Bayer格式（常用于彩色相机）
            ["BayerGR8"] = 0x01080008,  // Bayer GR 8位
            ["BayerRG8"] = 0x01080009,  // Bayer RG 8位
            ["BayerGB8"] = 0x0108000A,  // Bayer GB 8位
            ["BayerBG8"] = 0x0108000B,  // Bayer BG 8位
            ["BayerGR12"] = 0x01100010,  // Bayer GR 12位
            ["BayerRG12"] = 0x01100011,  // Bayer RG 12位
            ["BayerGB12"] = 0x01100012,  // Bayer GB 12位
            ["BayerBG12"] = 0x01100013,  // Bayer BG 12位
        };

        /// <summary>
        /// 图像处理功能列表
        /// </summary>
        public static readonly List<string> ImageProcessingFeatureList = new()
        {
            "亮度",              // 0: Brightness [-100, 100]
            "对比度",            // 1: Contrast [0, 100]
            "Gamma",            // 2: Gamma [0, 100]
            "伪彩",              // 3: PseudoColor
            "旋转",              // 4: Rotation
            "翻转"               // 5: Flip
        };

        #endregion

        #region 静态方法（SDK级别）

        /// <summary>
        /// 初始化SDK（全局调用一次）
        /// </summary>
        /// <param name="logLevel">日志级别: 0=Off, 1=Error, 2=Warn, 3=Info, 4=Debug</param>
        /// <param name="logPath">日志路径</param>
        /// <param name="fileSize">单个日志文件大小(字节)</param>
        /// <param name="fileNum">日志文件数量</param>
        public static void Initialize(int logLevel = 3, string logPath = ".",
            uint fileSize = 10485760, uint fileNum = 1)
        {
            lock (_sdkLock)
            {
                if (_sdkInitialized) return;

                int ret = NativeMethods.Camera_Initialize(logLevel, logPath, fileSize, fileNum);
                if (ret != 0)
                    throw new CameraException(ret);

                _sdkInitialized = true;
            }
        }

        /// <summary>释放SDK（程序退出时调用）</summary>
        public static void Release()
        {
            lock (_sdkLock)
            {
                if (!_sdkInitialized) return;
                NativeMethods.Camera_Release();
                _sdkInitialized = false;
            }
        }

        /// <summary>枚举所有相机设备</summary>
        /// <param name="interfaceType">接口类型: 0=All, 1=USB3, 2=CXP, 3=Custom</param>
        public static List<CameraInfo> EnumerateDevices(uint interfaceType = 0)
        {
            EnsureInitialized();

            int ret = NativeMethods.Camera_EnumDevices(out int count, interfaceType);
            if (ret != 0)
                throw new CameraException(ret);

            var devices = new List<CameraInfo>();
            for (int i = 0; i < count; i++)
            {
                var name = new StringBuilder(256);
                ret = NativeMethods.Camera_GetDeviceName(i, name, name.Capacity);
                if (ret == 0)
                {
                    devices.Add(new CameraInfo
                    {
                        Index = i,
                        Name = name.ToString()
                    });
                }
            }

            return devices;
        }

        /// <summary>获取SDK版本</summary>
        public static string GetVersion()
        {
            return NativeMethods.GetVersionString();
        }

        private static void EnsureInitialized()
        {
            if (!_sdkInitialized)
                throw new InvalidOperationException("请先调用 EyeCamera.Initialize() 初始化SDK");
        }

        #endregion

        #region 构造与析构

        /// <summary>创建相机实例</summary>
        /// <param name="deviceIndex">设备索引</param>
        public Revealer(int deviceIndex)
        {
            EnsureInitialized();

            var devices = EnumerateDevices();
            if (devices.Count < 2) throw new Exception("无法检测出相机！");

            int ret = NativeMethods.Camera_CreateHandle(out _handle, deviceIndex);//检测出虚拟相机+真实相机，选用真实相机
            if (ret != 0 || _handle == IntPtr.Zero)
                throw new CameraException(ret);

            Console.WriteLine( _handle);
        }

        ~Revealer() => Dispose(false);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (_handle != IntPtr.Zero)
            {
                try
                {
                    if (_isGrabbing) StopGrabbing();
                    Close();
                    NativeMethods.Camera_DestroyHandle(_handle);
                }
                catch { }
                _handle = IntPtr.Zero;
            }

            // 释放回调委托引用
            _connectCallback = null;
            _paramUpdateCallback = null;
            _exportCallback = null;
            _frameCallback = null;

            _disposed = true;
        }

        #endregion

        #region 基本操作

        /// <summary>打开相机</summary>
        public void Open()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_Open(_handle);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>关闭相机</summary>
        public void Close()
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.Camera_Close(_handle);
            }
        }

        /// <summary>获取设备详细信息</summary>
        public DeviceInfo GetDeviceInfo()
        {
            CheckDisposed();

            var info = new NativeMethods.DeviceInfo();
            int ret = NativeMethods.Camera_GetDeviceInfo(_handle, ref info);
            if (ret != 0)
                throw new CameraException(ret);

            return new DeviceInfo
            {
                CameraName = info.cameraName,
                SerialNumber = info.serialNumber,
                ModelName = info.modelName,
                ManufacturerInfo = info.manufacturerInfo,
                DeviceVersion = info.deviceVersion
            };
        }

        /// <summary>下载GenICam XML配置文件</summary>
        public void DownloadGenICamXML(string filePath)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_DownloadGenICamXML(_handle, filePath);
            if (ret != 0)
                throw new CameraException(ret);
        }

        #endregion

        #region 采集控制

        /// <summary>开始采集</summary>
        public void StartGrabbing()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_StartGrabbing(_handle);
            if (ret != 0)
                throw new CameraException(ret);
            _isGrabbing = true;
        }

        /// <summary>停止采集</summary>
        public void StopGrabbing()
        {
            if (_handle != IntPtr.Zero && _isGrabbing)
            {
                NativeMethods.Camera_StopGrabbing(_handle);
                _isGrabbing = false;
            }
        }

        /// <summary>是否正在采集</summary>
        public bool IsGrabbing
        {
            get
            {
                if (_handle == IntPtr.Zero) return false;
                int ret = NativeMethods.Camera_IsGrabbing(_handle, out int isGrabbing);
                return ret == 0 && isGrabbing != 0;
            }
        }

        /// <summary>设置帧缓冲区数量</summary>
        public void SetBufferCount(uint count)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetBufferCount(_handle, count);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>获取一帧图像（同步）</summary>
        /// <param name="timeout">超时时间(毫秒), 0xFFFFFFFF表示无限等待</param>
        public ImageFrame GetFrame(uint timeout = 5000)
        {
            CheckDisposed();

            var imageData = new NativeMethods.ImageData();
            int ret = NativeMethods.Camera_GetFrame(_handle, ref imageData, timeout);

            if (ret != 0)
                throw new CameraException(ret);

            try
            {
                var frame = new ImageFrame(imageData);
                return frame;
            }
            finally
            {
                // 释放SDK内部的帧资源
                NativeMethods.Camera_ReleaseFrame(_handle, ref imageData);
            }
        }

        /// <summary>获取处理后的图像（同步）</summary>
        /// <param name="timeout">超时时间(毫秒)</param>
        public Mat GetProcessedFrame(uint timeout = 5000)
        {
            CheckDisposed();

            var imageData = new NativeMethods.ImageData();
            int ret = NativeMethods.Camera_GetProcessedFrame(_handle, ref imageData, timeout);

            if (ret != 0)
                throw new CameraException(ret);

            try
            {
                // ✅ 传递当前的 ReadoutMode
                Mat? mat = ConvertImageDataToMat(ref imageData, this.ReadoutMode);
                if (mat == null)
                    throw new CameraException("图像转换失败");

                return mat;
            }
            finally
            {
                NativeMethods.Camera_ReleaseFrame(_handle, ref imageData);
            }
        }

        #endregion

        #region 录像功能

        /// <summary>开始录像</summary>
        /// <param name="recordFilePath">保存路径</param>
        /// <param name="fileName">文件名</param>
        /// <param name="recordFormat">0=TIFF, 1=BMP(暂不支持), 2=SCD, 3=TIFFVideo</param>
        /// <param name="quality">质量 0-100</param>
        /// <param name="frameRate">帧率</param>
        /// <param name="startFrame">起始帧（默认0）</param>
        /// <param name="count">采集帧数（0=持续录制）</param>
        public void StartRecording(string recordFilePath, string fileName,
            int recordFormat = 0, int quality = 90, int frameRate = 30,
            uint startFrame = 0, uint count = 0)
        {
            CheckDisposed();

            var param = new NativeMethods.RecordParam
            {
                recordFilePath = recordFilePath,
                fileName = fileName,
                recordFormat = recordFormat,
                quality = quality,
                frameRate = frameRate,
                startFrame = startFrame,
                count = count
            };

            int ret = NativeMethods.Camera_OpenRecord(_handle, ref param);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>停止录像</summary>
        public void StopRecording()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_CloseRecord(_handle);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>设置导出缓存大小</summary>
        public void SetExportCacheSize(ulong cacheSizeInByte)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetExportCacheSize(_handle, cacheSizeInByte);
            if (ret != 0)
                throw new CameraException(ret);
        }

        #endregion

        #region 自动功能

        /// <summary>设置自动曝光参数</summary>
        /// <param name="mode">0=中央, 1=右侧, 2=关闭</param>
        /// <param name="targetGray">目标灰度值, -1=默认</param>
        public void SetAutoExposureParam(int mode, int targetGray = -1)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetAutoExposureParam(_handle, mode, targetGray);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>执行自动曝光</summary>
        /// <returns>实际目标灰度值</returns>
        public int ExecuteAutoExposure()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_AutoExposure(_handle, out int actualGray);
            if (ret != 0)
                throw new CameraException(ret);
            return actualGray;
        }

        /// <summary>设置自动色阶模式</summary>
        /// <param name="mode">0=关闭, 1=右, 2=左, 3=左右</param>
        public void SetAutoLevels(int mode)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetAutoLevels(_handle, mode);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>获取自动色阶模式</summary>
        public int GetAutoLevels()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetAutoLevels(_handle, out int mode);
            if (ret != 0)
                throw new CameraException(ret);
            return mode;
        }

        /// <summary>设置色阶阈值</summary>
        /// <param name="mode">1=右色阶, 2=左色阶</param>
        /// <param name="value">阈值</param>
        public void SetAutoLevelValue(int mode, int value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetAutoLevelValue(_handle, mode, value);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>获取色阶阈值</summary>
        /// <param name="mode">1=右色阶, 2=左色阶</param>
        public int GetAutoLevelValue(int mode)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetAutoLevelValue(_handle, mode, out int value);
            if (ret != 0)
                throw new CameraException(ret);
            return value;
        }

        /// <summary>执行一次自动色阶</summary>
        /// <param name="mode">1=右, 2=左, 3=左右</param>
        public void ExecuteAutoLevel(int mode)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_ExecuteAutoLevel(_handle, mode);
            if (ret != 0)
                throw new CameraException(ret);
        }

        #endregion

        #region 图像处理

        /// <summary>设置图像处理功能使能</summary>
        /// <param name="feature">0=亮度, 1=对比度, 2=Gamma, 3=伪彩, 4=旋转, 5=翻转</param>
        /// <param name="enable">是否使能</param>
        public void SetImageProcessingEnabled(int feature, bool enable)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetImageProcessingEnabled(_handle, feature, enable ? 1 : 0);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>获取图像处理功能使能状态</summary>
        /// <param name="feature">0=亮度, 1=对比度, 2=Gamma, 3=伪彩, 4=旋转, 5=翻转</param>
        public bool GetImageProcessingEnabled(int feature)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetImageProcessingEnabled(_handle, feature, out int enable);
            if (ret != 0)
                throw new CameraException(ret);
            return enable != 0;
        }

        /// <summary>设置图像处理参数值</summary>
        /// <param name="feature">功能枚举</param>
        /// <param name="value">参数值</param>
        public void SetImageProcessingValue(int feature, int value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetImageProcessingValue(_handle, feature, value);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>获取图像处理参数值</summary>
        /// <param name="feature">功能枚举</param>
        public int GetImageProcessingValue(int feature)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetImageProcessingValue(_handle, feature, out int value);
            if (ret != 0)
                throw new CameraException(ret);
            return value;
        }

        /// <summary>设置伪彩映射模式</summary>
        /// <param name="mapMode">0=HSV, 1=Jet, 2=Red, 3=Green, 4=Blue</param>
        public void SetPseudoColorMap(int mapMode)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetPseudoColorMap(_handle, mapMode);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>获取伪彩映射模式</summary>
        public int GetPseudoColorMap()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetPseudoColorMap(_handle, out int mapMode);
            if (ret != 0)
                throw new CameraException(ret);
            return mapMode;
        }

        #endregion

        #region 回调注册（异步采集）

        /// <summary>注册设备连接状态回调函数</summary>
        /// <param name="callback">回调函数，参数为 (isConnected, cameraKey)</param>
        public void AttachConnectCallback(Action<bool, string> callback)
        {
            CheckDisposed();

            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            // 创建委托并保持引用（防止GC）
            _connectCallback = (int isConnected, string cameraKey, IntPtr pUser) =>
            {
                try
                {
                    bool connected = isConnected != 0;
                    callback(connected, cameraKey);
                }
                catch (Exception ex)
                {
                    // 回调中的异常需要记录，避免崩溃
                    System.Diagnostics.Debug.WriteLine($"连接状态回调异常: {ex.Message}");
                }
            };

            int ret = NativeMethods.Camera_SubscribeConnectArg(_handle, _connectCallback, IntPtr.Zero);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>注册参数更新回调函数</summary>
        /// <param name="callback">回调函数，参数为属性名</param>
        public void AttachParamUpdateCallback(Action<string> callback)
        {
            CheckDisposed();

            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            _paramUpdateCallback = (string featureName, IntPtr pUser) =>
            {
                try
                {
                    callback(featureName);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"参数更新回调异常: {ex.Message}");
                }
            };

            int ret = NativeMethods.Camera_SubscribeParamUpdateArg(_handle, _paramUpdateCallback, IntPtr.Zero);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>注册导出状态回调函数</summary>
        /// 录像功能，不注册实现
        /// <param name="callback">回调函数，参数为 (status, progress)</param>
        public void AttachExportCallback(Action<int, int> callback)
        {
            CheckDisposed();

            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            _exportCallback = (int status, int progress, IntPtr pUser) =>
            {
                try
                {
                    callback(status, progress);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"导出状态回调异常: {ex.Message}");
                }
            };

            int ret = NativeMethods.Camera_SubscribeExportNotify(_handle, _exportCallback, IntPtr.Zero);
            if (ret != 0)
                throw new CameraException(ret);
        }

        // ✅ 新增：丢帧保护
        private int _isProcessingFrame = 0;
        private long _totalFramesReceived = 0;
        private long _totalFramesDropped = 0;
        private long _totalFramesProcessed = 0;

        /// <summary>注册处理后图像数据回调函数（异步）</summary>
        /// <remarks>
        /// 重要改进：
        /// 1. 立即转换为Mat对象
        /// 2. 异步执行用户回调，不阻塞 SDK 线程
        /// 3. 防止处理积压导致 Buffer Full
        /// </remarks>
        public void AttachProcessedGrabbing(Action<Mat> callback) // ✅ 改为 Action<Mat>
        {
            CheckDisposed();

            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            _frameCallback = (ref NativeMethods.ImageData imageData, IntPtr pUser) =>
            {
                try
                {
                    Interlocked.Increment(ref _totalFramesReceived);

                    // ✅ 立即转换为Mat（必须在 SDK 回调返回前完成）
                    Mat? mat = ConvertImageDataToMat(ref imageData, this.ReadoutMode);
                    if (mat == null || mat.Empty())
                    {
                        Interlocked.Increment(ref _totalFramesDropped);
                        return;
                    }

                    // ✅ 检查是否有上一帧正在处理（防止积压）
                    if (Interlocked.CompareExchange(ref _isProcessingFrame, 1, 0) != 0)
                    {
                        Interlocked.Increment(ref _totalFramesDropped);
                        mat.Dispose(); // ✅ 释放未使用的Mat

                        // 定期打印统计
                        if (_totalFramesReceived % 100 == 0)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[Revealer] 接收:{_totalFramesReceived} 处理:{_totalFramesProcessed} 丢弃:{_totalFramesDropped}");
                        }
                        return;
                    }

                    // ✅ 异步执行用户回调（关键改进）
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            callback(mat); // ✅ 直接传递Mat
                            Interlocked.Increment(ref _totalFramesProcessed);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"用户回调异常: {ex.Message}");
                        }
                        finally
                        {
                            mat.Dispose(); // ✅ 回调完成后释放Mat
                            Interlocked.Exchange(ref _isProcessingFrame, 0);
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"帧回调异常: {ex.Message}");
                    Interlocked.Exchange(ref _isProcessingFrame, 0);
                }
            };

            int ret = NativeMethods.Camera_AttachProcessedGrabbing(_handle, _frameCallback, IntPtr.Zero);
            if (ret != 0)
                throw new CameraException(ret);
        }

        #endregion

        #region 通用属性访问（GenICam属性）

        /// <summary>检查属性是否可用</summary>
        public bool IsFeatureAvailable(string featureName)
        {
            CheckDisposed();
            return NativeMethods.Camera_FeatureIsAvailable(_handle, featureName) != 0;
        }

        /// <summary>检查属性是否可读</summary>
        public bool IsFeatureReadable(string featureName)
        {
            CheckDisposed();
            return NativeMethods.Camera_FeatureIsReadable(_handle, featureName) != 0;
        }

        /// <summary>检查属性是否可写</summary>
        public bool IsFeatureWriteable(string featureName)
        {
            CheckDisposed();
            return NativeMethods.Camera_FeatureIsWriteable(_handle, featureName) != 0;
        }

        /// <summary>获取属性类型</summary>
        /// <returns>0=Int, 1=Float, 2=Enum, 3=Bool, 4=String, 5=Command</returns>
        public int GetFeatureType(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetFeatureType(_handle, featureName, out int type);
            if (ret != 0)
                throw new CameraException(ret);
            return type;
        }

        /// <summary>获取整型属性值</summary>
        public long GetIntFeature(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetIntFeatureValue(_handle, featureName, out long value);
            if (ret != 0)
                throw new CameraException(ret);
            return value;
        }

        /// <summary>获取整型属性最小值</summary>
        public long GetIntFeatureMin(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetIntFeatureMin(_handle, featureName, out long value);
            if (ret != 0)
                throw new CameraException(ret);
            return value;
        }

        /// <summary>获取整型属性最大值</summary>
        public long GetIntFeatureMax(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetIntFeatureMax(_handle, featureName, out long value);
            if (ret != 0)
                throw new CameraException(ret);
            return value;
        }

        /// <summary>获取整型属性步长</summary>
        public long GetIntFeatureInc(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetIntFeatureInc(_handle, featureName, out long value);
            if (ret != 0)
                throw new CameraException(ret);
            return value;
        }

        /// <summary>设置整型属性值</summary>
        public void SetIntFeature(string featureName, long value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetIntFeatureValue(_handle, featureName, value);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>获取浮点属性值</summary>
        public double GetFloatFeature(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetFloatFeatureValue(_handle, featureName, out double value);
            if (ret != 0)
                throw new CameraException(ret);
            return value;
        }

        /// <summary>获取浮点属性最小值</summary>
        public double GetFloatFeatureMin(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetFloatFeatureMin(_handle, featureName, out double value);
            if (ret != 0)
                throw new CameraException(ret);
            return value;
        }

        /// <summary>获取浮点属性最大值</summary>
        public double GetFloatFeatureMax(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetFloatFeatureMax(_handle, featureName, out double value);
            if (ret != 0)
                throw new CameraException(ret);
            return value;
        }

        /// <summary>获取浮点属性步长</summary>
        public double GetFloatFeatureInc(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetFloatFeatureInc(_handle, featureName, out double value);
            if (ret != 0)
                throw new CameraException(ret);
            return value;
        }

        /// <summary>设置浮点属性值</summary>
        public void SetFloatFeature(string featureName, double value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetFloatFeatureValue(_handle, featureName, value);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>获取枚举属性值</summary>
        public ulong GetEnumFeature(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetEnumFeatureValue(_handle, featureName, out ulong value);
            if (ret != 0)
                throw new CameraException(ret);
            return value;
        }

        /// <summary>设置枚举属性值</summary>
        public void SetEnumFeature(string featureName, ulong value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetEnumFeatureValue(_handle, featureName, value);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>获取枚举属性的可用值数量</summary>
        public uint GetEnumFeatureEntryNum(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetEnumFeatureEntryNum(_handle, featureName, out uint num);
            if (ret != 0)
                throw new CameraException(ret);
            return num;
        }

        /// <summary>获取枚举属性的符号名（Symbol）</summary>
        public string GetEnumFeatureSymbol(string featureName)
        {
            CheckDisposed();
            var symbol = new StringBuilder(256);
            int ret = NativeMethods.Camera_GetEnumFeatureSymbol(_handle, featureName, symbol, symbol.Capacity);
            if (ret != 0)
                throw new CameraException(ret);
            return symbol.ToString();
        }

        /// <summary>通过符号名设置枚举属性</summary>
        public void SetEnumFeatureSymbol(string featureName, string symbol)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetEnumFeatureSymbol(_handle, featureName, symbol);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>获取布尔属性值</summary>
        public bool GetBoolFeature(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetBoolFeatureValue(_handle, featureName, out int value);
            if (ret != 0)
                throw new CameraException(ret);
            return value != 0;
        }

        /// <summary>设置布尔属性值</summary>
        public void SetBoolFeature(string featureName, bool value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetBoolFeatureValue(_handle, featureName, value ? 1 : 0);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>获取字符串属性值</summary>
        public string GetStringFeature(string featureName)
        {
            CheckDisposed();
            var value = new StringBuilder(256);
            int ret = NativeMethods.Camera_GetStringFeatureValue(_handle, featureName, value, value.Capacity);
            if (ret != 0)
                throw new CameraException(ret);
            return value.ToString();
        }

        /// <summary>设置字符串属性值</summary>
        public void SetStringFeature(string featureName, string value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetStringFeatureValue(_handle, featureName, value);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>执行命令属性</summary>
        /// <param name="commandName">命令名称，如 "TriggerSoftware"</param>
        public void ExecuteCommand(string commandName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_ExecuteCommandFeature(_handle, commandName);
            if (ret != 0)
                throw new CameraException(ret);
        }

        #endregion

        #region 便捷属性封装（通过GenICam属性实现）

        // =================================================================
        // 传感器信息（只读）
        // =================================================================

        /// <summary>传感器宽度（像素）- 只读</summary>
        public long SensorWidth => GetIntFeature("SensorWidth");

        /// <summary>传感器高度（像素）- 只读</summary>
        public long SensorHeight => GetIntFeature("SensorHeight");

        // =================================================================
        // 图像尺寸和偏移
        // =================================================================

        /// <summary>图像宽度（像素）</summary>
        /// <remarks>注意：会影响OffsetX、AcquisitionFrameRate等属性</remarks>
        public long Width
        {
            get => GetIntFeature("Width");
            set => SetIntFeature("Width", value);
        }

        /// <summary>图像高度（像素）</summary>
        /// <remarks>注意：会影响OffsetY、AcquisitionFrameRate等属性</remarks>
        public long Height
        {
            get => GetIntFeature("Height");
            set => SetIntFeature("Height", value);
        }

        /// <summary>X偏移（ROI起始X坐标）</summary>
        /// <remarks>注意：会影响Width、AcquisitionFrameRate等属性</remarks>
        public long OffsetX
        {
            get => GetIntFeature("OffsetX");
            set => SetIntFeature("OffsetX", value);
        }

        /// <summary>Y偏移（ROI起始Y坐标）</summary>
        /// <remarks>注意：会影响Height、AcquisitionFrameRate等属性</remarks>
        public long OffsetY
        {
            get => GetIntFeature("OffsetY");
            set => SetIntFeature("OffsetY", value);
        }

        // =================================================================
        // 像素格式
        // =================================================================

        /// <summary>像素格式</summary>
        /// <remarks>
        /// 常用格式：
        /// 17825797 = Mono12  (12位单色)
        /// 17563719 = Mono12p (12位压缩)
        /// 17825799 = Mono16  (16位单色)
        /// 注意：采集时不可更改，影响数据大小和帧率
        /// </remarks>
        public ulong PixelFormat
        {
            get => GetEnumFeature("PixelFormat");
            set => SetEnumFeature("PixelFormat", value);
        }

        /// <summary>获取像素格式的符号名</summary>
        public string PixelFormatSymbol
        {
            get => GetEnumFeatureSymbol("PixelFormat");
            set => SetEnumFeatureSymbol("PixelFormat", value);
        }

        // =================================================================
        // 读出模式和合并模式
        // =================================================================

        /// <summary>读出模式</summary>
        /// <remarks>
        /// 0 = bit11_HS_Low    - 11位高速低增益（适合明亮场景）
        /// 1 = bit11_HS_High   - 11位高速高增益（适合弱光场景）
        /// 6 = bit12_CMS       - 12位低噪声模式（高质量）
        /// 7 = bit16_From11    - 16位高动态范围（高对比度场景）
        /// </remarks>
        public ulong ReadoutMode
        {
            get => GetEnumFeature("ReadoutMode");
            set => SetEnumFeature("ReadoutMode", value);
        }

        /// <summary>合并模式（Binning）</summary>
        /// <remarks>
        /// 0 = OneByOne (1x1)    - 无合并，全分辨率
        /// 1 = TwoByTwo (2x2)    - 2x2合并，1/4分辨率，4倍亮度
        /// 2 = FourByFour (4x4)  - 4x4合并，1/16分辨率，16倍亮度
        /// 注意：会改变Width、Height、OffsetX、OffsetY，停止采集后才能更改
        /// </remarks>
        public ulong BinningMode
        {
            get => GetEnumFeature("BinningMode");
            set => SetEnumFeature("BinningMode", value);
        }

        // =================================================================
        // 曝光和帧率
        // =================================================================

        /// <summary>曝光时间（微秒）</summary>
        /// <remarks>
        /// 影响因素：
        /// - 图像亮度：时间越长越亮
        /// - 运动模糊：时间越长运动物体越模糊
        /// - 帧率：时间越长帧率越低
        /// 最大帧率 = 1 / (曝光时间 + 读出时间)
        /// </remarks>
        public double ExposureTime
        {
            get => GetFloatFeature("ExposureTime");
            set => SetFloatFeature("ExposureTime", value);
        }

        /// <summary>采集帧率（fps）</summary>
        /// <remarks>
        /// 帧率限制因素：
        /// 1. 曝光时间：必须 < 1/帧率
        /// 2. ROI大小：ROI越大读出时间越长
        /// 3. 接口带宽：USB3或CXP带宽限制
        /// 4. 像素格式：格式不同数据量不同
        /// 使用前需启用FrameRateEnable
        /// </remarks>
        public double AcquisitionFrameRate
        {
            get => GetFloatFeature("AcquisitionFrameRate");
            set => SetFloatFeature("AcquisitionFrameRate", value);
        }

        /// <summary>
        /// 获取最大可用帧率
        /// </summary>
        public double MaxAcquisitionFrameRate => GetFloatFeatureMax("AcquisitionFrameRate");

        /// <summary>帧率使能</summary>
        /// <remarks>
        /// False = 禁用帧率控制，最大速度采集
        /// True  = 启用帧率控制，按设定帧率采集
        /// </remarks>
        public bool FrameRateEnable
        {
            get => GetEnumFeature("FrameRateEnable") != 0;
            set => SetEnumFeature("FrameRateEnable", value ? 1u : 0u);
        }

        // =================================================================
        // 触发控制
        // =================================================================

        /// <summary>触发输入类型</summary>
        /// <remarks>
        /// 0 = Off (关闭)
        /// 1 = External_Edge_Trigger (外部边缘触发)
        /// 2 = External_Start_Trigger (外部开始触发)
        /// 3 = External_Level_Trigger (外部电平触发)
        /// 4 = Synchronous_Readout (同步读出)
        /// 5 = Software_Trigger (软件触发)
        /// </remarks>
        public ulong TriggerInType
        {
            get => GetEnumFeature("TriggerInType");
            set => SetEnumFeature("TriggerInType", value);
        }

        /// <summary>触发激活方式</summary>
        /// <remarks>
        /// 0 = RisingEdge  (上升沿)
        /// 1 = FallingEdge (下降沿)
        /// 2 = LevelHigh   (高电平)
        /// 3 = LevelLow    (低电平)
        /// 与TriggerInType必须兼容
        /// </remarks>
        public ulong TriggerActivation
        {
            get => GetEnumFeature("TriggerActivation");
            set => SetEnumFeature("TriggerActivation", value);
        }

        /// <summary>触发延迟（微秒）</summary>
        /// <remarks>从触发信号到开始曝光的延迟</remarks>
        public double TriggerDelay
        {
            get => GetFloatFeature("TriggerDelay");
            set => SetFloatFeature("TriggerDelay", value);
        }

        /// <summary>发送软件触发</summary>
        /// <remarks>
        /// 前提条件：
        /// - TriggerInType必须设置为Software_Trigger (5)
        /// - 必须已经StartGrabbing
        /// </remarks>
        public void SoftwareTrigger()
        {
            ExecuteCommand("TriggerSoftware");
        }

        // =================================================================
        // 触发输出（用于同步其他设备）
        // =================================================================

        /// <summary>触发输出端口选择器</summary>
        /// <remarks>
        /// 1 = TriggerOut1
        /// 2 = TriggerOut2
        /// 3 = TriggerOut3
        /// </remarks>
        public ulong TriggerOutSelector
        {
            get => GetEnumFeature("TriggerOutSelector");
            set => SetEnumFeature("TriggerOutSelector", value);
        }

        /// <summary>触发输出信号类型</summary>
        /// <remarks>
        /// 0 = Exposure_Start    (曝光开始)
        /// 1 = VSYNC             (第一行读出结束)
        /// 2 = Readout_End       (读出结束)
        /// 3 = Trigger_Ready     (触发就绪)
        /// 4 = Global_Exposure   (全局曝光)
        /// 5 = High              (恒定高电平)
        /// 6 = Low               (恒定低电平)
        /// </remarks>
        public ulong TriggerOutType
        {
            get => GetEnumFeature("TriggerOutType");
            set => SetEnumFeature("TriggerOutType", value);
        }

        /// <summary>触发输出激活方式</summary>
        /// <remarks>
        /// 0 = RisingEdge  (上升沿)
        /// 1 = FallingEdge (下降沿)
        /// 2 = LevelHigh   (高电平)
        /// 3 = LevelLow    (低电平)
        /// 与TriggerOutType必须兼容
        /// </remarks>
        public ulong TriggerOutActivation
        {
            get => GetEnumFeature("TriggerOutActivation");
            set => SetEnumFeature("TriggerOutActivation", value);
        }

        /// <summary>触发输出延迟（微秒）</summary>
        /// <remarks>从事件发生到输出信号的延迟</remarks>
        public double TriggerOutDelay
        {
            get => GetFloatFeature("TriggerOutDelay");
            set => SetFloatFeature("TriggerOutDelay", value);
        }

        /// <summary>触发输出脉冲宽度（微秒）</summary>
        /// <remarks>仅对边缘触发信号有效</remarks>
        public double TriggerOutPulseWidth
        {
            get => GetFloatFeature("TriggerOutPulseWidth");
            set => SetFloatFeature("TriggerOutPulseWidth", value);
        }

        // =================================================================
        // 设备控制（温度和风扇）
        // =================================================================

        /// <summary>设备温度（摄氏度）- 只读</summary>
        /// <remarks>
        /// 正常工作温度：通常 -10℃ 到 50℃
        /// 过高会影响图像质量和器件寿命
        /// </remarks>
        public float DeviceTemperature
        {
            get => (float)GetFloatFeature("DeviceTemperature");
        }

        /// <summary>目标温度（摄氏度）</summary>
        /// <remarks>
        /// 制冷功能：降低暗电流、提高信噪比、减少热噪声
        /// 范围：取决于相机型号，通常 -30℃ 到 25℃
        /// 目标温度越低，功耗越大，需要良好的散热条件
        /// </remarks>
        public long DeviceTemperatureTarget
        {
            get => GetIntFeature("DeviceTemperatureTarget");
            set => SetIntFeature("DeviceTemperatureTarget", value);
        }

        /// <summary>风扇开关</summary>
        /// <remarks>
        /// True = 开启风扇（长时间工作、低温制冷时必须开启）
        /// False = 关闭风扇（注意监控温度，防止过热）
        /// </remarks>
        public bool FanSwitch
        {
            get => GetBoolFeature("FanSwitch");
            set => SetBoolFeature("FanSwitch", value);
        }

        /// <summary>风扇模式</summary>
        /// <remarks>
        /// 可能的模式：自动、全速、安静
        /// 具体枚举值请参考相机文档
        /// </remarks>
        public ulong FanMode
        {
            get => GetEnumFeature("FanMode");
            set => SetEnumFeature("FanMode", value);
        }

        // =================================================================
        // 便捷方法
        // =================================================================

        /// <summary>设置完整ROI（一次性设置所有参数）</summary>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        /// <param name="offsetX">X偏移</param>
        /// <param name="offsetY">Y偏移</param>
        /// <remarks>
        /// ROI说明：
        /// - 减小ROI可以提高帧率
        /// - ROI必须在传感器范围内
        /// - OffsetX + Width <= SensorWidth
        /// - OffsetY + Height <= SensorHeight
        /// 
        /// 使用建议：
        /// - 先停止采集
        /// - 调用SetROI
        /// - 重新读取实际值（可能被调整）
        /// - 开始采集
        /// </remarks>
        public void SetROI(long width, long height, long offsetX, long offsetY)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetROI(_handle, width, height, offsetX, offsetY);
            if (ret != 0)
                throw new CameraException(ret);
        }

        /// <summary>获取当前ROI设置</summary>
        public (long Width, long Height, long OffsetX, long OffsetY) GetROI()
        {
            return (Width, Height, OffsetX, OffsetY);
        }

        /// <summary>重置ROI为最大值（全传感器区域）</summary>
        public void ResetROI()
        {
            SetROI(SensorWidth, SensorHeight, 0, 0);
        }

        /// <summary>配置软件触发模式</summary>
        /// <remarks>
        /// 使用流程：
        /// 1. ConfigureSoftwareTrigger()
        /// 2. StartGrabbing()
        /// 3. SoftwareTrigger() - 触发一帧
        /// 4. GetFrame() - 获取图像
        /// 5. 重复3-4
        /// </remarks>
        public void ConfigureSoftwareTrigger()
        {
            TriggerInType = 5; // Software_Trigger
        }

        /// <summary>配置连续采集模式（关闭触发）</summary>
        public void ConfigureContinuousMode()
        {
            TriggerInType = 0; // Off
        }

        /// <summary>配置外部触发模式</summary>
        /// <param name="edgeType">0=上升沿, 1=下降沿</param>
        public void ConfigureExternalTrigger(int edgeType = 0)
        {
            TriggerInType = 1; // External_Edge_Trigger
            TriggerActivation = (ulong)edgeType; // 0=RisingEdge, 1=FallingEdge
        }

        /// <summary>获取属性的取值范围</summary>
        /// <param name="featureName">属性名</param>
        /// <returns>(最小值, 最大值, 步长)</returns>
        public (long Min, long Max, long Inc) GetIntFeatureRange(string featureName)
        {
            return (
                GetIntFeatureMin(featureName),
                GetIntFeatureMax(featureName),
                GetIntFeatureInc(featureName)
            );
        }

        /// <summary>获取浮点属性的取值范围</summary>
        /// <param name="featureName">属性名</param>
        /// <returns>(最小值, 最大值, 步长)</returns>
        public (double Min, double Max, double Inc) GetFloatFeatureRange(string featureName)
        {
            return (
                GetFloatFeatureMin(featureName),
                GetFloatFeatureMax(featureName),
                GetFloatFeatureInc(featureName)
            );
        }

        #endregion

        #region 私有方法

        //todo，此处后续可用：
        //环形缓冲区 + 专用处理线程

        /// <summary>
        /// 将ImageData转换为OpenCV Mat
        /// </summary>
        private static unsafe Mat? ConvertImageDataToMat(ref NativeMethods.ImageData imageData, ulong readoutMode)
        {
            if (imageData.pData == IntPtr.Zero || imageData.dataSize == 0)
                return null;

            Mat? mat = null;
            try
            {
                (MatType matType, int depth, int channels) = GetMatTypeFromPixelFormat(imageData.pixelFormat);

                Debug.WriteLine($"{matType.ToString()}  {imageData.width}*{imageData.height}");

                // ✅ 修复：对于10/12/16bit图像，每像素固定2字节
                int bytesPerPixel;
                if (matType == MatType.CV_16UC1)
                {
                    bytesPerPixel = 2;  // 16bit单通道，2字节
                }
                else if (matType == MatType.CV_8UC1)
                {
                    bytesPerPixel = 1;  // 8bit单通道，1字节
                }
                else if (matType == MatType.CV_8UC3)
                {
                    bytesPerPixel = 3;  // 8bit三通道，3字节
                }
                else
                {
                    // 通用计算（向上取整）
                    bytesPerPixel = channels * ((depth + 7) / 8);
                }

                int width = imageData.width;
                int height = imageData.height;
                int sdkStride = imageData.stride;
                int rowBytes = width * bytesPerPixel;  // ✅ 现在计算正确了

                // 创建连续内存的Mat
                mat = new Mat(height, width, matType);

                // 按行复制
                byte* srcBase = (byte*)imageData.pData.ToPointer();
                byte* dstBase = (byte*)mat.Data.ToPointer();
                int dstStride = (int)mat.Step();

                for (int row = 0; row < height; row++)
                {
                    byte* srcRow = srcBase + (row * sdkStride);
                    byte* dstRow = dstBase + (row * dstStride);

                    Buffer.MemoryCopy(srcRow, dstRow, dstStride, rowBytes);
                }

                // 移位归一化
                Mat? normalizedMat = NormalizeTo16Bit(mat, readoutMode);
                mat.Dispose();

                return normalizedMat;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Frame conversion failed: {ex.Message}");
                mat?.Dispose();
                return null;
            }
        }

        /// <summary>
        /// 根据像素格式获取Mat类型
        /// </summary>
        private static (MatType matType, int depth, int channels) GetMatTypeFromPixelFormat(int pixelFormat)
        {
            // 使用PixelFormatMap进行匹配
            foreach (var kvp in PixelFormatMap)
            {
                if (kvp.Value == pixelFormat)
                {
                    return kvp.Key switch
                    {
                        "Mono8" => (MatType.CV_8UC1, 8, 1),
                        "Mono10" => (MatType.CV_16UC1, 10, 1),
                        "Mono12" => (MatType.CV_16UC1, 12, 1),
                        "Mono16" => (MatType.CV_16UC1, 16, 1),
                        "RGB8" => (MatType.CV_8UC3, 8, 3),
                        "BGR8" => (MatType.CV_8UC3, 8, 3),
                        _ => (MatType.CV_8UC1, 8, 1)
                    };
                }
            }

            // 默认：Mono8
            return (MatType.CV_8UC1, 8, 1);
        }

        /// <summary>
        /// 将11bit或12bit图像转换为标准16bit（左移补齐高位）
        /// </summary>
        /// <param name="source">源图像</param>
        /// <param name="readoutMode">读出模式</param>
        /// <returns>归一化后的16bit图像</returns>
        private static Mat? NormalizeTo16Bit(Mat source, ulong readoutMode)
        {
            if (source == null || source.Empty())
                return null;

            // 根据 ReadoutMode 确定位深度
            // 0 = bit11_HS_Low    - 11位
            // 1 = bit11_HS_High   - 11位
            // 6 = bit12_CMS       - 12位
            // 7 = bit16_From11    - 16位
            int bitDepth = readoutMode switch
            {
                0 or 1 => 11,  // 11位高速模式
                6 => 12,       // 12位低噪声模式
                7 => 16,       // 16位高动态模式
                _ => 16        // 默认16位
            };

            // 如果已经是16bit，直接返回
            if (bitDepth == 16)
                return source.Clone();

            // 计算需要左移的位数
            int leftShift = 16 - bitDepth;

            if (leftShift <= 0)
                return source.Clone();

            Mat output = new Mat();

            // 左移补齐到16bit
            // 11bit: 左移5位 (0-2047 -> 0-65504)
            // 12bit: 左移4位 (0-4095 -> 0-65520)
            source.ConvertTo(output, MatType.CV_16UC1);
            output *= (1 << leftShift);

            return output;
        }

        private void CheckDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Revealer));
        }

        #endregion
    }

    #region 辅助类

    /// <summary>相机信息</summary>
    public class CameraInfo
    {
        public int Index { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>设备详细信息</summary>
    public class DeviceInfo
    {
        public string? CameraName { get; set; }
        public string? SerialNumber { get; set; }
        public string? ModelName { get; set; }
        public string? ManufacturerInfo { get; set; }
        public string? DeviceVersion { get; set; }
    }

    /// <summary>图像帧</summary>
    public class ImageFrame
    {
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public int PixelFormat { get; }
        public int DataSize { get; }
        public ulong BlockId { get; }
        public ulong TimeStamp { get; }
        public byte[] Data { get; }

        internal ImageFrame(NativeMethods.ImageData imageData)
        {
            Width = imageData.width;
            Height = imageData.height;
            Stride = imageData.stride;
            PixelFormat = imageData.pixelFormat;
            DataSize = imageData.dataSize;
            BlockId = imageData.blockId;
            TimeStamp = imageData.timeStamp;

            // 复制图像数据到托管内存
            Data = new byte[DataSize];
            if (imageData.pData != IntPtr.Zero)
            {
                System.Runtime.InteropServices.Marshal.Copy(imageData.pData, Data, 0, DataSize);
            }
        }
    }

    /// <summary>相机异常</summary>
    public class CameraException : Exception
    {
        public int ErrorCode { get; }

        public CameraException(string message, int errorCode = -101) : base(message)
        {
            ErrorCode = errorCode;
        }

        public CameraException(int errorCode)
            : base(CameraErrorCodeHelper.GetChineseMessage(errorCode))
        {
            ErrorCode = errorCode;
        }
    }

    #endregion
}