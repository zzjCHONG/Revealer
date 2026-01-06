using System;
using System.Collections.Generic;
using System.Text;
using EyeCam.Shared.Enums;
using EyeCam.Shared.Exceptions;
using EyeCam.Shared.Models;
using EyeCam.Shared.Native;

namespace EyeCam.Shared
{
    /// <summary>
    /// 相机类 - 提供相机的完整控制
    /// 基于NativeMethods的完整封装
    /// </summary>
    public class EyeCamera : IDisposable
    {
        private IntPtr _handle = IntPtr.Zero;
        private bool _disposed = false;
        private bool _isGrabbing = false;
        private static bool _sdkInitialized = false;
        private static readonly object _sdkLock = new object();

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
                    throw new CameraException($"初始化SDK失败", ret);

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
        public static List<CameraInfo> EnumerateDevices()
        {
            EnsureInitialized();

            int ret = NativeMethods.Camera_EnumDevices(out int count, 0);
            if (ret != 0)
                throw new CameraException($"枚举设备失败", ret);

            var devices = new List<CameraInfo>();
            for (int i = 0; i < count; i++)
            {
                var name = new StringBuilder(256);
                ret = NativeMethods.Camera_GetDeviceName(i, name, name.Capacity);
                if (ret == 0)
                {
                    devices.Add(new CameraInfo(i, name.ToString()));
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
                throw new InvalidOperationException("请先调用 EyeCamera.Initialize()");
        }

        #endregion

        #region 构造与析构

        /// <summary>创建相机实例</summary>
        /// <param name="deviceIndex">设备索引</param>
        public EyeCamera(int deviceIndex)
        {
            EnsureInitialized();

            int ret = NativeMethods.Camera_CreateHandle(out _handle, deviceIndex);
            if (ret != 0 || _handle == IntPtr.Zero)
                throw new CameraException($"创建相机句柄失败 (设备索引: {deviceIndex})", ret);
        }

        ~EyeCamera() => Dispose(false);

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
                throw new CameraException($"打开相机失败", ret);
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
        public DeviceInfoModel GetDeviceInfo()
        {
            CheckDisposed();

            var info = new NativeMethods.DeviceInfo();
            int ret = NativeMethods.Camera_GetDeviceInfo(_handle, ref info);
            if (ret != 0)
                throw new CameraException($"获取设备信息失败", ret);

            return new DeviceInfoModel
            {
                CameraName = info.cameraName,
                SerialNumber = info.serialNumber,
                ModelName = info.modelName,
                ManufacturerInfo = info.manufacturerInfo,
                DeviceVersion = info.deviceVersion
            };
        }

        /// <summary>下载GenICam XML配置</summary>
        public void DownloadGenICamXML(string filePath)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_DownloadGenICamXML(_handle, filePath);
            if (ret != 0)
                throw new CameraException($"下载XML配置失败", ret);
        }

        #endregion

        #region 采集控制

        /// <summary>开始采集</summary>
        public void StartGrabbing()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_StartGrabbing(_handle);
            if (ret != 0)
                throw new CameraException($"开始采集失败", ret);
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
                NativeMethods.Camera_IsGrabbing(_handle, out int isGrabbing);
                return isGrabbing != 0;
            }
        }

