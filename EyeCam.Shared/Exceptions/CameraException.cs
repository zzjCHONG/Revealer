using System;

namespace EyeCam.Shared.Exceptions
{
    /// <summary>相机异常</summary>
    public class CameraException : Exception
    {
        public int ErrorCode { get; }

        public CameraException(string message, int errorCode = 0)
            : base($"{message} (错误码: {errorCode})")
        {
            ErrorCode = errorCode;
        }

        public CameraException(string message, int errorCode, Exception innerException)
            : base($"{message} (错误码: {errorCode})", innerException)
        {
            ErrorCode = errorCode;
        }
    }
}