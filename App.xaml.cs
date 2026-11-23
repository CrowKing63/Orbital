using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.Win32;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Media;
using System.Windows.Interop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WinForms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace Orbital
{
    public partial class App : System.Windows.Application
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private RadialMenuWindow _radialMenu = null!;
        private WinForms.NotifyIcon _notifyIcon = null!;
        private ActionExecutorService? _actionExecutor;
        private CancellationTokenSource? _selectionCts;
        private readonly object _selectionLock = new object();
        private (string Version, string InstallerPath)? _pendingUpdate;
        private DateTime _lastDismissTime = DateTime.MinValue;
        private DateTime _lastTriggerAttemptTime = DateTime.MinValue;

        private enum TriggerCase
        {
            Unsupported,
            InputField,
            OutsideSelection,
        }

        /// <summary>
        /// Target classification for a trigger gesture. Exactly two cases produce a popup.
        /// HasText only controls button visibility for Case B; Case A must show even with
        /// no selection so Paste / Select All remain available.
        /// </summary>
        private readonly struct TriggerDecision
        {
            public TriggerCase Case { get; }
            public bool IsEditable { get; }
            public bool HasText { get; }

            private TriggerDecision(TriggerCase targetCase, bool isEditable, bool hasText)
            {
                Case = targetCase;
                IsEditable = isEditable;
                HasText = hasText;
            }

            public static TriggerDecision Unsupported { get; } =
                new(TriggerCase.Unsupported, false, false);

            public static TriggerDecision InputField(bool hasText) =>
                new(TriggerCase.InputField, true, hasText);

            public static TriggerDecision OutsideSelection(bool hasText) =>
                new(TriggerCase.OutsideSelection, false, hasText);
        }

        private static readonly HashSet<string> _supportedLanguages =
            new() { "en", "ko", "ja", "zh", "es", "fr", "de", "pt", "ru", "it" };

        /// <summary>Win32 class names of browser windows that support text selection.
        /// Used as a fast-path to skip expensive UIA IPC calls.</summary>
        private static readonly HashSet<string> s_browserClasses =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Chrome_WidgetWin_1",
                "Chrome_WidgetWin_2",
                "Chrome_RenderWidgetHostHWND",
                "MozillaWindowClass",
            };

        private static readonly HashSet<string> s_excludedRootWindowClasses =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Shell_TrayWnd",
                "Shell_SecondaryTrayWnd",
                "Progman",
                "WorkerW",
            };

        private static readonly HashSet<string> s_excludedControlClasses =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "SHELLDLL_DefView",
                "SysListView32",
                "DirectUIHWND",
                "NamespaceTreeControl",
            };

        private static readonly HashSet<string> s_clipboardFallbackEditorProcesses =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "notepad++",
                "emeditor",
                "sublime_text",
            };

        private static readonly HashSet<string> s_clipboardFallbackEditorClasses =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Notepad++",
                "EmEditorMainFrame3",
                "EmEditorMainFrame4",
            };

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            bool recovered = SettingsManager.LoadSettings();

            ApplyTheme(SettingsManager.CurrentSettings.Theme);
            ApplySystemAccentResources();
            ApplyLanguage(SettingsManager.CurrentSettings.Language);
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged;

            if (recovered)
            {
                MessageBox.Show(Loc.Get("Str_SettingsRecoveryMsg"),
                    Loc.Get("Str_SettingsRecoveryTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }

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
            SystemHookManager.OnClipboardShortcut   += SystemHookManager_OnClipboardShortcut;
            SystemHookManager.OnCustomHotkey        += SystemHookManager_OnCustomHotkey;

            ApplyHotkeySettings();

            if (!SystemHookManager.StartMouseHook(out int mouseErrCode))
            {
                string errorMessage = string.Format(Loc.Get("Str_HookErrorMsg"), mouseErrCode);
                MessageBox.Show(errorMessage, Loc.Get("Str_HookErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // Keyboard hook is best-effort; failures are non-fatal (mouse-only mode still works)
            SystemHookManager.StartKeyboardHook(out _);
        }

        public static void ApplyTheme(string themeName)
        {
            string resolved = ResolveTheme(themeName);

            var dicts = Current.Resources.MergedDictionaries;
            var existing = dicts.FirstOrDefault(d =>
                d.Source != null &&
                (d.Source.OriginalString.Contains("Dark.xaml") ||
                 d.Source.OriginalString.Contains("Light.xaml")));

            if (existing != null)
                dicts.Remove(existing);

            string path = resolved == "Light" ? "Themes/Light.xaml" : "Themes/Dark.xaml";
            dicts.Insert(0, new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });
            (Current as App)?.UpdateTrayIcon(resolved);

            SettingsManager.CurrentSettings.Theme = themeName; // "System" 그대로 저장
        }

        public static void ApplyLanguage(string languageCode)
        {
            string lang = _supportedLanguages.Contains(languageCode) ? languageCode : "en";

            var dicts = Current.Resources.MergedDictionaries;
            var existing = dicts.FirstOrDefault(d =>
                d.Source != null &&
                d.Source.OriginalString.Contains("Strings/Strings."));

            if (existing != null)
                dicts.Remove(existing);

            string path = $"Strings/Strings.{lang}.xaml";
            dicts.Insert(0, new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });

            SettingsManager.CurrentSettings.Language = lang;

            // Rebuild WinForms tray menu (DynamicResource doesn't apply to WinForms)
            (Current as App)?.RebuildTrayMenu();
        }

        private static string ResolveTheme(string themeName)
            => themeName == "System" ? GetSystemTheme() : themeName;

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

            if (e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General)
            {
                Dispatcher.BeginInvoke(ApplySystemAccentResources);
            }
        }

        private static void ApplySystemAccentResources()
        {
            if (Current == null) return;

            if (!TryGetSystemAccentColor(out var accent))
                return;

            var accentSoft = BlendTowards(accent, Colors.White, 0.35);

            Current.Resources["AccentVi"] = new SolidColorBrush(accent);
            Current.Resources["AccentCy"] = new SolidColorBrush(accentSoft);
            Current.Resources["AccentBrush"] = new SolidColorBrush(accent);
            Current.Resources["AccentViColor"] = accent;
            Current.Resources["AccentCyColor"] = accentSoft;
        }

        private static bool TryGetSystemAccentColor(out MediaColor accent)
        {
            accent = default;

            try
            {
                int hr = DwmGetColorizationColor(out uint rawColor, out _);
                if (hr != 0)
                    return false;

                byte a = (byte)((rawColor >> 24) & 0xFF);
                byte r = (byte)((rawColor >> 16) & 0xFF);
                byte g = (byte)((rawColor >> 8) & 0xFF);
                byte b = (byte)(rawColor & 0xFF);

                if (a == 0) a = 0xFF;
                accent = MediaColor.FromArgb(a, r, g, b);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static MediaColor BlendTowards(MediaColor baseColor, MediaColor target, double ratio)
        {
            ratio = Math.Clamp(ratio, 0d, 1d);
            byte r = (byte)Math.Round(baseColor.R + ((target.R - baseColor.R) * ratio));
            byte g = (byte)Math.Round(baseColor.G + ((target.G - baseColor.G) * ratio));
            byte b = (byte)Math.Round(baseColor.B + ((target.B - baseColor.B) * ratio));
            return MediaColor.FromArgb(0xFF, r, g, b);
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using var http = new HttpClient();
                const string latestUrl = "https://api.github.com/repos/CrowKing63/Orbital/releases/latest";
                string json = await http.GetStringAsync(latestUrl);
                var release = JObject.Parse(json);

                // Only update from stable releases.
                if ((bool?)release["prerelease"] == true) return;

                string? tag = (string?)release["tag_name"];
                if (tag == null) return;
                string version = tag.TrimStart('v');
                string current = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
                if (!UpdateChecker.IsNewerVersion(version, current)) return;

                string? url = UpdateChecker.FindSetupAssetUrl(release, version);
                if (url == null) return;

                // Download the new installer to a temp file.
                string installerPath = Path.Combine(Path.GetTempPath(), $"Orbital-{version}-Setup.exe");
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                using var inStream = await response.Content.ReadAsStreamAsync();
                using (var outStream = File.Create(installerPath))
                {
                    await inStream.CopyToAsync(outStream);
                }

                _pendingUpdate = (version, installerPath);
                Dispatcher.Invoke(() =>
                {
                    RebuildTrayMenu();
                    _notifyIcon.ShowBalloonTip(
                        8000,
                        Loc.Get("Str_UpdateBalloonTitle"),
                        string.Format(Loc.Get("Str_UpdateBalloonMsg"), version),
                        WinForms.ToolTipIcon.Info);
                });
            }
            catch { /* Update check is best-effort — ignore all errors */ }
        }

        /// <summary>
        /// Launches the downloaded installer silently, then shuts the app down.
        /// The installer waits for this process to exit, upgrades in place, and
        /// relaunches the app via a restart flag file (see Orbital.iss).
        /// </summary>
        private void ApplyPendingUpdate()
        {
            if (!_pendingUpdate.HasValue) return;
            var (version, installerPath) = _pendingUpdate.Value;

            try
            {
                // Flag file consumed by the installer's post-install step to relaunch the app.
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "orbital-update-restart.flag"), version);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath)
                {
                    Arguments = "/VERYSILENT /SUPPRESSMSGBOXES",
                    UseShellExecute = false
                });
            }
            catch { /* If the installer fails to start, just exit normally. */ }

            Dispatcher.Invoke(Shutdown);
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
                MessageBox.Show(string.Format(Loc.Get("Str_InvalidApiConfigMsg"), ex.Message),
                    Loc.Get("Str_InvalidApiConfigTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                Visible = true
            };

            UpdateTrayIcon(ResolveTheme(SettingsManager.CurrentSettings.Theme));

            _notifyIcon.ContextMenuStrip = new WinForms.ContextMenuStrip();
            _notifyIcon.DoubleClick += (s, e) => Dispatcher.Invoke(OpenSettingsWindow);

            RebuildTrayMenu();
        }

        private void UpdateTrayIcon(string resolvedTheme)
        {
            if (_notifyIcon == null) return;

            string themedPath = resolvedTheme == "Light"
                ? "pack://application:,,,/Assets/orbit_logo_light.ico"
                : "pack://application:,,,/Assets/orbit_logo_dark.ico";

            if (TryLoadTrayIcon(themedPath, out var themedIcon))
            {
                _notifyIcon.Icon = themedIcon;
                return;
            }

            if (TryLoadTrayIcon("pack://application:,,,/Assets/orbit_logo.ico", out var fallbackIcon))
            {
                _notifyIcon.Icon = fallbackIcon;
                return;
            }

            _notifyIcon.Icon = SystemIcons.Application;
        }

        private static bool TryLoadTrayIcon(string resourceUri, out Icon icon)
        {
            icon = null!;

            try
            {
                var uri = new Uri(resourceUri);
                var streamInfo = System.Windows.Application.GetResourceStream(uri);
                if (streamInfo == null)
                    return false;

                using var stream = streamInfo.Stream;
                icon = new Icon(stream);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Rebuilds the WinForms tray context menu with the currently active language strings.
        /// Called on startup and whenever the language is changed.
        /// </summary>
        internal void RebuildTrayMenu()
        {
            if (_notifyIcon == null) return;

            _notifyIcon.Text = Loc.Get("Str_TrayTooltip");

            var menu = _notifyIcon.ContextMenuStrip;
            if (menu == null) return;

            menu.Items.Clear();
            menu.Items.Add(Loc.Get("Str_TraySettings"), null,
                (s, e) => Dispatcher.Invoke(OpenSettingsWindow));
            menu.Items.Add(new WinForms.ToolStripSeparator());

            string restartLabel = _pendingUpdate.HasValue
                ? $"{Loc.Get("Str_TrayRestart")} (v{_pendingUpdate.Value.Version})"
                : Loc.Get("Str_TrayRestart");
            menu.Items.Add(restartLabel, null, (s, e) =>
            {
                if (_pendingUpdate.HasValue)
                    ApplyPendingUpdate();
                else
                {
                    var exe = Environment.ProcessPath;
                    if (exe != null) System.Diagnostics.Process.Start(exe);
                    Dispatcher.Invoke(Shutdown);
                }
            });

            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(Loc.Get("Str_TrayExit"), null, (s, e) =>
            {
                _notifyIcon.Visible = false;
                Dispatcher.Invoke(Shutdown);
            });
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
                {
                    _lastDismissTime = DateTime.UtcNow;
                    _radialMenu.Hide();
                }
            });
        }

        private void SystemHookManager_OnMouseUp(object? sender, SystemHookManager.MousePoint e)
        {
            if (!SettingsManager.CurrentSettings.EnableDragTrigger) return;

            // Capture drag-start position now; _buttonDownPos may be overwritten by the next click.
            var down = SystemHookManager.LastButtonDownPos;
            var downClass = SystemHookManager.LastButtonDownHwndClass;
            TriggerSelectionMenu(e.X, e.Y, isKeyboard: false, editCheckX: down.X, editCheckY: down.Y, dragStartHwndClass: downClass);
        }

        private void SystemHookManager_OnDoubleClickRelease(object? sender, SystemHookManager.MousePoint e)
        {
            if (!SettingsManager.CurrentSettings.EnableDoubleClickTrigger) return;

            // Pass the button-down window class so browser windows hit the fast path in
            // ClassifyTrigger: Chrome's UIA tree resolves to ControlType.Window at word
            // positions, which would otherwise be rejected as unsupported.
            // A double-click is an intentional new-selection gesture: its first click
            // doubles as the dismiss of any visible popup, so that dismiss must not
            // suppress this trigger via the 150 ms window.
            var downClass = SystemHookManager.LastButtonDownHwndClass;
            TriggerSelectionMenu(e.X, e.Y, isKeyboard: false, requireEditable: true,
                dragStartHwndClass: downClass, ignoreDismissSuppression: true);
        }

        private void SystemHookManager_OnKeyboardSelection(object? sender, SystemHookManager.MousePoint e)
        {
            if (!SettingsManager.CurrentSettings.EnableKeyboardSelectionTrigger) return;

            TriggerSelectionMenu(e.X, e.Y, isKeyboard: true);
        }

        private void SystemHookManager_OnCustomHotkey(object? sender, SystemHookManager.MousePoint e)
        {
            TriggerSelectionMenu(e.X, e.Y, isKeyboard: true);
        }

        private void SystemHookManager_OnLongPress(object? sender, SystemHookManager.MousePoint e)
        {
            if (!SettingsManager.CurrentSettings.EnableLongPressTrigger) return;

            // Run IsOverEditableControl on a background thread to avoid blocking the hook callback.
            // Then go through TriggerSelectionMenu so CTS cancellation prevents conflicts with
            // subsequent drag / double-click / keyboard triggers.
            System.Threading.Tasks.Task.Run(() =>
            {
                if (!IsOverEditableControl(e.X, e.Y)) return;
                TriggerSelectionMenu(e.X, e.Y, isKeyboard: true, forceHasText: false);
            });
        }

        private void SystemHookManager_OnEscapePressed(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_radialMenu.IsVisible)
                {
                    _lastDismissTime = DateTime.UtcNow;
                    _radialMenu.Hide();
                }
            });
        }

        private void SystemHookManager_OnClipboardShortcut(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_radialMenu.IsVisible)
                {
                    _lastDismissTime = DateTime.UtcNow;
                    _radialMenu.Hide();
                }
            });
        }

        /// <summary>
        /// Shows the popup immediately (no clipboard/UI Automation calls before display).
        /// Selected text is read lazily when the user clicks an action button.
        /// <paramref name="isKeyboard"/> = true means a keyboard selection triggered this
        /// (always editable, so Paste/Cut are shown).
        /// <paramref name="requireEditable"/> = true (double-click) checks editable on a
        /// background thread and skips the popup if the target is read-only.
        /// <paramref name="forceHasText"/> overrides hasText when non-null (used by long-press).
        /// </summary>
        /// <param name="editCheckX">X coordinate to use for the editability check.
        /// For drag selection, pass the drag-start position so the check succeeds even when
        /// the cursor drifts outside the text field before mouse-up.</param>
        private void TriggerSelectionMenu(int screenX, int screenY, bool isKeyboard,
            bool requireEditable = false, int editCheckX = -1, int editCheckY = -1,
            string dragStartHwndClass = "", bool? forceHasText = null,
            bool ignoreDismissSuppression = false)
        {
            // Throttle: if another trigger arrived within 80 ms, just cancel the in-flight
            // task instead of starting a new UIA round-trip that would pile up and lag the mouse.
            if ((DateTime.UtcNow - _lastTriggerAttemptTime).TotalMilliseconds < 80)
            {
                lock (_selectionLock) { _selectionCts?.Cancel(); }
                return;
            }
            _lastTriggerAttemptTime = DateTime.UtcNow;

            // Suppress rapid re-triggers after dismiss (e.g. accidental double-trigger).
            // Skipped for double-click: its first click doubles as the dismiss of any visible
            // popup, so that dismiss must not suppress the new selection itself.
            if (!ignoreDismissSuppression &&
                (DateTime.UtcNow - _lastDismissTime).TotalMilliseconds < 150)
                return;

            // Fall back to the menu-display position when no explicit check position is given.
            if (editCheckX < 0) editCheckX = screenX;
            if (editCheckY < 0) editCheckY = screenY;

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

                TriggerDecision decision;

                if (forceHasText.HasValue)
                {
                    // Long-press already verified an editable control on the hook path.
                    decision = TriggerDecision.InputField(forceHasText.Value);
                }
                else
                {
                    // Let word selection settle (longer for double-click) before classifying.
                    int delay = requireEditable ? 80 : 25;
                    if (delay > 0) Thread.Sleep(delay);
                    if (token.IsCancellationRequested) return;

                    decision = ClassifyTrigger(editCheckX, editCheckY, dragStartHwndClass,
                        isKeyboard: isKeyboard, selectionIntent: true);
                }

                if (decision.Case == TriggerCase.Unsupported) return;

                Dispatcher.BeginInvoke(() =>
                {
                    if (!token.IsCancellationRequested)
                        _radialMenu.ShowAtCursor(screenX, screenY,
                            decision.IsEditable, decision.HasText);
                });
            }, token);
        }

        /// <summary>
        /// Classifies the target of a trigger gesture into one of two supported cases:
        /// an editable text input, or a text selection outside an input field.
        /// Anything else returns Unsupported and produces no popup.
        /// Decision order: focused element, point, browser class, fallback classes.
        /// The browser class check must come after focus/point so that an empty input
        /// field in a browser is still classified as a text input (Case A).
        /// </summary>
        private static TriggerDecision ClassifyTrigger(
            int screenX, int screenY, string hwndClass = "",
            bool isKeyboard = false, bool selectionIntent = true)
        {
            bool isBrowser = !string.IsNullOrEmpty(hwndClass) && s_browserClasses.Contains(hwndClass);

            try
            {
                IntPtr hwnd = WindowFromPoint(new POINT { X = screenX, Y = screenY });
                string rootClass = GetRootWindowClass(hwnd);
                string controlClass = GetWindowClass(hwnd);
                string processName = GetProcessNameFromHwnd(hwnd);

                if (s_excludedRootWindowClasses.Contains(rootClass) ||
                    s_excludedControlClasses.Contains(controlClass))
                    return TriggerDecision.Unsupported;

                // Focused element first. This is the reliable signal in Chromium and modern
                // WinUI apps where FromPoint only sees the top-level Window. Keyboard gestures
                // skip the rectangle check because no click is involved.
                var focusedDecision = ClassifyFocusedElement(screenX, screenY, isKeyboard, selectionIntent);
                if (focusedDecision.HasValue) return focusedDecision.Value;

                var point = new System.Windows.Point(screenX, screenY);

                // CacheRequest batches property/pattern retrieval into one IPC round-trip.
                var cacheRequest = new CacheRequest();
                cacheRequest.Add(AutomationElement.ControlTypeProperty);
                cacheRequest.Add(AutomationElement.IsKeyboardFocusableProperty);
                cacheRequest.Add(TextPattern.Pattern);
                cacheRequest.Add(ValuePattern.Pattern);

                using (cacheRequest.Activate())
                {
                    var element = AutomationElement.FromPoint(point);
                    if (element == null)
                        return isBrowser
                            ? OutsideSelectionOrUnsupported(selectionIntent)
                            : TriggerDecision.Unsupported;

                    var controlType = element.GetCachedPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;

                    if (controlType == ControlType.Edit || controlType == ControlType.ComboBox)
                        return TriggerDecision.InputField(HasRealTextSelection(element));

                    if (controlType == ControlType.Document)
                    {
                        var vp = TryGetCachedPattern<ValuePattern>(element, ValuePattern.Pattern);
                        bool writable = vp != null
                            ? !vp.Current.IsReadOnly
                            : (bool)element.GetCachedPropertyValue(AutomationElement.IsKeyboardFocusableProperty);
                        return writable
                            ? TriggerDecision.InputField(HasRealTextSelection(element))
                            : OutsideSelectionOrUnsupported(selectionIntent);
                    }

                    // WinUI 3 / UWP text areas report as Pane but expose TextPattern.
                    if (controlType == ControlType.Pane &&
                        TryGetCachedPattern<TextPattern>(element, TextPattern.Pattern) != null)
                        return TriggerDecision.InputField(HasRealTextSelection(element));

                    // One-level parent fallback only — deeper walking causes false positives.
                    var parent = TreeWalker.ControlViewWalker.GetParent(element);
                    if (parent != null)
                    {
                        var parentType = parent.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;
                        if (parentType == ControlType.Edit)
                            return TriggerDecision.InputField(HasRealTextSelection(parent));
                        if (parentType == ControlType.Document)
                        {
                            bool writable;
                            if (parent.TryGetCurrentPattern(ValuePattern.Pattern, out var vpo2) && vpo2 is ValuePattern vp2)
                                writable = !vp2.Current.IsReadOnly;
                            else
                                writable = (bool)parent.GetCurrentPropertyValue(AutomationElement.IsKeyboardFocusableProperty);
                            return writable
                                ? TriggerDecision.InputField(HasRealTextSelection(parent))
                                : OutsideSelectionOrUnsupported(selectionIntent);
                        }
                        if (controlType == ControlType.Text && parentType == ControlType.Group)
                            return OutsideSelectionOrUnsupported(selectionIntent);
                    }
                }

                // Browser: FromPoint often resolves to the top-level Window and focus may be
                // on browser chrome. Only now fall back to class-based Case B, after the
                // focused element and point checks have failed to find an input field.
                if (isBrowser)
                    return OutsideSelectionOrUnsupported(selectionIntent);

                if (IsClipboardFallbackEditor(controlClass, rootClass, processName))
                    return TriggerDecision.InputField(true);

                return TriggerDecision.Unsupported;
            }
            catch { return TriggerDecision.Unsupported; }
        }

        /// <summary>
        /// Case B is valid only when a real selection exists. Returns true when confirmed,
        /// false when proven absent, and null when verification was unavailable. The caller
        /// suppresses the popup only on false.
        /// </summary>
        private static bool? VerifyOutsideSelection(bool selectionIntent)
        {
            if (!selectionIntent) return false;

            bool? viaUia = HasRealFocusedSelection();
            if (viaUia == true) return true;

            try
            {
                string text = ClipboardHelper.GetSelectedText();
                return !string.IsNullOrWhiteSpace(text);
            }
            catch
            {
                return viaUia; // null = unknown; keep the popup rather than lose a selection
            }
        }

        private static TriggerDecision OutsideSelectionOrUnsupported(bool selectionIntent)
        {
            bool? verified = VerifyOutsideSelection(selectionIntent);
            if (verified == false) return TriggerDecision.Unsupported;
            return TriggerDecision.OutsideSelection(verified ?? true);
        }

        /// <summary>
        /// Focus-based classification. A mouse gesture must land inside the focused
        /// control, otherwise a click on a title bar or button would reuse the previously
        /// focused text control and produce a false Case A.
        /// </summary>
        private static TriggerDecision? ClassifyFocusedElement(
            int screenX, int screenY, bool isKeyboard, bool selectionIntent)
        {
            try
            {
                var focused = AutomationElement.FocusedElement;
                if (focused == null) return null;

                var ct = focused.Current.ControlType;
                if (ct != ControlType.Edit && ct != ControlType.Document &&
                    ct != ControlType.ComboBox && ct != ControlType.Text &&
                    ct != ControlType.Pane)
                    return null;

                if (!isKeyboard)
                {
                    var rect = focused.Current.BoundingRectangle;
                    if (rect.Width <= 0 || rect.Height <= 0) return null;
                    if (screenX < rect.X || screenX > rect.X + rect.Width ||
                        screenY < rect.Y || screenY > rect.Y + rect.Height)
                        return null;
                }

                bool writable;
                if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vpo) && vpo is ValuePattern vp)
                    writable = !vp.Current.IsReadOnly;
                else
                    writable = ct == ControlType.Edit || ct == ControlType.ComboBox ||
                        (ct == ControlType.Document &&
                         (bool)focused.GetCurrentPropertyValue(AutomationElement.IsKeyboardFocusableProperty)) ||
                        (ct == ControlType.Pane &&
                         focused.TryGetCurrentPattern(TextPattern.Pattern, out _) &&
                         (bool)focused.GetCurrentPropertyValue(AutomationElement.IsKeyboardFocusableProperty));

                if (writable)
                {
                    bool hasText = HasRealFocusedSelection() ?? HasRealTextSelection(focused);
                    return TriggerDecision.InputField(hasText);
                }

                return OutsideSelectionOrUnsupported(selectionIntent);
            }
            catch { return null; }
        }

        internal static bool IsClipboardFallbackEditor(string controlClass, string rootClass, string processName)
        {
            return s_clipboardFallbackEditorClasses.Contains(controlClass) ||
                s_clipboardFallbackEditorClasses.Contains(rootClass) ||
                (!string.IsNullOrEmpty(processName) && s_clipboardFallbackEditorProcesses.Contains(processName));
        }

        private static string GetWindowClass(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            var sb = new System.Text.StringBuilder(128);
            int len = GetClassName(hwnd, sb, sb.Capacity);
            return len > 0 ? sb.ToString() : string.Empty;
        }

        private static string GetRootWindowClass(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            IntPtr root = GetAncestor(hwnd, GA_ROOT);
            if (root == IntPtr.Zero) root = hwnd;
            return GetWindowClass(root);
        }

        private static string GetProcessNameFromHwnd(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return string.Empty;
            try
            {
                return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Safely retrieves a cached pattern from an element. Returns null if the pattern
        /// was not cached (i.e. the element does not support it).
        /// </summary>
        private static T? TryGetCachedPattern<T>(AutomationElement element, AutomationPattern pattern) where T : class
        {
            try
            {
                return element.GetCachedPattern(pattern) as T;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns true when the element's TextPattern reports a non-collapsed (non-empty) selection.
        /// Prefers a cached pattern when available (inside an active CacheRequest scope).
        /// </summary>
        private static bool HasRealTextSelection(AutomationElement element)
        {
            TextPattern? tp = TryGetCachedPattern<TextPattern>(element, TextPattern.Pattern);

            if (tp == null)
            {
                if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var tpo) || tpo is not TextPattern tpLive)
                    return false;
                tp = tpLive;
            }

            var sel = tp.GetSelection();
            if (sel.Length == 0) return false;
            // CompareEndpoints == 0 means start == end (collapsed cursor, no visible selection).
            return sel[0].CompareEndpoints(TextPatternRangeEndpoint.Start, sel[0], TextPatternRangeEndpoint.End) != 0;
        }

        /// <summary>
        /// Returns the real selection state of the currently focused element:
        /// true = a non-whitespace text selection exists, false = definitively none,
        /// null = cannot be determined (no TextPattern available). Used by ClassifyTrigger
        /// for input fields and by VerifyOutsideSelection to confirm Case B.
        /// </summary>
        private static bool? HasRealFocusedSelection()
        {
            try
            {
                var focused = AutomationElement.FocusedElement;
                if (focused == null) return null;

                TextPattern? tp = null;
                if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var tpo) && tpo is TextPattern live)
                    tp = live;
                if (tp == null)
                {
                    // Some apps expose the selection only on a Text descendant of the focused element.
                    try
                    {
                        var txt = focused.FindFirst(TreeScope.Subtree,
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                        if (txt != null && txt.TryGetCurrentPattern(TextPattern.Pattern, out var dpo) && dpo is TextPattern dto)
                            tp = dto;
                    }
                    catch { }
                }
                if (tp == null) return null;

                var sel = tp.GetSelection();
                if (sel.Length == 0) return false;
                // CompareEndpoints == 0 means start == end (collapsed cursor, no visible selection).
                if (sel[0].CompareEndpoints(TextPatternRangeEndpoint.Start, sel[0], TextPatternRangeEndpoint.End) == 0)
                    return false;
                // A whitespace-only selection (e.g. a lone newline from double-clicking an empty
                // line in Notepad) does not count as real text.
                string text = sel[0].GetText(200);
                return text.Trim().Length > 0;
            }
            catch { return null; }
        }

        /// <summary>
        /// Returns true when the cursor is over an editable text control — used by the long-press path.
        /// Unlike ClassifyTrigger, this does NOT require an active text selection.
        /// Handles ControlType.Edit (classic Win32/WPF) and ControlType.Document with a writable
        /// ValuePattern (Windows 11 Notepad, contenteditable, Word Online, etc.).
        /// </summary>
        private static bool IsOverEditableControl(int screenX, int screenY)
        {
            try
            {
                var point = new System.Windows.Point(screenX, screenY);

                var cacheRequest = new CacheRequest();
                cacheRequest.Add(AutomationElement.ControlTypeProperty);
                cacheRequest.Add(AutomationElement.IsKeyboardFocusableProperty);
                cacheRequest.Add(ValuePattern.Pattern);

                using (cacheRequest.Activate())
                {
                    var element = AutomationElement.FromPoint(point);
                    if (element == null) return false;

                    var controlType = element.GetCachedPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;

                    if (controlType == ControlType.Edit || controlType == ControlType.ComboBox)
                        return true;

                    // Document is editable when it exposes a writable ValuePattern, or when it is
                    // keyboard-focusable (UWP RichEditBox / Sticky Notes lack ValuePattern but do
                    // accept keyboard input). Browser read-only pages are not keyboard-focusable
                    // at the document level.
                    if (controlType == ControlType.Document)
                    {
                        var vp = TryGetCachedPattern<ValuePattern>(element, ValuePattern.Pattern);
                        if (vp != null)
                            return !vp.Current.IsReadOnly;
                        return (bool)element.GetCachedPropertyValue(AutomationElement.IsKeyboardFocusableProperty);
                    }
                }

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
