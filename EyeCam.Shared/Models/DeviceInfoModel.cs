namespace EyeCam.Shared.Models
{
    /// <summary>设备详细信息</summary>
    public class DeviceInfoModel
    {
        public string CameraName { get; set; }
        public string SerialNumber { get; set; }
        public string ModelName { get; set; }
        public string ManufacturerInfo { get; set; }
        public string DeviceVersion { get; set; }

        public override string ToString()
        {
            return $"{ModelName} (S/N: {SerialNumber})";
        }
    }
}