using System;
using System.Windows;
using System.Windows.Threading;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace Orbital
{
    public partial class ResultTooltipWindow : Window
    {
        private readonly DispatcherTimer _autoCloseTimer;
        private readonly PopupAnchorContext _anchors;

        public ResultTooltipWindow(string result, PopupAnchorContext anchors)
        {
            InitializeComponent();
            ResultText.Text = result;
            _anchors = anchors;

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
            Rect workArea = new(
                screen.WorkingArea.Left * dpiX,
                screen.WorkingArea.Top * dpiY,
                screen.WorkingArea.Width * dpiX,
                screen.WorkingArea.Height * dpiY);
            var normalizedAnchors = new PopupAnchorContext(
                new Point(_anchors.CursorDip.X * dpiX, _anchors.CursorDip.Y * dpiY),
                _anchors.SelectionDip is Rect selectionPx
                    ? new Rect(selectionPx.X * dpiX, selectionPx.Y * dpiY, selectionPx.Width * dpiX, selectionPx.Height * dpiY)
                    : null,
                _anchors.ActionBarDip is Rect actionBarPx
                    ? new Rect(actionBarPx.X * dpiX, actionBarPx.Y * dpiY, actionBarPx.Width * dpiX, actionBarPx.Height * dpiY)
                    : null);

            Point clamped = PopupPlacementCalculator.CalculatePosition(
                SettingsManager.CurrentSettings.PopupPlacement,
                normalizedAnchors,
                workArea,
                new Size(ActualWidth, ActualHeight));
            Left = clamped.X;
            Top = clamped.Y;
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
