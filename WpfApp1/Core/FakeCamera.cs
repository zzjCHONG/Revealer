using OpenCvSharp;
using Simscop.Spindisk.Core.Interfaces;
using System.Windows;
using System.Windows.Threading;
using Size = OpenCvSharp.Size;

namespace Simscop.Spindisk.Core.FakeHardware
{
    public class FakeCamera : ICamera
    {
        private readonly DispatcherTimer _timer;
        private readonly object _lock = new();
        private readonly string[] _imagePaths;

        private Mat? _latestCapturedImage;
        private int _imageIndex;


        public FakeCamera()
        {
            var root = AppContext.BaseDirectory + "TestImage\\";
            _imagePaths = new[] { $"{root}1_3.tif", $"{root}4_2.tif" };//1_1.bmp;1_3.tif, $"{root}4_2.tif",$"{root}4_3.tif"

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(0.5)
            };
            _timer.Tick += OnTimerTick;
        }

        public bool Init()
        {
            //Thread.Sleep(2000);
            return true;
        }

        public Dictionary<InfoEnum, string> InfoDirectory => new()
        {
            { InfoEnum.Model, "Toupcam" },
            { InfoEnum.Version, "v1.0" }
        };

        public Size ImageSize { get; private set; } = new(0, 0);

        public double FrameRate => 0;
        public (double Min, double Max) ExposureRange => (0, 10000);
        public double Exposure { get; set; }
        public bool IsAutoExposure { get; set; } = true;

        public (double Min, double Max) TemperatureRange => (0, 10000);
        public double Temperature { get; set; }

        public (double Min, double Max) TintRange => (0, 10000);
        public double Tint { get; set; }

        public bool AutoWhiteBlanceOnce() => true;

        public double Gamma { get; set; }
        public double Contrast { get; set; }
        public double Brightness { get; set; }

        public (ushort Min, ushort Max) GainRange => (0, 10000);
        public ushort Gain { get; set; } = 1;

        public bool IsAutoLevel { get; set; } = true;
        public (int Min, int Max) LevelRange => (0, 10000);
        public (int Left, int Right) CurrentLevel { get; set; } = (0, 10000);

        public int PseudoColor { get; set; } = 1;
        public int ClockwiseRotation { get; set; } = 1;
        public bool IsFlipHorizontally { get; set; }
        public bool IsFlipVertially { get; set; }

        public int ImageDetph
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public (uint Width, uint Height) Resolution { get; set; } = (2048, 2048);

        public List<(uint Width, uint Height)> Resolutions { get; set; } = new()
        {
            (4096, 4096),
            (2048, 2048),
            (1024, 1024)
        };

        public List<string> ResolutionsList => new()
        {
            "2048*2048",
            "1024*1024",
            "512*512"
        };

        public bool SetResolution(int resolution) => true;

        public List<string> ImageModesList => new()
        {
            "Mode1",
            "Mode2",
            "Mode3"
        };

        public bool SetImageMode(int imageMode) => true;

        public List<string> ROIList => new()
        {
            "ROI1",
            "ROI2",
            "ROI3"
        };

        public void SetROI(int width, int height, int offsetX, int offsetY)
        {
            // Fake implementation
        }

        public bool DisableROI() => true;

        public List<string> GainList => new()
        {
            "Low",
            "Medium",
            "High"
        };

        public List<string> CompositeModeList => new()
        {
            "Mode1",
            "Mode2",
            "Mode3",
            "Mode4",
            "Mode5",
            "Mode6",
            "Mode7"
        };

        public List<string> PseudoColorList => new() { "默认", "伪彩1", "伪彩2" };

        public bool FrameRateEnable { get; set; } = true;
        public double FrameRateLimit { get; set; } = 0;
        public int ImageModeIndex { get; set; } = 0;
        public int FlipIndex { get; set; } = 0;

        public List<string> FlipList => new() { "默认", "垂直翻转", "水平翻转" };

        public bool SetCompositeMode(int mode) => true;


        public bool StartCapture()
        {
            _timer.Start();
            return true;
        }

        public bool StopCapture()
        {
            _timer.Stop();
            return true;
        }

        public bool Capture(out Mat? img)
        {
            lock (_lock)
            {
                if (_latestCapturedImage != null)
                {
                    img = _latestCapturedImage.Clone();
                    return true;
                }
            }

            img = null;
            return false;
        }

        public async Task<Mat?> CaptureAsync()
        {
            await Task.Delay(10);

            lock (_lock)
            {
                return _latestCapturedImage?.Clone();
            }
        }

        public bool SaveImage(string path)
        {
            throw new NotImplementedException();
        }

        public event Action<Mat>? FrameReceived;
        public event Action<bool>? OnDisConnectState;

        private void OnTimerTick(object? sender, EventArgs e)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var img = LoadNextImage();
                    if (img == null) return;

                    UpdateLatestImage(img);
                    ImageSize = new Size(img.Width, img.Height);
                    FrameReceived?.Invoke(img);
                }
                catch (Exception ex)
                {
                    // Log error in production code
                    System.Diagnostics.Debug.WriteLine($"Error in timer tick: {ex.Message}");
                }
            });
        }

        private Mat? LoadNextImage()
        {
            if (_imagePaths.Length == 0) return null;

            var path = _imagePaths[_imageIndex % _imagePaths.Length];
            _imageIndex++;

            return Cv2.ImRead(path, ImreadModes.Unchanged);
        }

        private void UpdateLatestImage(Mat img)
        {
            lock (_lock)
            {
                _latestCapturedImage?.Dispose();
                _latestCapturedImage = img.Clone();
            }
        }

        public bool FreeDevice()
        {
            return true;
        }

    }
}