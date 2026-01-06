using EyeCam.Shared.Native;
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
        private NativeMethods.ConnectCallBackDelegate _connectCallback;
        private NativeMethods.ParamUpdateCallBackDelegate _paramUpdateCallback;
        private NativeMethods.ExportEventCallBackDelegate _exportCallback;
        private NativeMethods.FrameCallBackDelegate _frameCallback;

        #region 静态方法（SDK级别）

        /// <summary>
        /// 初始化SDK（全局调用一次）
        /// </summary>
        /// <param name="logLevel">日志级别: 0=Off, 1=Error, 2=Warn, 3=Info, 4=Debug</param>
        /// <param name="logPath">日志路径</param>
        /// <param name="fileSize">单个日志文件大小(字节)</param>
        /// <param name="fileNum">日志文件数量</param>
        public static void Initialize(int logLevel = 3, string logPath = ".",
            uint fileSize = 10485760, uint fileNum = 10)
        {
            lock (_sdkLock)
            {
                if (_sdkInitialized) return;

                int ret = NativeMethods.Camera_Initialize(logLevel, logPath, fileSize, fileNum);
                if (ret != 0)
                    throw new CameraException($"初始化SDK失败，错误码: {ret}");

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
                throw new CameraException($"枚举设备失败，错误码: {ret}");

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

            int ret = NativeMethods.Camera_CreateHandle(out _handle, deviceIndex);
            if (ret != 0 || _handle == IntPtr.Zero)
                throw new CameraException($"创建相机句柄失败 (设备索引: {deviceIndex})，错误码: {ret}");
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
                throw new CameraException($"打开相机失败，错误码: {ret}");
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
                throw new CameraException($"获取设备信息失败，错误码: {ret}");

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
                throw new CameraException($"下载XML配置失败，错误码: {ret}");
        }

        #endregion

        #region 采集控制

        /// <summary>开始采集</summary>
        public void StartGrabbing()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_StartGrabbing(_handle);
            if (ret != 0)
                throw new CameraException($"开始采集失败，错误码: {ret}");
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
                throw new CameraException($"设置缓冲区失败，错误码: {ret}");
        }

        /// <summary>获取一帧图像（同步）</summary>
        /// <param name="timeout">超时时间(毫秒), 0xFFFFFFFF表示无限等待</param>
        public ImageFrame GetFrame(uint timeout = 5000)
        {
            CheckDisposed();

            var imageData = new NativeMethods.ImageData();
            int ret = NativeMethods.Camera_GetFrame(_handle, ref imageData, timeout);

            if (ret != 0)
                throw new CameraException($"获取帧失败，错误码: {ret}");

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
        public ImageFrame GetProcessedFrame(uint timeout = 5000)
        {
            CheckDisposed();

            var imageData = new NativeMethods.ImageData();
            int ret = NativeMethods.Camera_GetProcessedFrame(_handle, ref imageData, timeout);

            if (ret != 0)
                throw new CameraException($"获取处理后图像失败，错误码: {ret}");

            try
            {
                var frame = new ImageFrame(imageData);
                return frame;
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
                throw new CameraException($"开始录像失败，错误码: {ret}");
        }

        /// <summary>停止录像</summary>
        public void StopRecording()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_CloseRecord(_handle);
            if (ret != 0)
                throw new CameraException($"停止录像失败，错误码: {ret}");
        }

        /// <summary>设置导出缓存大小</summary>
        public void SetExportCacheSize(ulong cacheSizeInByte)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetExportCacheSize(_handle, cacheSizeInByte);
            if (ret != 0)
                throw new CameraException($"设置导出缓存失败，错误码: {ret}");
        }

        #endregion

        #region ROI设置

        /// <summary>设置ROI（感兴趣区域）</summary>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        /// <param name="offsetX">X偏移</param>
        /// <param name="offsetY">Y偏移</param>
        public void SetROI(long width, long height, long offsetX, long offsetY)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetROI(_handle, width, height, offsetX, offsetY);
            if (ret != 0)
                throw new CameraException($"设置ROI失败，错误码: {ret}");
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
                throw new CameraException($"设置自动曝光参数失败，错误码: {ret}");
        }

        /// <summary>执行自动曝光</summary>
        /// <returns>实际目标灰度值</returns>
        public int ExecuteAutoExposure()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_AutoExposure(_handle, out int actualGray);
            if (ret != 0)
                throw new CameraException($"执行自动曝光失败，错误码: {ret}");
            return actualGray;
        }

        /// <summary>设置自动色阶模式</summary>
        /// <param name="mode">0=关闭, 1=右, 2=左, 3=左右</param>
        public void SetAutoLevels(int mode)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetAutoLevels(_handle, mode);
            if (ret != 0)
                throw new CameraException($"设置自动色阶模式失败，错误码: {ret}");
        }

        /// <summary>获取自动色阶模式</summary>
        public int GetAutoLevels()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetAutoLevels(_handle, out int mode);
            if (ret != 0)
                throw new CameraException($"获取自动色阶模式失败，错误码: {ret}");
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
                throw new CameraException($"设置色阶阈值失败，错误码: {ret}");
        }

        /// <summary>获取色阶阈值</summary>
        /// <param name="mode">1=右色阶, 2=左色阶</param>
        public int GetAutoLevelValue(int mode)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetAutoLevelValue(_handle, mode, out int value);
            if (ret != 0)
                throw new CameraException($"获取色阶阈值失败，错误码: {ret}");
            return value;
        }

        /// <summary>执行一次自动色阶</summary>
        /// <param name="mode">1=右, 2=左, 3=左右</param>
        public void ExecuteAutoLevel(int mode)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_ExecuteAutoLevel(_handle, mode);
            if (ret != 0)
                throw new CameraException($"执行自动色阶失败，错误码: {ret}");
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
                throw new CameraException($"设置图像处理功能失败，错误码: {ret}");
        }

        /// <summary>获取图像处理功能使能状态</summary>
        /// <param name="feature">0=亮度, 1=对比度, 2=Gamma, 3=伪彩, 4=旋转, 5=翻转</param>
        public bool GetImageProcessingEnabled(int feature)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetImageProcessingEnabled(_handle, feature, out int enable);
            if (ret != 0)
                throw new CameraException($"获取图像处理功能状态失败，错误码: {ret}");
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
                throw new CameraException($"设置图像处理参数失败，错误码: {ret}");
        }

        /// <summary>获取图像处理参数值</summary>
        /// <param name="feature">功能枚举</param>
        public int GetImageProcessingValue(int feature)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetImageProcessingValue(_handle, feature, out int value);
            if (ret != 0)
                throw new CameraException($"获取图像处理参数失败，错误码: {ret}");
            return value;
        }

        /// <summary>设置伪彩映射模式</summary>
        /// <param name="mapMode">0=HSV, 1=Jet, 2=Red, 3=Green, 4=Blue</param>
        public void SetPseudoColorMap(int mapMode)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetPseudoColorMap(_handle, mapMode);
            if (ret != 0)
                throw new CameraException($"设置伪彩映射失败，错误码: {ret}");
        }

        /// <summary>获取伪彩映射模式</summary>
        public int GetPseudoColorMap()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetPseudoColorMap(_handle, out int mapMode);
            if (ret != 0)
                throw new CameraException($"获取伪彩映射失败，错误码: {ret}");
            return mapMode;
        }

        #endregion

        #region 回调注册（异步采集）

        /// <summary>注册处理后图像数据回调函数（异步）</summary>
        /// <param name="callback">回调函数</param>
        /// <remarks>与 GetProcessedFrame 互斥，只能选其一</remarks>
        public void AttachProcessedGrabbing(Action<ImageFrame> callback)
        {
            CheckDisposed();

            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            // 创建委托并保持引用（防止GC）
            _frameCallback = (ref NativeMethods.ImageData imageData, IntPtr pUser) =>
            {
                try
                {
                    var frame = new ImageFrame(imageData);
                    callback(frame);
                }
                catch (Exception ex)
                {
                    // 回调中的异常需要记录，避免崩溃
                    System.Diagnostics.Debug.WriteLine($"帧回调异常: {ex.Message}");
                }
            };

            int ret = NativeMethods.Camera_AttachProcessedGrabbing(_handle, _frameCallback, IntPtr.Zero);
            if (ret != 0)
                throw new CameraException($"注册处理后图像回调失败，错误码: {ret}");
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
                throw new CameraException($"获取属性类型失败: {featureName}，错误码: {ret}");
            return type;
        }

        /// <summary>获取整型属性值</summary>
        public long GetIntFeature(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetIntFeatureValue(_handle, featureName, out long value);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 失败，错误码: {ret}");
            return value;
        }

        /// <summary>获取整型属性最小值</summary>
        public long GetIntFeatureMin(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetIntFeatureMin(_handle, featureName, out long value);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 最小值失败，错误码: {ret}");
            return value;
        }

        /// <summary>获取整型属性最大值</summary>
        public long GetIntFeatureMax(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetIntFeatureMax(_handle, featureName, out long value);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 最大值失败，错误码: {ret}");
            return value;
        }

        /// <summary>获取整型属性步长</summary>
        public long GetIntFeatureInc(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetIntFeatureInc(_handle, featureName, out long value);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 步长失败，错误码: {ret}");
            return value;
        }

        /// <summary>设置整型属性值</summary>
        public void SetIntFeature(string featureName, long value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetIntFeatureValue(_handle, featureName, value);
            if (ret != 0)
                throw new CameraException($"设置属性 {featureName} 失败，错误码: {ret}");
        }

        /// <summary>获取浮点属性值</summary>
        public double GetFloatFeature(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetFloatFeatureValue(_handle, featureName, out double value);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 失败，错误码: {ret}");
            return value;
        }

        /// <summary>获取浮点属性最小值</summary>
        public double GetFloatFeatureMin(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetFloatFeatureMin(_handle, featureName, out double value);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 最小值失败，错误码: {ret}");
            return value;
        }

        /// <summary>获取浮点属性最大值</summary>
        public double GetFloatFeatureMax(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetFloatFeatureMax(_handle, featureName, out double value);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 最大值失败，错误码: {ret}");
            return value;
        }

        /// <summary>获取浮点属性步长</summary>
        public double GetFloatFeatureInc(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetFloatFeatureInc(_handle, featureName, out double value);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 步长失败，错误码: {ret}");
            return value;
        }

        /// <summary>设置浮点属性值</summary>
        public void SetFloatFeature(string featureName, double value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetFloatFeatureValue(_handle, featureName, value);
            if (ret != 0)
                throw new CameraException($"设置属性 {featureName} 失败，错误码: {ret}");
        }

        /// <summary>获取枚举属性值</summary>
        public ulong GetEnumFeature(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetEnumFeatureValue(_handle, featureName, out ulong value);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 失败，错误码: {ret}");
            return value;
        }

        /// <summary>设置枚举属性值</summary>
        public void SetEnumFeature(string featureName, ulong value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetEnumFeatureValue(_handle, featureName, value);
            if (ret != 0)
                throw new CameraException($"设置属性 {featureName} 失败，错误码: {ret}");
        }

        /// <summary>获取枚举属性的可用值数量</summary>
        public uint GetEnumFeatureEntryNum(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetEnumFeatureEntryNum(_handle, featureName, out uint num);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 枚举数量失败，错误码: {ret}");
            return num;
        }

        /// <summary>获取枚举属性的符号名（Symbol）</summary>
        public string GetEnumFeatureSymbol(string featureName)
        {
            CheckDisposed();
            var symbol = new StringBuilder(256);
            int ret = NativeMethods.Camera_GetEnumFeatureSymbol(_handle, featureName, symbol, symbol.Capacity);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 符号名失败，错误码: {ret}");
            return symbol.ToString();
        }

        /// <summary>通过符号名设置枚举属性</summary>
        public void SetEnumFeatureSymbol(string featureName, string symbol)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetEnumFeatureSymbol(_handle, featureName, symbol);
            if (ret != 0)
                throw new CameraException($"设置属性 {featureName} 失败，错误码: {ret}");
        }

        /// <summary>获取布尔属性值</summary>
        public bool GetBoolFeature(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetBoolFeatureValue(_handle, featureName, out int value);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 失败，错误码: {ret}");
            return value != 0;
        }

        /// <summary>设置布尔属性值</summary>
        public void SetBoolFeature(string featureName, bool value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetBoolFeatureValue(_handle, featureName, value ? 1 : 0);
            if (ret != 0)
                throw new CameraException($"设置属性 {featureName} 失败，错误码: {ret}");
        }

        /// <summary>获取字符串属性值</summary>
        public string GetStringFeature(string featureName)
        {
            CheckDisposed();
            var value = new StringBuilder(256);
            int ret = NativeMethods.Camera_GetStringFeatureValue(_handle, featureName, value, value.Capacity);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 失败，错误码: {ret}");
            return value.ToString();
        }

        /// <summary>设置字符串属性值</summary>
        public void SetStringFeature(string featureName, string value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetStringFeatureValue(_handle, featureName, value);
            if (ret != 0)
                throw new CameraException($"设置属性 {featureName} 失败，错误码: {ret}");
        }

        /// <summary>执行命令属性</summary>
        /// <param name="commandName">命令名称，如 "TriggerSoftware"</param>
        public void ExecuteCommand(string commandName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_ExecuteCommandFeature(_handle, commandName);
            if (ret != 0)
                throw new CameraException($"执行命令 {commandName} 失败，错误码: {ret}");
        }

        #endregion

        //#region 便捷属性封装（通过GenICam属性实现）

        ///// <summary>曝光时间（微秒）</summary>
        //public double ExposureTime
        //{
        //    get => GetFloatFeature("ExposureTime");
        //    set => SetFloatFeature("ExposureTime", value);
        //}

        ///// <summary>采集帧率（fps）</summary>
        //public double AcquisitionFrameRate
        //{
        //    get => GetFloatFeature("AcquisitionFrameRate");
        //    set => SetFloatFeature("AcquisitionFrameRate", value);
        //}

        ///// <summary>图像宽度</summary>
        //public long Width
        //{
        //    get => GetIntFeature("Width");
        //    set => SetIntFeature("Width", value);
        //}

        ///// <summary>图像高度</summary>
        //public long Height
        //{
        //    get => GetIntFeature("Height");
        //    set => SetIntFeature("Height", value);
        //}

        ///// <summary>X偏移</summary>
        //public long OffsetX
        //{
        //    get => GetIntFeature("OffsetX");
        //    set => SetIntFeature("OffsetX", value);
        //}

        ///// <summary>Y偏移</summary>
        //public long OffsetY
        //{
        //    get => GetIntFeature("OffsetY");
        //    set => SetIntFeature("OffsetY", value);
        //}

        ///// <summary>发送软件触发</summary>
        //public void SoftwareTrigger()
        //{
        //    ExecuteCommand("TriggerSoftware");
        //}

        //#endregion

        #region 私有方法

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
        public CameraException(string message) : base(message) { }
    }

    #endregion
}