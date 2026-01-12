using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using Simscop.Spindisk.Core.Interfaces;
using Simscop.Spindisk.Hardware.Revealer;
using Simscop.Spindisk.Wpf.Hardware.Camera.TuCam.SettingViews;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Timers;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfApp1
{
    public partial class RevealerViewModel : ObservableObject
    {
        private readonly ICamera? _camera;
        private readonly System.Timers.Timer? _timer;

        [ObservableProperty]
        private ImageSource? _display;

        // ✅ 帧率限制字段
        private long _lastFrameTime = 0;
        private const long MinFrameInterval = 50; // 20fps 限制 (1000ms / 20)
        private long _droppedFrames = 0;

        public RevealerViewModel()
        {
            _camera = new RevealerCamera();
            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += OnTimerElapsed!;

            _camera.FrameReceived += img =>
            {
                if (img == null || img.Empty())
                {
                    img?.Dispose();
                    return;
                }

                // ✅ 帧率限制逻辑
                long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long elapsed = currentTime - Interlocked.Read(ref _lastFrameTime);

                if (elapsed < MinFrameInterval)
                {
                    // 丢弃帧
                    Interlocked.Increment(ref _droppedFrames);
                    img.Dispose();

                    // 定期统计
                    if (_droppedFrames % 100 == 0)
                    {
                        Debug.WriteLine($"[FPS Limiter] Dropped {_droppedFrames} frames (keeping <20fps)");
                    }
                    return;
                }

                // 更新时间戳
                Interlocked.Exchange(ref _lastFrameTime, currentTime);

                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        Display = BitmapFrame.Create(img.ToBitmapSource());
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("OnCaptureChanged Error: " + ex.Message);
                    }
                    finally
                    {
                        img?.Dispose();
                    }
                });
            };
        }

        private readonly bool isAcqMessage = false;

        void InitSetting()
        {
            ExposureRangeMax = _camera!.ExposureRange.Max;
            ExposureRangeMin = _camera.ExposureRange.Min;
            LevelRangeMax = _camera.LevelRange.Max;
            LevelRangeMin = _camera.LevelRange.Min;

            Application.Current.Dispatcher.Invoke(() =>
            {
                RoiList.Clear();
                foreach (var item in _camera.ROIList)
                {
                    RoiList.Add(item);
                }
            });

            ResolutionsList = _camera.ResolutionsList;
            ImageModeList = _camera.ImageModesList;
            CompositeModeList = _camera.CompositeModeList;
            PseudoColorList = _camera.PseudoColorList;
            FrameRateLimit = _camera.FrameRateLimit; 
            FlipList = _camera.FlipList;

            FlipIndex = _camera.FlipIndex;
            ImageModeIndex = _camera.ImageModeIndex;
            ResolutionIndex = 0;
            ROIIndex = 0;
            preRoiIndex = ROIIndex;
            PseudoColorIndex = 5;
            LeftLevel = _camera.CurrentLevel.Left;
            RightLevel = _camera.CurrentLevel.Right;

            IsAutoExposure = _camera.IsAutoExposure;
            IsAutoLevel = _camera.IsAutoLevel;

            Exposure = 50;

            FrameRateEnable = false;
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            Exposure = _camera!.Exposure;
            LeftLevel = _camera.CurrentLevel.Left;
            RightLevel = _camera.CurrentLevel.Right;
            FrameRate = _camera!.FrameRate;
            //FrameRateLimit = _camera!.FrameRateLimit;
            Gamma = _camera!.Gamma;
            Contrast = _camera!.Contrast;
            Brightness = _camera!.Brightness;
        }

        [ObservableProperty]
        public double _exposure;

        [ObservableProperty]
        public double _exposureRangeMax;

        [ObservableProperty]
        public double _exposureRangeMin;

        [ObservableProperty]
        public bool _isAutoExposure = false;

        [ObservableProperty]
        public bool _isExposureTextboxEnable = true;

        [ObservableProperty]
        public double _brightness = 0;

        [ObservableProperty]
        public double _gamma = 1;

        [ObservableProperty]
        public double _contrast = 0;

        [ObservableProperty]
        public double _frameRate = 0;

        [ObservableProperty]
        public List<string>? flipList;

        [ObservableProperty]
        public int flipIndex;

        [ObservableProperty]
        private int _leftLevel = 0;

        [ObservableProperty]
        private int _rightLevel = 0;

        [ObservableProperty]
        public double _LevelRangeMax;

        [ObservableProperty]
        public double _LevelRangeMin;

        [ObservableProperty]
        public bool _isAutoLevel = false;

        [ObservableProperty]
        public bool _isLevelTextboxEnable = true;

        public bool EnableControlwithIsStartAcquisition => !IsStartAcquisition;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EnableControlwithIsStartAcquisition))]
        private bool _isStartAcquisition = false;

        [ObservableProperty]
        private List<string> _imageModeList = new();

        [ObservableProperty]
        private ObservableCollection<string> _roiList = new();

        [ObservableProperty]
        private List<string> _resolutionsList = new();

        [ObservableProperty]
        private List<string> _compositeModeList = new();

        [ObservableProperty]
        private List<string> _pseudoColorList = new();

        [ObservableProperty]
        private int pseudoColorIndex = -1;

        [ObservableProperty]
        private int resolutionIndex = -1;

        [ObservableProperty]
        private int _imageModeIndex;

        [ObservableProperty]
        private int _ROIIndex;

        [ObservableProperty]
        private int _compositeModeIndex = -1;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsOperable))]
        private bool isConnected;

        [ObservableProperty]
        private bool _propControlIsEnable = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsOperable))]
        private bool frameRateEnable = true;

        public bool IsFrameRateEnableControl => FrameRateEnable;

        [ObservableProperty]
        private double frameRateLimit;

        partial void OnFrameRateLimitChanged(double value)
        {
            if (value != _camera!.FrameRateLimit)
                _camera!.FrameRateLimit = value;
        }

        partial void OnFrameRateEnableChanged(bool value)
        {
            if (value != _camera!.FrameRateEnable)
                _camera!.FrameRateEnable = value;
        }

        public bool IsOperable => !IsConnected;

        [RelayCommand]
        void Init()
        {
            IsConnected = _camera!.Init();
            if (IsConnected)
            {
                _timer!.Start();
                InitSetting();
            }
            Debug.WriteLine("Init_" + IsConnected);
        }

        [RelayCommand]
        void RestoretoInitialSettings()
        {
            ResolutionIndex = 0;
            ROIIndex = 0;
            PseudoColorIndex = 5;
            ImageModeIndex = 3;
            FlipIndex = 0;
            Brightness = 50;
            Contrast = 50;
            Gamma = 56;
            Exposure = 50;
            LeftLevel = (int)LevelRangeMin;
            RightLevel = (int)LevelRangeMax;
        }

        private static string? _lastSaveDirectory;

        [ObservableProperty]
        private string _root = "D:\\MoticData\\Camera";

        [ObservableProperty]
        private string _defaultFileName = "";

        [ObservableProperty]
        private bool _timeSuffix = true;

        [RelayCommand]
        async Task CaptureAsync()
        {
            try
            {
                var img = await _camera!.CaptureAsync();
                if (img != null)
                {
                    var image = img!.Clone();

                    var initialDir = _lastSaveDirectory ?? Root;
                    if (!Directory.Exists(initialDir)) Directory.CreateDirectory(initialDir);

                    var name = $"{DefaultFileName}" + $"{(!string.IsNullOrEmpty(DefaultFileName) && TimeSuffix ? "_" : "")}" + $"{(TimeSuffix ? $"{DateTime.Now:yyyyMMdd_HH_mm_ss}" : "")}";

                    var dlg = new SaveFileDialog()
                    {
                        Title = "存储图片",
                        FileName = name,
                        Filter = "TIF|*.tif|PNG|*.png|JPG|*.jpg;*.jpeg|BMP|*.bmp",
                        DefaultExt = ".tif",
                        InitialDirectory = initialDir
                    };

                    if (dlg.ShowDialog() == true)
                    {
                        if (image.Channels() == 4)
                            Cv2.CvtColor(image, image, ColorConversionCodes.BGRA2BGR);

                        if (image!.SaveImage(dlg.FileName))
                        {
                            //Global.Info("保存完成!");

                            if (!string.Equals(Path.GetDirectoryName(dlg.FileName), initialDir, StringComparison.OrdinalIgnoreCase))
                                _lastSaveDirectory = Path.GetDirectoryName(dlg.FileName);
                        }
                    }

                    img?.Dispose();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("CaptureAsy Error:" + ex.Message);
                //Global.Error("CaptureAsy Error:" + ex.Message);
            }
        }

        [RelayCommand]
        void OpenDirectory()
        {
            var direc = _lastSaveDirectory ?? Root;
            OpenDirectoryAndEnter(direc);
        }

        [RelayCommand]
        void StartAcquisition()
        {
            IsStartAcquisition = !IsStartAcquisition;
        }

        async partial void OnIsStartAcquisitionChanged(bool value)
        {
            if (isAcqMessage) return;

            if (value)
            {
                _camera!.StartCapture();
                _timer!.Start();
            }
            else
            {
                PropControlIsEnable = false;

                await Task.Run(() =>
                {
                    _camera?.StopCapture();
                });

                PropControlIsEnable = true;
                _timer!.Stop();
            }
            //WeakReferenceMessenger.Default.Send(new IsCameraAcquisitingMessage(IsStartAcquisition));
        }

        partial void OnIsAutoLevelChanged(bool value)
        {
            IsLevelTextboxEnable = !value;
            _camera!.IsAutoLevel = value;

            //_ = HandleAutoLevelAsync(value);
        }

        private async Task HandleAutoLevelAsync(bool value)
        {
            await Task.Run(() =>
            {
                _camera!.IsAutoLevel = value;
                if (value)
                {
                    Thread.Sleep(2000); // 等待1秒让自动色阶生效
                }
            });

            if (value)
            {
                // 先关闭自动色阶，让数值稳定
                _camera!.IsAutoLevel = false;

                // 更新UI状态
                IsAutoLevel = false;

                // 等待一小段时间确保状态更新
                await Task.Delay(50);

                // 获取关闭后稳定的左色阶值并*1.1
                int adjustedLeftLevel = (int)(_camera!.CurrentLevel.Left * 1.1);

                Debug.WriteLine($"{_camera!.CurrentLevel.Left}__{adjustedLeftLevel}");

                // 如果小于100则设置为100
                if (adjustedLeftLevel < 100)
                    adjustedLeftLevel = 100;

                // 确保不超过最大值，且不超过右色阶-100
                adjustedLeftLevel = Math.Min(adjustedLeftLevel, (int)LevelRangeMax);
                adjustedLeftLevel = Math.Min(adjustedLeftLevel, RightLevel - 100);

                LeftLevel = adjustedLeftLevel;

            }

            IsLevelTextboxEnable = true;
        }

        partial void OnIsAutoExposureChanged(bool value)
        {
            IsExposureTextboxEnable = !value;

            _ = HandleAutoExposureAsync(value);
        }

        private async Task HandleAutoExposureAsync(bool value)
        {
            await Task.Run(() =>
            {
                _camera!.IsAutoExposure = value;

                if (value)
                {
                    Thread.Sleep(2000);
                }
            });

            if (value)
                IsAutoExposure = false;

            IsExposureTextboxEnable = true;
        }

        partial void OnExposureChanged(double value)
        {
            if (!IsAutoExposure)
                _camera!.Exposure = value;
        }

        partial void OnGammaChanged(double value)
        {
            if(value!=_camera!.Gamma)
            _camera!.Gamma = value;
        }

        partial void OnBrightnessChanged(double value)
        {
            if (value != _camera!.Brightness)
                _camera!.Brightness = value;
        }

        partial void OnContrastChanged(double value)
        {
            if (value != _camera!.Contrast)
                _camera!.Contrast = value;
        }

        partial void OnImageModeIndexChanged(int value)
        {
            if (_camera!.ImageModeIndex != value)
            {
                _camera!.ImageModeIndex = value;

                LevelRangeMax = _camera!.LevelRange.Max;
                LevelRangeMin = _camera.LevelRange.Min;
            }
        }

        partial void OnLeftLevelChanged(int value)
        {
            if (!IsAutoLevel&& value != _camera!.CurrentLevel.Left)
                _camera!.CurrentLevel = (value, RightLevel);
        }

        partial void OnRightLevelChanged(int value)
        {
            if (!IsAutoLevel && value != _camera!.CurrentLevel.Right)
                _camera!.CurrentLevel = (LeftLevel, value);
        }

        partial void OnPseudoColorIndexChanged(int value)
        {
            if (value != _camera!.PseudoColor)
                _camera!.PseudoColor = value;
        }

        async partial void OnCompositeModeIndexChanged(int value)
        {
            PropControlIsEnable = false;

            await Task.Run(() =>
            {
                _camera!.SetCompositeMode(value);
            });

            LevelRangeMax = _camera!.LevelRange.Max;
            LevelRangeMin = _camera.LevelRange.Min;

            PropControlIsEnable = true;
        }

        async partial void OnResolutionIndexChanged(int value)
        {
            PropControlIsEnable = false;

            await Task.Run(() =>
            {
                _camera!.SetResolution(value);
                _camera!.DisableROI(); //切换分辨率，重置ROI设置
            });

            PropControlIsEnable = true;

            var (width, height) = ParseResolutionFromList(value);
            if (width > 0 && height > 0)
            {
                //WeakReferenceMessenger.Default.Send(new ResolutionChangeControlScaleMessage(width, height));
                Console.WriteLine($"[RevealerCamViewModel] 分辨率切换: {width}x{height}");
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                IsROIIndexChangedEnable = true;
                RoiList.Clear();
                foreach (var item in _camera!.ROIList)
                {
                    RoiList.Add(item);
                }
                ROIIndex = 0;
                IsROIIndexChangedEnable = false;
            });
        }

        /// <summary>
        /// 从分辨率列表中解析实际分辨率
        /// </summary>
        /// <param name="resolutionIndex">分辨率索引</param>
        /// <returns>宽度和高度</returns>
        private (int Width, int Height) ParseResolutionFromList(int resolutionIndex)
        {
            if (resolutionIndex < 0 || resolutionIndex >= ResolutionsList.Count)
                return (0, 0);

            var resolutionFull = ResolutionsList[resolutionIndex];

            // 匹配 "2048x2048" 或 "2048×2048" 或 "2048*2048" 格式
            var match = Regex.Match(resolutionFull, @"(\d+)\s*[x×*]\s*(\d+)");

            if (match.Success)
            {
                int width = int.Parse(match.Groups[1].Value);
                int height = int.Parse(match.Groups[2].Value);
                return (width, height);
            }

            // 备用方案：根据索引硬编码（基于你之前的代码）
            return resolutionIndex switch
            {
                0 => (2048, 2048), // 2048x2048(标准)
                1 => (2048, 2048), // 2048x2048(增强)
                2 => (1024, 1024), // 1024x1024(2x2Bin)
                3 => (512, 512),   // 512x512(4x4Bin)
                _ => (0, 0)
            };
        }

        partial void OnFlipIndexChanged(int value)
        {
            if (value != _camera!.FlipIndex)
                _camera!.FlipIndex = value;
        }

        private bool IsROIIndexChangedEnable = false;

        async partial void OnROIIndexChanged(int value)
        {
            if (IsROIIndexChangedEnable) return;

            PropControlIsEnable = false;

            await Task.Run(() =>
            {
                ApplyRoi(value);
            });

            PropControlIsEnable = true;
        }

        private void ApplyRoi(int roiIndex)
        {
            if (RoiList[roiIndex].Contains("自定义"))
            {
                OpenCustomROI();
            }
            //else if (RoiList[roiIndex].Contains("全画幅"))
            //{
            //    _camera!.DisableROI();
            //    PropControlIsEnable = false;
            //    CamROI(RoiList[0]);
            //    ROIIndex = 0;//纯粹切换显示
            //    PropControlIsEnable = true;
            //    preRoiIndex = ROIIndex;
            //}
            else
            {
                PropControlIsEnable = false;
                CamROI(RoiList[roiIndex]);
                preRoiIndex = ROIIndex;
                PropControlIsEnable = true;
            }
        }

        private int preRoiIndex = 0;

        private (int Width, int Height) GetCurrentSensorSize()
        {
            return ResolutionIndex switch
            {
                0 => (2048, 2048), // 2048x2040(标准)
                1 => (2048, 2048), // 2048x2040(增强)
                2 => (1024, 1024), // 1024x1020(2x2Bin)
                3 => (512, 512),   // 512x510(4x4Bin)
                _ => (2048, 2048)  // 默认
            };
        }

        private void OpenCustomROI()
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var (maxWidth, maxHeight) = GetCurrentSensorSize();
                var dialog = new CustomROIWindow(maxWidth, maxHeight);

                if (dialog.ShowDialog() == true)
                {
                    _camera?.SetROI(dialog.ROIWidth, dialog.ROIHeight, dialog.OffsetX, dialog.OffsetY);
                }
                else
                {
                    PropControlIsEnable = false;
                    ROIIndex = preRoiIndex;//纯粹切换显示
                    PropControlIsEnable = true;
                }
            });
        }

        private void CamROI(string roi)
        {
            var match = Regex.Match(roi, @"(\d+)\s*x\s*(\d+)");

            if (match.Success)
            {
                int width = int.Parse(match.Groups[1].Value);
                int height = int.Parse(match.Groups[2].Value);

                //不超过当前分辨率设置
                var (maxWidth, maxHeight) = GetCurrentSensorSize();

                if (width > maxWidth || height > maxHeight)
                {
                    MessageBox.Show($"ROI尺寸超过限制");
                    //Global.Error($"ROI尺寸超过限制");
                    return;
                }

                int offsetX = (maxWidth - width) / 2;
                int offsetY = (maxHeight - height) / 2;

                _camera?.SetROI(width, height, offsetX, offsetY);
            }
        }

        private static void OpenDirectoryAndEnter(string directory)
        {
            if (!string.IsNullOrEmpty(directory))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"\"{directory}\"");
            }
        }
    }
}
