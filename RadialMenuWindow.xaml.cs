using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Orbit
{
    public partial class RadialMenuWindow : Window
    {
        public string SelectedText { get; private set; } = string.Empty;
        private ActionExecutorService? _actionExecutor;

        public RadialMenuWindow(ActionExecutorService? actionExecutor)
        {
            InitializeComponent();
            _actionExecutor = actionExecutor;
        }

        public void UpdateActionExecutor(ActionExecutorService? executor)
        {
            _actionExecutor = executor;
        }

        private void PopulateBarButtons(bool hasText)
        {
            ButtonPanel.Children.Clear();

            var actions = SettingsManager.CurrentSettings?.Actions;
            if (actions == null || actions.Count == 0) return;

            for (int i = 0; i < actions.Count; i++)
            {
                // 구분선 (첫 번째 버튼 앞은 제외)
                if (i > 0)
                {
                    ButtonPanel.Children.Add(new Border
                    {
                        Width = 1,
                        Height = 18,
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x28, 0x70, 0x80, 0xFF)),
                        VerticalAlignment = VerticalAlignment.Center,
                        IsHitTestVisible = false
                    });
                }

                var action = actions[i];
                bool enabled = hasText || !action.IsSelectionRequired;

                var btn = new Button
                {
                    Content = action.Name,
                    Tag = action,
                    Style = (Style)FindResource("BarButtonStyle"),
                    IsEnabled = enabled,
                    Opacity = enabled ? 1.0 : 0.4
                };

                btn.Click += ActionButton_Click;
                ButtonPanel.Children.Add(btn);
            }
        }

        public void ShowAtCursor(int mouseX, int mouseY, string text)
        {
            SelectedText = text;
            PopulateBarButtons(!string.IsNullOrEmpty(text));

            // 측정을 위해 화면 밖에서 불투명도 0으로 먼저 표시
            Opacity = 0;
            Left = -9999;
            Top = -9999;
            Show();
            UpdateLayout();

            PresentationSource? source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

            double cursorX = mouseX * dpiX;
            double cursorY = mouseY * dpiY;

            // 커서 위, 수평 중앙 정렬
            double left = cursorX - ActualWidth / 2;
            double top  = cursorY - ActualHeight - 8;

            // 화면 경계 보정 (모니터 인식)
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(mouseX, mouseY));
            double workAreaLeft = screen.WorkingArea.Left * dpiX;
            double workAreaTop = screen.WorkingArea.Top * dpiY;
            double workAreaRight = screen.WorkingArea.Right * dpiX;

            left = Math.Max(workAreaLeft + 4, Math.Min(left, workAreaRight - ActualWidth - 4));
            if (top < workAreaTop + 4)
                top = cursorY + 8; // 위쪽 공간 부족 → 아래 표시

            Left = left;
            Top = top;
            Opacity = 1;
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: ActionProfile action }) return;

            Hide();

            bool isLlmAction = action.ActionType.RequiresLlm();
            if (isLlmAction && (_actionExecutor == null || !_actionExecutor.HasLlmService))
            {
                MessageBox.Show(
                    "API key is not configured.\nDouble-click the system tray icon to open Settings.",
                    "Orbit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (_actionExecutor != null)
                {
                    await Task.Run(() => _actionExecutor.ExecuteAsync(action, SelectedText));
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    var tooltip = new ResultTooltipWindow($"An error occurred:\n{ex.Message}");
                    tooltip.Show();
                });
            }
        }

        private void PillBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 비주얼 트리를 타고 올라가서 Button 조상이 있으면 버튼 클릭 — 건드리지 않음
            DependencyObject? source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is Button) return;
                source = VisualTreeHelper.GetParent(source);
            }
            Hide();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            Hide();
        }
    }
}
