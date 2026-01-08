using OpenCvSharp;

namespace Simscop.Spindisk.Core.Interfaces;

public interface ICamera
{
    public bool Init();

    /// <summary>
    /// 设备基本属性字典，比如 model，serialNumber ，FirmwareVersion等
    /// </summary>
    public Dictionary<InfoEnum, string> InfoDirectory { get; }

    /// <summary>
    /// 采集并返回一张当前的图像
    /// </summary>
    /// <param name="img"></param>
    /// <returns></returns>
    public bool Capture(out Mat? img);

    /// <summary>
    /// 执行一次相机自动白平衡
    /// </summary>
    /// <returns></returns>
    public bool AutoWhiteBlanceOnce();

    /// <summary>
    /// 开始采集数据
    /// </summary>
    /// <returns></returns>
    public bool StartCapture();

    /// <summary>
    /// 停止捕获数据
    /// </summary>
    /// <returns></returns>
    public bool StopCapture();

    /// <summary>
    /// 曝光范围
    /// </summary>
    public (double Min, double Max) ExposureRange { get; }

    /// <summary>
    /// 曝光，单位ms
    /// </summary>
    public double Exposure { get; set; }

    /// <summary>
    /// 色温范围
    /// </summary>
    public (double Min, double Max) TemperatureRange { get; }

    /// <summary>
    /// 色温
    /// </summary>
    public double Temperature { get; set; }

    /// <summary>
    /// 色调范围
    /// </summary>
    public (double Min, double Max) TintRange { get; }

    /// <summary>
    /// 色调
    /// </summary>
    public double Tint { get; set; }

    /// <summary>
    /// 图像Gamma值，默认为1
    /// -1 - 1
    /// </summary>
    public double Gamma { get; set; }

    /// <summary>
    /// 图像对比度，默认为0
    /// -1 - 1
    /// </summary>
    public double Contrast { get; set; }

    /// <summary>
    /// 图像亮度，默认为0
    /// -1 - 1
    /// </summary>
    public double Brightness { get; set; }

    /// <summary>
    /// 相机增益，仅设配硬件增益
    /// </summary>
    public ushort Gain { get; set; }

    /// <summary>
    /// 是否自动色阶
    /// </summary>
    public bool IsAutoLevel { get; set; }

    /// <summary>
    /// 是否自动曝光
    /// </summary>
    public bool IsAutoExposure { get; set; }

    /// <summary>
    /// 相机当前帧率
    /// </summary>
    public double FrameRate { get; }

    /// <summary>
    /// 顺时针旋转角度
    /// <code>
    /// 0 - 0
    /// 1 - 90
    /// 2 - 180
    /// 3 - 270
    /// </code>
    /// </summary>
    public int ClockwiseRotation { get; set; }

    /// <summary>
    /// 是否水平翻转
    /// </summary>
    public bool IsFlipHorizontally { get; set; }

    /// <summary>
    /// 是否垂直翻转
    /// </summary>
    public bool IsFlipVertially { get; set; }

    /// <summary>
    /// 图像深度，8bit or 16bit
    /// </summary>
    public int ImageDetph { get; set; }

    /// <summary>
    /// 采集图像的分辨率
    /// </summary>
    public Size ImageSize { get; }

    /// <summary>
    /// 当前模式色阶上下限
    /// </summary>
    public (int Min, int Max) LevelRange { get; }

    /// <summary>
    /// 当前实际左右色阶
    /// </summary>
    public (int Left, int Right) CurrentLevel { get; set; }

    /// <summary>
    /// 存取图片
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public bool SaveImage(string path);

    /// <summary>
    /// 当前分辨率
    /// </summary>
    public (uint Width, uint Height) Resolution { get; set; }

    /// <summary>
    /// 支持的分辨率种类
    /// </summary>
    public List<(uint Width, uint Height)> Resolutions { get; set; }

    /// <summary>
    /// 当前捕获结果刷新
    /// </summary>
    public event Action<Mat> FrameReceived;

    /// <summary>
    /// 伪彩设置
    /// </summary>
    public int PseudoColor { get; set; }

    /// <summary>
    /// 取单张图
    /// </summary>
    /// <returns></returns>
    public Task<Mat?> CaptureAsync();

    /// <summary>
    /// 增益范围
    /// </summary>
    public (ushort Min, ushort Max) GainRange { get; }

    /// <summary>
    /// 断连状态
    /// </summary>
    public event Action<bool> OnDisConnectState;

    //========================新增========================

    /// <summary>
    /// 设置分辨率，搭配分辨率列表使用
    /// </summary>
    /// <param name="resolution"></param>
    /// <returns></returns>
    public bool SetResolution(int resolution);

    /// <summary>
    /// 获取所有分辨率
    /// </summary>
    /// <returns></returns>
    public List<string> ResolutionsList { get; }

    /// <summary>
    /// 设置图像模式，该硬件共5种
    /// </summary>
    /// <param name="imageMode"></param>
    /// <returns></returns>
    public bool SetImageMode(int imageMode);

    /// <summary>
    /// 获取图像模式
    /// </summary>
    /// <returns></returns>
    public List<string> ImageModesList { get; }

    /// <summary>
    /// 设置ROI
    /// </summary>
    /// <param name="width"></param>
    /// <param name="height"></param>
    /// <param name="offsetX"></param>
    /// <param name="offsetY"></param>
    public void SetROI(int width, int height, int offsetX, int offsetY);

    /// <summary>
    /// 获取ROI集合
    /// </summary>
    /// <returns></returns>
    public List<string> ROIList { get; }

    /// <summary>
    /// 关闭ROI模式，恢复全画幅输出
    /// </summary>
    /// <returns></returns>
    public bool DisableROI();

    /// <summary>
    /// 获取增益模式
    /// </summary>
    /// <returns></returns>
    public List<string> GainList { get; }

    /// <summary>
    /// 设置组合模式，该硬件共7种
    /// </summary>
    /// <param name="resolution"></param>
    /// <returns></returns>
    public bool SetCompositeMode(int mode);

    /// <summary>
    /// 获取组合模式
    /// </summary>
    /// <returns></returns>
    public List<string> CompositeModeList { get; }

    /// <summary>
    /// 断连或释放SDK占用等，用于软件关闭前使用
    /// </summary>
    public bool FreeDevice();

    //------------------千眼狼相机重写---------------//

    /// <summary>
    /// 相机自带伪彩设置
    /// </summary>
    public List<string> PseudoColorList {  get; }

    /// <summary>
    /// 获取帧率限制状态
    /// 可在可设置帧率范围内，设置固定帧率
    /// </summary>
    public bool FrameRateEnable { get; set; }

    /// <summary>
    /// 输入帧率限制数据（在开启帧率限制后生效）
    /// </summary>
    public double FrameRateLimit { get; set; }

    /// <summary>
    /// 图像模式类型
    /// </summary>
    public int ImageModeIndex { get; set; }

    /// <summary>
    /// 图像翻转类型
    /// </summary>
    public int FlipIndex { get; set; }

    /// <summary>
    /// 图像翻转列表
    /// </summary>
    public List<string>FlipList { get; }


}

public enum InfoEnum
{
    Version,
    FrameWork,
    Model,//设备名称
    FirmwareVersion,//固件版本号
    SerialNumber,//设备序列号
}