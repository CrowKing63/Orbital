using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace Orbital
{
    public partial class App : System.Windows.Application
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private RadialMenuWindow _radialMenu = null!;
        private WinForms.NotifyIcon _notifyIcon = null!;
        private ActionExecutorService? _actionExecutor;
        private CancellationTokenSource? _selectionCts;
        private readonly object _selectionLock = new object();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            bool recovered = SettingsManager.LoadSettings();
            if (recovered)
            {
                MessageBox.Show("Your settings file was corrupted and has been reset to defaults.\n\nA backup of the corrupted file was saved with a .bak extension.", "Orbital Settings Recovery", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            ApplyTheme(SettingsManager.CurrentSettings.Theme);

            // Sync startup registry with settings
            if (SettingsManager.CurrentSettings.RunAtStartup != SettingsManager.IsStartupRegistryEnabled())
            {
                SettingsManager.ApplyStartupRegistry(SettingsManager.CurrentSettings.RunAtStartup);
            }

            RebuildActionExecutor();
            InitializeTrayIcon();

            _radialMenu = new RadialMenuWindow(_actionExecutor);

            SystemHookManager.OnAnyMouseDown += SystemHookManager_OnAnyMouseDown;
            SystemHookManager.OnMouseUp += SystemHookManager_OnMouseUp;
            SystemHookManager.OnLongPress += SystemHookManager_OnLongPress;
            
            if (!SystemHookManager.StartMouseHook(out int hookErrorCode))
            {
                string errorMessage = $"Failed to install global mouse hook (Error code: {hookErrorCode}).\n\n" +
                                    "Orbital requires this hook to detect text selection gestures.\n" +
                                    "The application will now exit.\n\n" +
                                    "Common causes:\n" +
                                    "- Insufficient permissions\n" +
                                    "- Conflicting software (security tools, other hook-based apps)\n" +
                                    "- System resource limitations";
                
                MessageBox.Show(errorMessage, "Orbital Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }
        }

        public static void ApplyTheme(string themeName)
        {
            var dicts = Current.Resources.MergedDictionaries;
            var existing = dicts.FirstOrDefault(d =>
                d.Source != null &&
                (d.Source.OriginalString.Contains("Dark.xaml") ||
                 d.Source.OriginalString.Contains("Light.xaml")));

            if (existing != null)
                dicts.Remove(existing);

            string path = themeName == "Light" ? "Themes/Light.xaml" : "Themes/Dark.xaml";
            dicts.Insert(0, new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });

            SettingsManager.CurrentSettings.Theme = themeName;
        }

        private void OpenSettingsWindow()
        {
            var win = new SettingsWindow();
            win.ShowDialog();
            RebuildActionExecutor();
            _radialMenu.UpdateActionExecutor(_actionExecutor);
        }

        private void RebuildActionExecutor()
        {
            // Dispose old service if it exists
            if (_actionExecutor?.LlmService is IDisposable disposable)
            {
                disposable.Dispose();
            }

            string apiKey = SettingsManager.GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                _actionExecutor = new ActionExecutorService(null);
                return;
            }
            string baseUrl = string.IsNullOrWhiteSpace(SettingsManager.CurrentSettings.ApiBaseUrl)
                ? "https://api.openai.com/v1"
                : SettingsManager.CurrentSettings.ApiBaseUrl;
            string modelName = string.IsNullOrWhiteSpace(SettingsManager.CurrentSettings.ModelName)
                ? "gpt-4o-mini"
                : SettingsManager.CurrentSettings.ModelName;
            
            try
            {
                _actionExecutor = new ActionExecutorService(new OpenAiApiService(apiKey, baseUrl, modelName));
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show($"Invalid API configuration: {ex.Message}\n\nPlease check your settings.", 
                    "Orbital Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                _actionExecutor = new ActionExecutorService(null);
            }
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new WinForms.NotifyIcon
            {
                Text = "Orbital - Text AI Assistant",
                Visible = true
            };

            try
            {
                // WPF 리소스로부터 이미지 로드하여 트레이 아이콘으로 설정
                var uri = new Uri("pack://application:,,,/Assets/orbit_logo.png");
                var streamInfo = System.Windows.Application.GetResourceStream(uri);
                if (streamInfo != null)
                {
                    using (var stream = streamInfo.Stream)
                    using (var bitmap = new Bitmap(stream))
                    {
                        // Bitmap을 Icon으로 변환. Clone()으로 소유권을 가진 Icon을 만든 뒤 원본 HICON 해제.
                        IntPtr hIcon = bitmap.GetHicon();
                        _notifyIcon.Icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
                        DestroyIcon(hIcon);
                    }
                }
                else
                {
                    _notifyIcon.Icon = SystemIcons.Application;
                }
            }
            catch
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }

            var menu = new WinForms.ContextMenuStrip();

            menu.Items.Add("Settings", null, (s, e) => Dispatcher.Invoke(OpenSettingsWindow));

            menu.Items.Add(new WinForms.ToolStripSeparator());

            menu.Items.Add("Exit", null, (s, e) =>
            {
                _notifyIcon.Visible = false;
                Dispatcher.Invoke(Shutdown);
            });

            _notifyIcon.ContextMenuStrip = menu;

            // 더블클릭으로도 설정 열기
            _notifyIcon.DoubleClick += (s, e) => Dispatcher.Invoke(OpenSettingsWindow);
        }

        private void SystemHookManager_OnAnyMouseDown(object? sender, SystemHookManager.MousePoint e)
        {
            // BeginInvoke: 훅 콜백은 UI 스레드에서 실행되므로 Invoke 대신 BeginInvoke 사용
            Dispatcher.BeginInvoke(() =>
            {
                if (!_radialMenu.IsVisible) return;

                var source = PresentationSource.FromVisual(_radialMenu);
                if (source?.CompositionTarget == null) return;
                double dpiX = source.CompositionTarget.TransformFromDevice.M11;
                double dpiY = source.CompositionTarget.TransformFromDevice.M22;

                double mouseXDip = e.X * dpiX;
                double mouseYDip = e.Y * dpiY;

                bool insideWindow = mouseXDip >= _radialMenu.Left &&
                                    mouseXDip <= _radialMenu.Left + _radialMenu.Width &&
                                    mouseYDip >= _radialMenu.Top &&
                                    mouseYDip <= _radialMenu.Top + _radialMenu.Height;

                if (!insideWindow)
                    _radialMenu.Hide();
            });
        }

        private void SystemHookManager_OnMouseUp(object? sender, SystemHookManager.MousePoint e)
        {
            CancellationToken token;
            lock (_selectionLock)
            {
                _selectionCts?.Cancel();
                _selectionCts?.Dispose();
                _selectionCts = new CancellationTokenSource();
                token = _selectionCts.Token;
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                if (token.IsCancellationRequested) return;

                Thread.Sleep(50);
                if (token.IsCancellationRequested) return;

                string selectedText = ClipboardHelper.GetSelectedText();

                if (!string.IsNullOrWhiteSpace(selectedText) && !token.IsCancellationRequested)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!token.IsCancellationRequested)
                            _radialMenu.ShowAtCursor(e.X, e.Y, selectedText);
                    });
                }
            }, token);
        }

        private void SystemHookManager_OnLongPress(object? sender, SystemHookManager.MousePoint e)
        {
            // Only show popup when long-pressing over an editable control
            if (!IsOverEditableControl(e.X, e.Y)) return;

            Dispatcher.Invoke(() =>
            {
                _radialMenu.ShowAtCursor(e.X, e.Y, string.Empty);
            });
        }

        private static bool IsOverEditableControl(int screenX, int screenY)
        {
            try
            {
                var element = AutomationElement.FromPoint(new System.Windows.Point(screenX, screenY));
                if (element == null) return false;

                var controlType = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;
                if (controlType == ControlType.Edit || controlType == ControlType.Document)
                    return true;

                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObj)
                    && valuePatternObj is ValuePattern vp && !vp.Current.IsReadOnly)
                    return true;

                if (element.TryGetCurrentPattern(TextPattern.Pattern, out _))
                    return true;

                return false;
            }
            catch { return false; }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemHookManager.StopMouseHook();
            _notifyIcon?.Dispose();
            _selectionCts?.Dispose();
            
            // Dispose LLM service if it implements IDisposable
            if (_actionExecutor?.LlmService is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            base.OnExit(e);
        }
    }
}
