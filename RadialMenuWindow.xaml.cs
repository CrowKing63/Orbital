using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        // LLM 호출이 필요한 ResultAction 목록
        private static readonly System.Collections.Generic.HashSet<string> LlmActions =
            new() { "Replace", "Copy", "Popup" };

        private void PopulateRadialButtons(bool hasText)
        {
            ButtonCanvas.Children.Clear();

            var actions = SettingsManager.CurrentSettings?.Actions;
            if (actions == null || actions.Count == 0) return;

            double radius = 88;
            double centerX = 115;
            double centerY = 115;

            for (int i = 0; i < actions.Count; i++)
            {
                double angle = i * (Math.PI * 2) / actions.Count - Math.PI / 2;
                double x = centerX + radius * Math.Cos(angle) - 25;
                double y = centerY + radius * Math.Sin(angle) - 25;

                var action = actions[i];
                bool enabled = hasText || !LlmActions.Contains(action.ResultAction);

                var btn = new Button
                {
                    Content = action.Name,
                    Tag = action,
                    Style = (Style)FindResource("RadialButtonStyle"),
                    IsEnabled = enabled,
                    Opacity = enabled ? 1.0 : 0.35
                };

                btn.Click += ActionButton_Click;
                Canvas.SetLeft(btn, x);
                Canvas.SetTop(btn, y);
                ButtonCanvas.Children.Add(btn);
            }
        }

        public void ShowAtCursor(int mouseX, int mouseY, string text)
        {
            SelectedText = text;
            PopulateRadialButtons(!string.IsNullOrEmpty(text));

            Show();

            PresentationSource source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

            Left = mouseX * dpiX - Width / 2;
            Top  = mouseY * dpiY - Height / 2;

            // 화면 경계 보정
            Rect workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left, Math.Min(Left, workArea.Right  - Width));
            Top  = Math.Max(workArea.Top,  Math.Min(Top,  workArea.Bottom - Height));

            Activate();
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: ActionProfile action }) return;

            Hide();

            if (_actionExecutor == null)
            {
                MessageBox.Show(
                    "API 키가 설정되지 않았습니다.\n시스템 트레이 아이콘을 더블클릭하여 설정 창을 여세요.",
                    "Orbit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await Task.Run(() => _actionExecutor.ExecuteAsync(action, SelectedText));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    var tooltip = new ResultTooltipWindow($"오류가 발생했습니다:\n{ex.Message}");
                    tooltip.Show();
                });
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            Hide();
        }
    }
}
