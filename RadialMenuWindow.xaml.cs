using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Orbital
{
    public partial class RadialMenuWindow : Window
    {
        private static readonly System.Windows.Media.FontFamily _iconFont = new System.Windows.Media.FontFamily("Segoe MDL2 Assets");
        private static readonly System.Windows.Media.FontFamily _emojiFont = new System.Windows.Media.FontFamily("Segoe UI Emoji");

        // Returns MDL2 font for private-use-area characters (U+E000–U+F8FF), emoji font otherwise.
        private static System.Windows.Media.FontFamily ResolveIconFont(string icon)
        {
            foreach (char c in icon)
                if (c >= '\uE000' && c <= '\uF8FF') return _iconFont;
            return _emojiFont;
        }

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

        private void PopulateBarButtons(bool isEditable, bool hasText)
        {
            ButtonPanel.Children.Clear();

            var actions = SettingsManager.CurrentSettings?.Actions;
            if (actions == null || actions.Count == 0) return;

            bool firstVisible = true;

            foreach (var action in actions)
            {
                // Hide actions that write to the target document when the target is read-only.
                // Replace simulates Ctrl+V (writes), so it is also excluded from read-only contexts.
                bool requiresWrite = action.ActionType == ActionType.Paste  ||
                                     action.ActionType == ActionType.Cut    ||
                                     action.ActionType == ActionType.Delete ||
                                     action.ActionType == ActionType.Replace;
                if (requiresWrite && !isEditable)
                    continue;

                // Separator before every button except the first visible one
                if (!firstVisible)
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

                // Create content with icon if available
                object content;
                bool showIcon = !string.IsNullOrEmpty(action.Icon)
                    && action.DisplayMode != ButtonDisplayMode.TextOnly;
                bool showText = action.DisplayMode != ButtonDisplayMode.IconOnly;

                if (showIcon && showText)
                {
                    var stack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                    stack.Children.Add(new TextBlock
                    {
                        Text = action.Icon,
                        FontFamily = ResolveIconFont(action.Icon),
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    });
                    stack.Children.Add(new TextBlock
                    {
                        Text = action.Name,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    content = stack;
                }
                else if (showIcon)
                {
                    content = new TextBlock
                    {
                        Text = action.Icon,
                        FontFamily = ResolveIconFont(action.Icon),
                        FontSize = 16,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                else
                {
                    content = action.Name;
                }

                // Hide actions that need a text selection when nothing is selected.
                // (IsSelectionRequired is false only for Paste.)
                if (action.IsSelectionRequired && !hasText)
                    continue;

                var btn = new Button
                {
                    Content = content,
                    Tag = action,
                    Style = (Style)FindResource("BarButtonStyle"),
                    IsEnabled = true,
                    Opacity = 1.0
                };

                btn.Click += ActionButton_Click;
                ButtonPanel.Children.Add(btn);
                firstVisible = false;
            }
        }

        public void ShowAtCursor(int mouseX, int mouseY, bool isEditable = true, bool hasText = false)
        {
            PopulateBarButtons(isEditable, hasText);

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
                    Loc.Get("Str_ApiKeyNotConfiguredDetail"),
                    Loc.Get("Str_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (_actionExecutor != null)
                {
                    await Task.Run(async () =>
                    {
                        string text = string.Empty;
                        if (action.IsSelectionRequired)
                        {
                            // Wait for focus to return to the source window before reading
                            await System.Threading.Tasks.Task.Delay(120);
                            text = ClipboardHelper.GetSelectedText();
                            if (string.IsNullOrWhiteSpace(text))
                                return; // Nothing selected — abort silently
                        }
                        await _actionExecutor.ExecuteAsync(action, text);
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    var tooltip = new ResultTooltipWindow(string.Format(Loc.Get("Str_ErrorOccurred"), ex.Message));
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
