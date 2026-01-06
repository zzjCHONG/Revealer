using System.Windows;

namespace Simscop.Spindisk.Wpf.Hardware.Camera.TuCam.SettingViews
{
    /// <summary>
    /// CustomROIWindow.xaml 的交互逻辑
    /// </summary>
    public partial class CustomROIWindow : Lift.UI.Controls.Window
    {
        public int ROIWidth { get; private set; }
        public int ROIHeight { get; private set; }
        public int OffsetX { get; private set; }
        public int OffsetY { get; private set; }

        private readonly int _maxWidth;
        private readonly int _maxHeight;

        public CustomROIWindow(int maxWidth, int maxHeight)
        {
            InitializeComponent();
            _maxWidth = maxWidth;
            _maxHeight = maxHeight;
            WidthTextBox.Text = $"{maxWidth}";
            HeightTextBox.Text = $"{maxHeight}";
            OffsetXTextBox.Text = "0";
            OffsetYTextBox.Text = "0";
            CustomTitle.Text = $"自定义ROI(最大: {maxWidth}x{maxHeight})";
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(WidthTextBox.Text, out int width) &&
                int.TryParse(HeightTextBox.Text, out int height) &&
                int.TryParse(OffsetXTextBox.Text, out int offsetX) &&
                int.TryParse(OffsetYTextBox.Text, out int offsetY))
            {
                // 验证参数范围
                if (width <= 0 || height <= 0 ||
                    offsetX < 0 || offsetY < 0 ||
                    width + offsetX > _maxWidth ||
                    height + offsetY > _maxHeight)
                {
                    MessageBox.Show($"参数超出有效范围！\n当前分辨率限制: {_maxWidth}x{_maxHeight}");
                    //Global.Warn($"参数超出有效范围！\n当前分辨率限制: {_maxWidth}x{_maxHeight}");
                    return;
                }

                int cameraOffsetY = _maxHeight - (offsetY + height);

                ROIWidth = width;
                ROIHeight = height;
                OffsetX = offsetX;
                OffsetY = cameraOffsetY;

                this.DialogResult = true;
            }
            else
            {
                MessageBox.Show("请输入有效的整数！");
                //Global.Info("请输入有效的整数！");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}
