using System;
using System.Drawing;
using Microsoft.Win32;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using Velopack;
using Velopack.Sources;
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
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged;

            // Sync startup registry with settings
            if (SettingsManager.CurrentSettings.RunAtStartup != SettingsManager.IsStartupRegistryEnabled())
            {
                SettingsManager.ApplyStartupRegistry(SettingsManager.CurrentSettings.RunAtStartup);
            }

            RebuildActionExecutor();
            InitializeTrayIcon();

            _ = Task.Run(CheckForUpdatesAsync);

            _radialMenu = new RadialMenuWindow(_actionExecutor);

            SystemHookManager.OnAnyMouseDown       += SystemHookManager_OnAnyMouseDown;
            SystemHookManager.OnMouseUp             += SystemHookManager_OnMouseUp;
            SystemHookManager.OnLongPress           += SystemHookManager_OnLongPress;
            SystemHookManager.OnDoubleClickRelease  += SystemHookManager_OnDoubleClickRelease;
            SystemHookManager.OnKeyboardSelection   += SystemHookManager_OnKeyboardSelection;
            SystemHookManager.OnEscapePressed       += SystemHookManager_OnEscapePressed;
            SystemHookManager.OnCustomHotkey        += SystemHookManager_OnCustomHotkey;

            ApplyHotkeySettings();

            if (!SystemHookManager.StartMouseHook(out int mouseErrCode))
            {
                string errorMessage = $"Failed to install global mouse hook (Error code: {mouseErrCode}).\n\n" +
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

            // Keyboard hook is best-effort; failures are non-fatal (mouse-only mode still works)
            SystemHookManager.StartKeyboardHook(out _);
        }

        public static void ApplyTheme(string themeName)
        {
            string resolved = themeName == "System" ? GetSystemTheme() : themeName;

            var dicts = Current.Resources.MergedDictionaries;
            var existing = dicts.FirstOrDefault(d =>
                d.Source != null &&
                (d.Source.OriginalString.Contains("Dark.xaml") ||
                 d.Source.OriginalString.Contains("Light.xaml")));

            if (existing != null)
                dicts.Remove(existing);

            string path = resolved == "Light" ? "Themes/Light.xaml" : "Themes/Dark.xaml";
            dicts.Insert(0, new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });

            SettingsManager.CurrentSettings.Theme = themeName; // "System" 그대로 저장
        }

        private static string GetSystemTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return (key?.GetValue("AppsUseLightTheme") is int v && v == 1) ? "Light" : "Dark";
            }
            catch { return "Dark"; }
        }

        private void OnSystemUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General &&
                SettingsManager.CurrentSettings.Theme == "System")
            {
                Dispatcher.BeginInvoke(() => ApplyTheme("System"));
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                var mgr = new UpdateManager(
                    new GithubSource("https://github.com/CrowKing63/Orbital", null, false));
                var updateInfo = await mgr.CheckForUpdatesAsync();
                if (updateInfo != null)
                {
                    await mgr.DownloadUpdatesAsync(updateInfo);
                    Dispatcher.Invoke(() =>
                    {
                        _notifyIcon.ShowBalloonTip(
                            8000,
                            "Orbital Updated",
                            $"Version {updateInfo.TargetFullRelease.Version} has been downloaded. Restart Orbital to apply.",
                            WinForms.ToolTipIcon.Info);
                    });
                }
            }
            catch { /* Update check is best-effort — ignore all errors */ }
        }

        private void OpenSettingsWindow()
        {
            var win = new SettingsWindow();
            win.ShowDialog();
            RebuildActionExecutor();
            ApplyHotkeySettings();
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

        /// <summary>Pushes current hotkey settings from AppSettings into SystemHookManager.</summary>
        public static void ApplyHotkeySettings()
        {
            SystemHookManager.HotkeyModifiers  = SettingsManager.CurrentSettings.HotkeyModifiers;
            SystemHookManager.HotkeyVirtualKey = SettingsManager.CurrentSettings.HotkeyVirtualKey;
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
                var uri = new Uri("pack://application:,,,/Assets/orbit_logo.ico");
                var streamInfo = System.Windows.Application.GetResourceStream(uri);
                if (streamInfo != null)
                {
                    using (var stream = streamInfo.Stream)
                    {
                        _notifyIcon.Icon = new System.Drawing.Icon(stream);
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
            _notifyIcon.DoubleClick += (s, e) => Dispatcher.Invoke(OpenSettingsWindow);
        }

        // ── Event handlers ───────────────────────────────────────────────────────

        private void SystemHookManager_OnAnyMouseDown(object? sender, SystemHookManager.MousePoint e)
        {
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
                                    mouseYDip >= _radialMenu.Top  &&
                                    mouseYDip <= _radialMenu.Top  + _radialMenu.Height;

                if (!insideWindow)
                    _radialMenu.Hide();
            });
        }

        private void SystemHookManager_OnMouseUp(object? sender, SystemHookManager.MousePoint e)
        {
            TriggerSelectionMenu(e.X, e.Y, isKeyboard: false);
        }

        private void SystemHookManager_OnDoubleClickRelease(object? sender, SystemHookManager.MousePoint e)
        {
            // Only show popup for double-click in editable controls (same guard as long-press)
            if (!IsOverEditableControl(e.X, e.Y)) return;
            TriggerSelectionMenu(e.X, e.Y, isKeyboard: false);
        }

        private void SystemHookManager_OnKeyboardSelection(object? sender, SystemHookManager.MousePoint e)
        {
            TriggerSelectionMenu(e.X, e.Y, isKeyboard: true);
        }

        private void SystemHookManager_OnCustomHotkey(object? sender, SystemHookManager.MousePoint e)
        {
            TriggerSelectionMenu(e.X, e.Y, isKeyboard: true);
        }

        private void SystemHookManager_OnLongPress(object? sender, SystemHookManager.MousePoint e)
        {
            if (!IsOverEditableControl(e.X, e.Y)) return;

            Dispatcher.Invoke(() =>
            {
                _radialMenu.ShowAtCursor(e.X, e.Y, string.Empty, isEditable: true);
            });
        }

        private void SystemHookManager_OnEscapePressed(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_radialMenu.IsVisible)
                    _radialMenu.Hide();
            });
        }

        /// <summary>
        /// Common path: wait 50 ms, extract selected text via Ctrl+C, show popup.
        /// <paramref name="isKeyboard"/> = true skips the editable-control check for Paste/Cut
        /// (keyboard selection always implies an editable control was focused).
        /// </summary>
        private void TriggerSelectionMenu(int screenX, int screenY, bool isKeyboard)
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
                bool   isEditable   = isKeyboard || IsOverEditableControl(screenX, screenY);

                if (!string.IsNullOrWhiteSpace(selectedText) && !token.IsCancellationRequested)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!token.IsCancellationRequested)
                            _radialMenu.ShowAtCursor(screenX, screenY, selectedText, isEditable);
                    });
                }
            }, token);
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
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemUserPreferenceChanged;
            SystemHookManager.StopHooks();
            _notifyIcon?.Dispose();
            _selectionCts?.Dispose();

            if (_actionExecutor?.LlmService is IDisposable disposable)
            {
                disposable.Dispose();
            }

            base.OnExit(e);
        }
    }
}
