namespace EyeCam.Shared.Enums
{
    /// <summary>自动曝光模式</summary>
    public enum AutoExposureMode
    {
        Off = 0,      // 关闭
        Right = 1,    // 右侧模式
        Center = 2    // 中央模式
    }

    /// <summary>自动色阶模式</summary>
    public enum AutoLevelMode
    {
        Off = 0,      // 关闭
        Right = 1,    // 右色阶
        Left = 2,     // 左色阶
        Both = 3      // 左右色阶
    }

    /// <summary>图像处理功能</summary>
    public enum ImageProcessingFeature
    {
        Brightness = 0,    // 亮度
        Contrast = 1,      // 对比度
        Gamma = 2,         // Gamma
        PseudoColor = 3,   // 伪彩
        Rotation = 4,      // 旋转
        Flip = 5           // 翻转
    }

    /// <summary>伪彩映射模式</summary>
    public enum PseudoColorMap
    {
        HSV = 0,
        Jet = 1,
        Red = 2,
        Green = 3,
        Blue = 4
    }

    /// <summary>触发输入类型</summary>
    public enum TriggerInType
    {
        Off = 0,
        ExternalEdge = 1,
        ExternalStart = 2,
        ExternalLevel = 3,
        SynchronousReadout = 4,
        Software = 5
    }

    /// <summary>触发激活方式</summary>
    public enum TriggerActivation
    {
        RisingEdge = 0,
        FallingEdge = 1,
        LevelHigh = 2,
        LevelLow = 3
    }
}