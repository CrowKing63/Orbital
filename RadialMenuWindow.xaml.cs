using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Orbit
{
    public partial class RadialMenuWindow : Window
    {
        public string SelectedText { get; private set; } = string.Empty;

        public RadialMenuWindow()
        {
            InitializeComponent();
            PopulateRadialButtons();
        }

        private void PopulateRadialButtons()
        {
            // Sample actions
            string[] actions = { "번역", "요약", "수정", "복사", "검색" };
            double radius = 75; // Distance from center
            double centerX = 100; // Half of Canvas width
            double centerY = 100;

            for (int i = 0; i < actions.Length; i++)
            {
                double angleIndex = i * (Math.PI * 2) / actions.Length;
                // Offset by -90 deg to start at 12 o'clock
                double x = centerX + radius * Math.Cos(angleIndex - Math.PI / 2) - 25; // 25 is half button width
                double y = centerY + radius * Math.Sin(angleIndex - Math.PI / 2) - 25;

                Button btn = new Button
                {
                    Content = actions[i],
                    Style = (Style)FindResource("RadialButtonStyle")
                };
                
                btn.Click += ActionButton_Click;

                Canvas.SetLeft(btn, x);
                Canvas.SetTop(btn, y);
                ButtonCanvas.Children.Add(btn);
            }
        }

        public void ShowAtCursor(int mouseX, int mouseY, string text)
        {
            this.SelectedText = text;

            // We must call Show() first so WPF can attach a PresentationSource to this window
            this.Show();

            // Convert raw physical screen pixels from the hook to WPF logical pixels (DIP) based on current DPI
            PresentationSource source = PresentationSource.FromVisual(this);
            double dpiFactorX = 1.0;
            double dpiFactorY = 1.0;

            if (source?.CompositionTarget != null)
            {
                dpiFactorX = source.CompositionTarget.TransformFromDevice.M11;
                dpiFactorY = source.CompositionTarget.TransformFromDevice.M22;
            }

            double logicalX = mouseX * dpiFactorX;
            double logicalY = mouseY * dpiFactorY;

            // X and Y denote the center of the radial menu.
            this.Left = logicalX - (this.Width / 2);
            this.Top = logicalY - (this.Height / 2);

            this.Activate(); // Bring to front and focus
        }

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Content: string action })
                return;

            // Handle actions here. For now, we simulate simple console output.
            Debug.WriteLine($"Action '{action}' executed on text: {SelectedText}");

            if (action == "번역")
            {
                // Simulated LLM translation
                ClipboardHelper.ReplaceSelectedText($"[번역됨] {SelectedText}");
            }
            else if (action == "복사")
            {
                Clipboard.SetText(SelectedText);
            }

            this.Hide();
        }

        // Hide when user clicks outside the window
        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }
    }
}
