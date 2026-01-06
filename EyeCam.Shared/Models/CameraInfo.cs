namespace EyeCam.Shared.Models
{
    /// <summary>相机信息（枚举时使用）</summary>
    public class CameraInfo
    {
        public int Index { get; }
        public string Name { get; }

        public CameraInfo(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public override string ToString() => $"[{Index}] {Name}";
    }
}