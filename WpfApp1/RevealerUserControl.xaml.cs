using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WpfApp1
{
    /// <summary>
    /// RevealerUserControl.xaml 的交互逻辑
    /// </summary>
    public partial class RevealerUserControl : UserControl
    {
        private readonly RevealerViewModel? VM;
        private Window? _parentWindow;
        private bool _isEventRegistered = false;

        public RevealerUserControl()
        {
            InitializeComponent();

            //VM = Global.ServiceProvider?.GetService<TUCamViewModel>();
            VM = new RevealerViewModel();
            this.DataContext = VM;

            this.Loaded += OnControlLoaded;
            this.Unloaded += OnControlUnloaded;
        }

        #region 生命周期事件注册

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            RegisterGlobalClickEvent();
        }

        private void OnControlUnloaded(object sender, RoutedEventArgs e)
        {
            UnregisterGlobalClickEvent();
        }

        private void RegisterGlobalClickEvent()
        {
            if (_isEventRegistered) return;

            _parentWindow = Window.GetWindow(this);
            if (_parentWindow != null)
            {
                _parentWindow.PreviewMouseDown += ParentWindow_PreviewMouseDown;
                _isEventRegistered = true;
            }
        }

        private void UnregisterGlobalClickEvent()
        {
            if (!_isEventRegistered) return;

            if (_parentWindow != null)
            {
                _parentWindow.PreviewMouseDown -= ParentWindow_PreviewMouseDown;
                _parentWindow = null;
            }

            _isEventRegistered = false;
        }

        #endregion

        #region 全局点击事件处理

        private void ParentWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not FrameworkElement clickedElement)
                return;

            // 点击了 TextBox 内部，直接返回
            if (IsDescendantOfTextBox(clickedElement))
                return;

            // 判断是否点击了当前控件内部
            bool isClickInsideThisControl = IsElementInsideControl(clickedElement, this);

            var focusedTextBox = GetFocusedTextBoxInControl();
            if (focusedTextBox == null)
                return;

            // 离开控件时或点击控件内部其他区域时都进行输入校验
            ProcessTextBoxInput(focusedTextBox);
        }

        private static bool IsElementInsideControl(FrameworkElement element, FrameworkElement control)
        {
            if (element == null || control == null) return false;

            DependencyObject current = element;
            while (current != null)
            {
                if (current == control)
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private TextBox? GetFocusedTextBoxInControl()
        {
            var textBoxes = FindVisualChildren<TextBox>(this);
            return textBoxes.FirstOrDefault(tb => tb.IsFocused);
        }

        private static bool IsDescendantOfTextBox(FrameworkElement element)
        {
            while (element != null)
            {
                if (element is TextBox)
                    return true;
                element = element.Parent as FrameworkElement;
            }
            return false;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T tChild)
                    yield return tChild;

                foreach (var childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }

        #endregion

        #region 输入处理逻辑

        private void ProcessTextBoxInput(TextBox textBox)
        {
            if (textBox == null) return;

            if (!double.TryParse(textBox.Text, out double value))
            {
                // 非法输入时回退绑定值
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                return;
            }

            // 根据 TextBox 名称限制范围
            value = textBox.Name switch
            {
                "BrightnessTextBox" => Math.Clamp(value, -100, 100),
                "ContrastTextBox" => Math.Clamp(value, 0, 100),
                "GammaTextBox" => Math.Clamp(value, 0, 100),
                "ExposureTextBox" => Math.Clamp(value, VM!.ExposureRangeMin, VM.ExposureRangeMax),
                "LeftLevelTextBox" or "RightLevelTextBox" => Math.Clamp(value, VM!.LevelRangeMin, VM.LevelRangeMax),
                "FrameRateLimit" => Math.Clamp(value, 0, VM!.FrameRate),
                _ => value
            };

            // 格式化输出
            textBox.Text = value.ToString("F0");

            // 更新绑定源
            var binding = textBox.GetBindingExpression(TextBox.TextProperty);
            binding?.UpdateSource();

            Keyboard.ClearFocus();
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (sender is not TextBox textBox) return;

            ProcessTextBoxInput(textBox);
            e.Handled = true;
        }

        #endregion
    }
}
