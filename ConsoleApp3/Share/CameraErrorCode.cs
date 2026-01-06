namespace EyeCam.Shared
{
    /// <summary>
    /// 相机SDK错误码枚举
    /// </summary>
    public enum CameraErrorCode
    {
        /// <summary>成功，无错误</summary>
        OK = 0,

        /// <summary>通用错误</summary>
        Error = -101,

        /// <summary>错误或无效的句柄</summary>
        InvalidHandle = -102,

        /// <summary>错误的参数</summary>
        InvalidParam = -103,

        /// <summary>错误或无效的帧句柄</summary>
        InvalidFrameHandle = -104,

        /// <summary>无效的帧</summary>
        InvalidFrame = -105,

        /// <summary>相机/事件/流等资源无效</summary>
        InvalidResource = -106,

        /// <summary>设备与主机的IP网段不匹配</summary>
        InvalidIP = -107,

        /// <summary>内存不足</summary>
        NoMemory = -108,

        /// <summary>传入的内存空间不足</summary>
        InsufficientMemory = -109,

        /// <summary>属性类型错误</summary>
        ErrorPropertyType = -110,

        /// <summary>属性不可访问、或不能读/写、或读/写失败</summary>
        InvalidAccess = -111,

        /// <summary>属性值超出范围、或者不是步长整数倍</summary>
        InvalidRange = -112,

        /// <summary>设备不支持的功能</summary>
        NotSupport = -113,

        /// <summary>功能未实现</summary>
        NotImplemented = -114,

        /// <summary>超时</summary>
        Timeout = -115,

        /// <summary>处于忙碌状态</summary>
        Busy = -116,

        /// <summary>访问设备被拒绝</summary>
        AccessDenied = -117,

        /// <summary>NodeMap非法或不存在</summary>
        InvalidNodemap = -118,

        /// <summary>无效的错误码</summary>
        InvalidErrorCode = -300
    }

    /// <summary>
    /// 错误码辅助类
    /// </summary>
    public static class CameraErrorCodeHelper
    {
        /// <summary>
        /// 将错误码转换为中文描述
        /// </summary>
        public static string GetChineseMessage(int errorCode)
        {
            return errorCode switch
            {
                0 => "成功",
                -101 => "通用错误",
                -102 => "错误或无效的句柄",
                -103 => "错误的参数",
                -104 => "错误或无效的帧句柄",
                -105 => "无效的帧",
                -106 => "相机/事件/流等资源无效",
                -107 => "设备与主机的IP网段不匹配",
                -108 => "内存不足",
                -109 => "传入的内存空间不足",
                -110 => "属性类型错误",
                -111 => "属性不可访问、或不能读/写、或读/写失败",
                -112 => "属性值超出范围、或者不是步长整数倍",
                -113 => "设备不支持的功能",
                -114 => "功能未实现",
                -115 => "超时",
                -116 => "处于忙碌状态",
                -117 => "访问设备被拒绝",
                -118 => "NodeMap非法或不存在",
                -300 => "无效的错误码",
                _ => $"未知错误 ({errorCode})"
            };
        }

        /// <summary>
        /// 将错误码转换为英文描述
        /// </summary>
        public static string GetEnglishMessage(int errorCode)
        {
            return errorCode switch
            {
                0 => "Success, no error",
                -101 => "Generic error",
                -102 => "Error or invalid handle",
                -103 => "Incorrect parameter",
                -104 => "Error or invalid frame handle",
                -105 => "Invalid frame",
                -106 => "Camera/Event/Stream resource invalid",
                -107 => "Device's and PC's subnet mismatch",
                -108 => "Memory allocation failed",
                -109 => "Insufficient memory",
                -110 => "Property type error",
                -111 => "Property not accessible or read/write failed",
                -112 => "Property value out of range or not multiple of increment",
                -113 => "Device not supported function",
                -114 => "Function not implemented",
                -115 => "Timeout",
                -116 => "Busy",
                -117 => "Access denied",
                -118 => "NodeMap invalid or missing",
                -300 => "Invalid error code",
                _ => $"Unknown error ({errorCode})"
            };
        }

        /// <summary>
        /// 检查错误码是否表示成功
        /// </summary>
        public static bool IsSuccess(int errorCode)
        {
            return errorCode == 0;
        }

        /// <summary>
        /// 检查错误码是否表示失败
        /// </summary>
        public static bool IsError(int errorCode)
        {
            return errorCode != 0;
        }
    }
}