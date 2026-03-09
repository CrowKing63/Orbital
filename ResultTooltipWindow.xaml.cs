using System;
using System.Windows;
using System.Windows.Threading;

namespace Orbital
{
    public partial class ResultTooltipWindow : Window
    {
        private readonly DispatcherTimer _autoCloseTimer;

        public ResultTooltipWindow(string result)
        {
            InitializeComponent();
            ResultText.Text = result;

            // 로드된 후 화면 우측 하단에 위치
            Loaded += OnLoaded;

            // 20초 후 자동 닫기
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _autoCloseTimer.Tick += (s, e) => { _autoCloseTimer.Stop(); Close(); };
            _autoCloseTimer.Start();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PresentationSource? source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

            var cursorPosition = System.Windows.Forms.Cursor.Position;
            var screen = System.Windows.Forms.Screen.FromPoint(cursorPosition);

            double workAreaRight = screen.WorkingArea.Right * dpiX;
            double workAreaBottom = screen.WorkingArea.Bottom * dpiY;

            Left = workAreaRight - ActualWidth - 20;
            Top = workAreaBottom - ActualHeight - 20;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _autoCloseTimer.Stop();
            Close();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(ResultText.Text);
        }
    }
}