        /// <summary>设置缓冲区数量</summary>
        public void SetBufferCount(uint count)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetBufferCount(_handle, count);
            if (ret != 0)
                throw new CameraException($"设置缓冲区失败", ret);
        }

        /// <summary>获取一帧图像</summary>
        /// <param name="timeout">超时时间(毫秒)</param>
        public ImageFrame GetFrame(uint timeout = 5000)
        {
            CheckDisposed();

            var imageData = new NativeMethods.ImageData();
            int ret = NativeMethods.Camera_GetFrame(_handle, ref imageData, timeout);

            if (ret != 0)
                throw new CameraException($"获取帧失败", ret);

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

        /// <summary>获取处理后的图像</summary>
        public ImageFrame GetProcessedFrame(uint timeout = 5000)
        {
            CheckDisposed();

            var imageData = new NativeMethods.ImageData();
            int ret = NativeMethods.Camera_GetProcessedFrame(_handle, ref imageData, timeout);

            if (ret != 0)
                throw new CameraException($"获取处理后图像失败", ret);

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
        /// <param name="fileName">文件路径</param>
        /// <param name="recordFormat">0=AVI, 1=MP4</param>
        /// <param name="quality">质量 0-100</param>
        /// <param name="frameRate">帧率</param>
        public void StartRecording(string fileName, int recordFormat = 0,
            int quality = 90, int frameRate = 30)
        {
            CheckDisposed();

            var param = new NativeMethods.RecordParam
            {
                fileName = fileName,
                recordFormat = recordFormat,
                quality = quality,
                frameRate = frameRate
            };

            int ret = NativeMethods.Camera_OpenRecord(_handle, ref param);
            if (ret != 0)
                throw new CameraException($"开始录像失败", ret);
        }

        /// <summary>停止录像</summary>
        public void StopRecording()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_CloseRecord(_handle);
            if (ret != 0)
                throw new CameraException($"停止录像失败", ret);
        }

        /// <summary>设置导出缓存大小</summary>
        public void SetExportCacheSize(ulong cacheSizeInByte)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetExportCacheSize(_handle, cacheSizeInByte);
            if (ret != 0)
                throw new CameraException($"设置导出缓存失败", ret);
        }

        #endregion

        #region 图像参数

        /// <summary>传感器宽度（只读）</summary>
        public int SensorWidth
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetSensorWidth(_handle, out int value);
                return value;
            }
        }

        /// <summary>传感器高度（只读）</summary>
        public int SensorHeight
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetSensorHeight(_handle, out int value);
                return value;
            }
        }

        /// <summary>图像宽度</summary>
        public int Width
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetWidth(_handle, out int value);
                return value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetWidth(_handle, value);
                if (ret != 0)
                    throw new CameraException($"设置宽度失败", ret);
            }
        }

        /// <summary>图像高度</summary>
        public int Height
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetHeight(_handle, out int value);
                return value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetHeight(_handle, value);
                if (ret != 0)
                    throw new CameraException($"设置高度失败", ret);
            }
        }

        /// <summary>X偏移</summary>
        public int OffsetX
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetOffsetX(_handle, out int value);
                return value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetOffsetX(_handle, value);
                if (ret != 0)
                    throw new CameraException($"设置OffsetX失败", ret);
            }
        }

        /// <summary>Y偏移</summary>
        public int OffsetY
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetOffsetY(_handle, out int value);
                return value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetOffsetY(_handle, value);
                if (ret != 0)
                    throw new CameraException($"设置OffsetY失败", ret);
            }
        }

        /// <summary>设置ROI</summary>
        public void SetROI(int width, int height, int offsetX, int offsetY)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetROI(_handle, width, height, offsetX, offsetY);
            if (ret != 0)
                throw new CameraException($"设置ROI失败", ret);
        }

        /// <summary>像素格式</summary>
        public ulong PixelFormat
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetPixelFormat(_handle, out ulong value);
                return value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetPixelFormat(_handle, value);
                if (ret != 0)
                    throw new CameraException($"设置像素格式失败", ret);
            }
        }

        /// <summary>读出模式</summary>
        public ulong ReadoutMode
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetReadoutMode(_handle, out ulong value);
                return value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetReadoutMode(_handle, value);
                if (ret != 0)
                    throw new CameraException($"设置读出模式失败", ret);
            }
        }

        /// <summary>合并模式</summary>
        public ulong BinningMode
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetBinningMode(_handle, out ulong value);
                return value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetBinningMode(_handle, value);
                if (ret != 0)
                    throw new CameraException($"设置合并模式失败", ret);
            }
        }

        #endregion

        #region 曝光和帧率

        /// <summary>曝光时间（微秒）</summary>
        public double ExposureTime
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetExposureTime(_handle, out double value);
                return value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetExposureTime(_handle, value);
                if (ret != 0)
                    throw new CameraException($"设置曝光时间失败", ret);
            }
        }

        /// <summary>采集帧率（fps）</summary>
        public double AcquisitionFrameRate
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetAcquisitionFrameRate(_handle, out double value);
                return value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetAcquisitionFrameRate(_handle, value);
                if (ret != 0)
                    throw new CameraException($"设置帧率失败", ret);
            }
        }

        /// <summary>帧率使能</summary>
        public bool FrameRateEnabled
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetFrameRateEnable(_handle, out int value);
                return value != 0;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetFrameRateEnable(_handle, value ? 1 : 0);
                if (ret != 0)
                    throw new CameraException($"设置帧率使能失败", ret);
            }
        }

        #endregion

        #region 自动功能

        /// <summary>设置自动曝光参数</summary>
        public void SetAutoExposureParam(AutoExposureMode mode, int targetGray = -1)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetAutoExposureParam(_handle, (int)mode, targetGray);
            if (ret != 0)
                throw new CameraException($"设置自动曝光参数失败", ret);
        }

        /// <summary>执行自动曝光</summary>
        public int ExecuteAutoExposure()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_AutoExposure(_handle, out int actualGray);
            if (ret != 0)
                throw new CameraException($"执行自动曝光失败", ret);
            return actualGray;
        }

        /// <summary>自动色阶模式</summary>
        public AutoLevelMode AutoLevelMode
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetAutoLevels(_handle, out int value);
                return (AutoLevelMode)value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetAutoLevels(_handle, (int)value);
                if (ret != 0)
                    throw new CameraException($"设置自动色阶模式失败", ret);
            }
        }

        /// <summary>设置色阶阈值</summary>
        /// <param name="mode">1=右, 2=左</param>
        public void SetAutoLevelValue(int mode, int value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetAutoLevelValue(_handle, mode, value);
            if (ret != 0)
                throw new CameraException($"设置色阶阈值失败", ret);
        }

        /// <summary>获取色阶阈值</summary>
        /// <param name="mode">1=右, 2=左</param>
        public int GetAutoLevelValue(int mode)
        {
            CheckDisposed();
            NativeMethods.Camera_GetAutoLevelValue(_handle, mode, out int value);
            return value;
        }

        /// <summary>执行自动色阶</summary>
        /// <param name="mode">1=右, 2=左, 3=左右</param>
        public void ExecuteAutoLevel(int mode)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_ExecuteAutoLevel(_handle, mode);
            if (ret != 0)
                throw new CameraException($"执行自动色阶失败", ret);
        }

        #endregion

        #region 图像处理

        /// <summary>设置图像处理功能使能</summary>
        public void SetImageProcessingEnabled(ImageProcessingFeature feature, bool enable)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetImageProcessingEnabled(_handle, (int)feature, enable ? 1 : 0);
            if (ret != 0)
                throw new CameraException($"设置图像处理功能失败", ret);
        }

        /// <summary>获取图像处理功能使能状态</summary>
        public bool GetImageProcessingEnabled(ImageProcessingFeature feature)
        {
            CheckDisposed();
            NativeMethods.Camera_GetImageProcessingEnabled(_handle, (int)feature, out int enable);
            return enable != 0;
        }

        /// <summary>设置图像处理参数值</summary>
        public void SetImageProcessingValue(ImageProcessingFeature feature, int value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetImageProcessingValue(_handle, (int)feature, value);
            if (ret != 0)
                throw new CameraException($"设置图像处理参数失败", ret);
        }

        /// <summary>获取图像处理参数值</summary>
        public int GetImageProcessingValue(ImageProcessingFeature feature)
        {
            CheckDisposed();
            NativeMethods.Camera_GetImageProcessingValue(_handle, (int)feature, out int value);
            return value;
        }

        /// <summary>设置伪彩映射模式</summary>
        public void SetPseudoColorMap(PseudoColorMap mapMode)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetPseudoColorMap(_handle, (int)mapMode);
            if (ret != 0)
                throw new CameraException($"设置伪彩映射失败", ret);
        }

        #endregion

        #region 触发控制

        /// <summary>触发输入类型</summary>
        public TriggerInType TriggerInType
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetTriggerInType(_handle, out ulong value);
                return (TriggerInType)value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetTriggerInType(_handle, (ulong)value);
                if (ret != 0)
                    throw new CameraException($"设置触发输入类型失败", ret);
            }
        }

        /// <summary>触发激活方式</summary>
        public TriggerActivation TriggerActivation
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetTriggerActivation(_handle, out ulong value);
                return (TriggerActivation)value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetTriggerActivation(_handle, (ulong)value);
                if (ret != 0)
                    throw new CameraException($"设置触发激活方式失败", ret);
            }
        }

        /// <summary>触发延迟（微秒）</summary>
        public double TriggerDelay
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetTriggerDelay(_handle, out double value);
                return value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetTriggerDelay(_handle, value);
                if (ret != 0)
                    throw new CameraException($"设置触发延迟失败", ret);
            }
        }

        /// <summary>发送软件触发</summary>
        public void SoftwareTrigger()
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SoftwareTrigger(_handle);
            if (ret != 0)
                throw new CameraException($"软件触发失败", ret);
        }

        #endregion

        #region 设备控制

        /// <summary>设备温度（℃）</summary>
        public float Temperature
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetDeviceTemperature(_handle, out float value);
                return value;
            }
        }

        /// <summary>目标温度（℃）</summary>
        public int TargetTemperature
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetDeviceTemperatureTarget(_handle, out int value);
                return value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetDeviceTemperatureTarget(_handle, value);
                if (ret != 0)
                    throw new CameraException($"设置目标温度失败", ret);
            }
        }

        /// <summary>风扇开关</summary>
        public bool FanEnabled
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetFanSwitch(_handle, out int value);
                return value != 0;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetFanSwitch(_handle, value ? 1 : 0);
                if (ret != 0)
                    throw new CameraException($"设置风扇开关失败", ret);
            }
        }

        /// <summary>风扇模式</summary>
        public ulong FanMode
        {
            get
            {
                CheckDisposed();
                NativeMethods.Camera_GetFanMode(_handle, out ulong value);
                return value;
            }
            set
            {
                CheckDisposed();
                int ret = NativeMethods.Camera_SetFanMode(_handle, value);
                if (ret != 0)
                    throw new CameraException($"设置风扇模式失败", ret);
            }
        }

        #endregion

        #region 通用属性访问（高级功能）

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

        /// <summary>获取整型属性值</summary>
        public long GetIntFeature(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetIntFeatureValue(_handle, featureName, out long value);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 失败", ret);
            return value;
        }

        /// <summary>设置整型属性值</summary>
        public void SetIntFeature(string featureName, long value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetIntFeatureValue(_handle, featureName, value);
            if (ret != 0)
                throw new CameraException($"设置属性 {featureName} 失败", ret);
        }

        /// <summary>获取浮点属性值</summary>
        public double GetFloatFeature(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetFloatFeatureValue(_handle, featureName, out double value);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 失败", ret);
            return value;
        }

        /// <summary>设置浮点属性值</summary>
        public void SetFloatFeature(string featureName, double value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetFloatFeatureValue(_handle, featureName, value);
            if (ret != 0)
                throw new CameraException($"设置属性 {featureName} 失败", ret);
        }

        /// <summary>获取枚举属性值</summary>
        public ulong GetEnumFeature(string featureName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_GetEnumFeatureValue(_handle, featureName, out ulong value);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 失败", ret);
            return value;
        }

        /// <summary>设置枚举属性值</summary>
        public void SetEnumFeature(string featureName, ulong value)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_SetEnumFeatureValue(_handle, featureName, value);
            if (ret != 0)
                throw new CameraException($"设置属性 {featureName} 失败", ret);
        }

        /// <summary>获取字符串属性值</summary>
        public string GetStringFeature(string featureName)
        {
            CheckDisposed();
            var value = new StringBuilder(256);
            int ret = NativeMethods.Camera_GetStringFeatureValue(_handle, featureName, value, value.Capacity);
            if (ret != 0)
                throw new CameraException($"获取属性 {featureName} 失败", ret);
            return value.ToString();
        }

        /// <summary>执行命令属性</summary>
        public void ExecuteCommand(string commandName)
        {
            CheckDisposed();
            int ret = NativeMethods.Camera_ExecuteCommandFeature(_handle, commandName);
            if (ret != 0)
                throw new CameraException($"执行命令 {commandName} 失败", ret);
        }

        #endregion

        #region 私有方法

        private void CheckDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(EyeCamera));
        }

        #endregion
    }
}