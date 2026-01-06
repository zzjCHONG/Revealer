using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleApp2
{
    /// <summary>
    /// P/Invoke 声明 - 直接映射 Revealer.dll 的C接口
    /// </summary>
    internal static class NativeMethods
    {
        // DLL名称（确保DLL在输出目录）
        private const string DllName = "Simscop.Hardware.Revealer.dll";

        // 调用约定：必须和C++侧一致
        private const CallingConvention Convention = CallingConvention.Cdecl;

        #region 数据结构

        /// <summary>
        /// 图像数据结构 - 必须和 C++ 的 ImageData 结构体布局一致
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ImageData
        {
            public int width;
            public int height;
            public int dtride;
            public int pixelFormat;
            public IntPtr pData;        // 对应 C++ 的 unsigned char*
            public int dataSize;
            public ulong blockId;       // 帧序号
            public ulong timeStamp;     // 时间戳
        }

        /// <summary>
        /// 设备信息结构 - 必须和 C++ 的 DeviceInfo 结构体布局一致
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DeviceInfo
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string cameraName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string serialNumber;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string modelName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string manufacturerInfo;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string deviceVersion;
        }

        /// <summary>
        /// 录像参数结构 - 必须和 C++ 的 RecordParam 结构体布局一致
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct RecordParam
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
            public string fileName;
            public int recordFormat;    // 0=AVI, 1=MP4
            public int quality;         // 0-100
            public int frameRate;
        }

        #endregion

        #region 5.1 系统操作

        /// <summary>获取SDK版本</summary>
        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern IntPtr Camera_GetVersion();

        /// <summary>初始化SDK</summary>
        /// <param name="logLevel">日志级别: 0=Off, 1=Error, 2=Warn, 3=Info, 4=Debug</param>
        /// <param name="logPath">日志路径</param>
        /// <param name="fileSize">单个日志文件大小(字节)</param>
        /// <param name="fileNum">日志文件数量</param>
        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_Initialize(
            int logLevel,
            [MarshalAs(UnmanagedType.LPStr)] string logPath,
            uint fileSize,
            uint fileNum);

        /// <summary>释放SDK资源</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern void Camera_Release();

        /// <summary>枚举设备</summary>
        /// <param name="deviceCount">输出: 设备数量</param>
        /// <param name="interfaceType">接口类型: 0=All, 1=USB3, 2=CXP, 3=Custom</param>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_EnumDevices(out int deviceCount, uint interfaceType);

        /// <summary>获取设备名称</summary>
        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetDeviceName(
            int index,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder name,
            int nameSize);

        /// <summary>创建相机句柄</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_CreateHandle(out IntPtr handle, int deviceIndex);

        /// <summary>销毁相机句柄</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_DestroyHandle(IntPtr handle);

        #endregion

        #region 5.2 相机操作

        /// <summary>打开相机(默认权限)</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_Open(IntPtr handle);

        ///// <summary>打开相机(指定权限)</summary>
        ///// <param name="accessPermission">访问权限: 0=Open, 1=Control, 2=Exclusive</param>
        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_OpenEx(IntPtr handle, int accessPermission);

        /// <summary>关闭相机</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_Close(IntPtr handle);

        #endregion

        #region 5.3 相机配置下载

        /// <summary>下载GenICam XML配置文件</summary>
        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_DownloadGenICamXML(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string fullPath);

        #endregion

        #region 5.4 获取设备信息

        /// <summary>获取设备详细信息</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_GetDeviceInfo(IntPtr handle, ref DeviceInfo deviceInfo);

        #endregion

        #region 5.5 相机数据流操作

        /// <summary>开始采集</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_StartGrabbing(IntPtr handle);

        /// <summary>停止采集</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_StopGrabbing(IntPtr handle);

        /// <summary>检查是否正在采集</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_IsGrabbing(IntPtr handle, out int isGrabbing);

        /// <summary>设置帧缓冲区数量</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_SetBufferCount(IntPtr handle, uint bufferCount);

        /// <summary>获取一帧图像</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_GetFrame(
            IntPtr handle,
            ref ImageData image,
            uint timeout);

        /// <summary>释放帧资源</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_ReleaseFrame(IntPtr handle, ref ImageData image);

        /// <summary>获取处理后的图像</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_GetProcessedFrame(
            IntPtr handle,
            ref ImageData image,
            uint timeout);

        /// <summary>打开录像</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_OpenRecord(IntPtr handle, ref RecordParam recordParam);

        /// <summary>关闭录像</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_CloseRecord(IntPtr handle);

        /// <summary>设置导出缓存大小</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_SetExportCacheSize(IntPtr handle, ulong cacheSizeInByte);

        #endregion

        #region 5.6 属性操作

        // 属性可用性检查
        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_FeatureIsAvailable(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_FeatureIsReadable(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_FeatureIsWriteable(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName);

        /// <summary>获取属性类型</summary>
        /// <param name="pType">输出: 0=Int, 1=Float, 2=Enum, 3=Bool, 4=String, 5=Command</param>
        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetFeatureType(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            out int pType);

        // Integer属性操作
        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetIntFeatureValue(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            out long value);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetIntFeatureMin(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            out long value);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetIntFeatureMax(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            out long value);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetIntFeatureInc(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            out long value);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_SetIntFeatureValue(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            long value);

        // Float属性操作
        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetFloatFeatureValue(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            out double value);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetFloatFeatureMin(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            out double value);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetFloatFeatureMax(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            out double value);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetFloatFeatureInc(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            out double value);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_SetFloatFeatureValue(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            double value);

        // Enum属性操作
        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetEnumFeatureValue(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            out ulong value);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_SetEnumFeatureValue(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            ulong value);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetEnumFeatureEntryNum(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            out uint num);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetEnumFeatureSymbol(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder symbol,
            int symbolSize);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_SetEnumFeatureSymbol(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            [MarshalAs(UnmanagedType.LPStr)] string symbol);

        // Bool属性操作
        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetBoolFeatureValue(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            out int value);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_SetBoolFeatureValue(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            int value);

        // String属性操作
        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_GetStringFeatureValue(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder value,
            int valueSize);

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_SetStringFeatureValue(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            [MarshalAs(UnmanagedType.LPStr)] string value);

        // Command属性操作
        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        public static extern int Camera_ExecuteCommandFeature(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName);

        #endregion

        #region 5.8 其他功能

        /// <summary>设置自动曝光参数</summary>
        /// <param name="mode">0=关闭, 1=右侧, 2=中央</param>
        /// <param name="targetGray">目标灰度值, -1=默认</param>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_SetAutoExposureParam(IntPtr handle, int mode, int targetGray);

        /// <summary>执行自动曝光</summary>
        /// <param name="pActualGray">输出: 实际目标灰度</param>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_AutoExposure(IntPtr handle, out int actualGray);

        /// <summary>设置自动色阶模式</summary>
        /// <param name="mode">0=关闭, 1=右, 2=左, 3=左右</param>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_SetAutoLevels(IntPtr handle, int mode);

        /// <summary>获取自动色阶模式</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_GetAutoLevels(IntPtr handle, out int mode);

        /// <summary>设置色阶阈值</summary>
        /// <param name="mode">1=右色阶, 2=左色阶</param>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_SetAutoLevelValue(IntPtr handle, int mode, int value);

        /// <summary>获取色阶阈值</summary>
        /// <param name="mode">1=右色阶, 2=左色阶</param>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_GetAutoLevelValue(IntPtr handle, int mode, out int value);

        /// <summary>执行一次自动色阶</summary>
        /// <param name="mode">1=右, 2=左, 3=左右</param>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_ExecuteAutoLevel(IntPtr handle, int mode);

        /// <summary>设置图像处理功能使能</summary>
        /// <param name="feature">0=亮度, 1=对比度, 2=Gamma, 3=伪彩, 4=旋转, 5=翻转</param>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_SetImageProcessingEnabled(IntPtr handle, int feature, int enable);

        /// <summary>获取图像处理功能使能</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_GetImageProcessingEnabled(IntPtr handle, int feature, out int enable);

        /// <summary>设置图像处理参数值</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_SetImageProcessingValue(IntPtr handle, int feature, int value);

        /// <summary>获取图像处理参数值</summary>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_GetImageProcessingValue(IntPtr handle, int feature, out int value);

        /// <summary>设置伪彩映射模式</summary>
        /// <param name="mapMode">0=HSV, 1=Jet, 2=Red, 3=Green, 4=Blue</param>
        [DllImport(DllName, CallingConvention = Convention)]
        public static extern int Camera_SetPseudoColorMap(IntPtr handle, int mapMode);

        #endregion

        //#region 便捷属性接口

        //// 图像格式控制
        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetSensorWidth(IntPtr handle, out int width);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetSensorHeight(IntPtr handle, out int height);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetWidth(IntPtr handle, int width);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetWidth(IntPtr handle, out int width);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetHeight(IntPtr handle, int height);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetHeight(IntPtr handle, out int height);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetOffsetX(IntPtr handle, int offsetX);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetOffsetX(IntPtr handle, out int offsetX);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetOffsetY(IntPtr handle, int offsetY);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetOffsetY(IntPtr handle, out int offsetY);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetROI(IntPtr handle, int width, int height, int offsetX, int offsetY);

        //// 像素格式和读出模式
        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetPixelFormat(IntPtr handle, ulong format);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetPixelFormat(IntPtr handle, out ulong format);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetReadoutMode(IntPtr handle, ulong mode);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetReadoutMode(IntPtr handle, out ulong mode);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetBinningMode(IntPtr handle, ulong mode);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetBinningMode(IntPtr handle, out ulong mode);

        //// 采集控制
        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetExposureTime(IntPtr handle, double exposureTime);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetExposureTime(IntPtr handle, out double exposureTime);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetAcquisitionFrameRate(IntPtr handle, double frameRate);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetAcquisitionFrameRate(IntPtr handle, out double frameRate);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetFrameRateEnable(IntPtr handle, int enable);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetFrameRateEnable(IntPtr handle, out int enable);

        //// 触发控制
        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetTriggerInType(IntPtr handle, ulong inType);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetTriggerInType(IntPtr handle, out ulong inType);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetTriggerActivation(IntPtr handle, ulong activation);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetTriggerActivation(IntPtr handle, out ulong activation);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetTriggerDelay(IntPtr handle, double delay);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetTriggerDelay(IntPtr handle, out double delay);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SoftwareTrigger(IntPtr handle);

        //// 触发输出
        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetTriggerOutSelector(IntPtr handle, ulong selector);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetTriggerOutSelector(IntPtr handle, out ulong selector);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetTriggerOutType(IntPtr handle, ulong outType);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetTriggerOutType(IntPtr handle, out ulong outType);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetTriggerOutActivation(IntPtr handle, ulong activation);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetTriggerOutActivation(IntPtr handle, out ulong activation);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetTriggerOutDelay(IntPtr handle, double delay);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetTriggerOutDelay(IntPtr handle, out double delay);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetTriggerOutPulseWidth(IntPtr handle, double width);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetTriggerOutPulseWidth(IntPtr handle, out double width);

        //// 设备控制
        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetDeviceTemperature(IntPtr handle, out float temperature);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetDeviceTemperatureTarget(IntPtr handle, int targetTemp);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetDeviceTemperatureTarget(IntPtr handle, out int targetTemp);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetFanSwitch(IntPtr handle, int enable);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetFanSwitch(IntPtr handle, out int enable);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_SetFanMode(IntPtr handle, ulong mode);

        //[DllImport(DllName, CallingConvention = Convention)]
        //public static extern int Camera_GetFanMode(IntPtr handle, out ulong mode);

        //#endregion

        #region 辅助方法

        /// <summary>
        /// 获取SDK版本字符串（辅助方法）
        /// </summary>
        public static string GetVersionString()
        {
            IntPtr ptr = Camera_GetVersion();
            if (ptr == IntPtr.Zero)
                return string.Empty;
            return Marshal.PtrToStringAnsi(ptr);
        }

        #endregion
    }
}