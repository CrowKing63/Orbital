using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.Win32;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Media;
using System.Windows.Interop;
using Velopack;
using Velopack.Sources;
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
        private UpdateInfo? _pendingUpdate;
        private UpdateManager? _updateManager;
        private DateTime _lastDismissTime = DateTime.MinValue;
        private DateTime _lastTriggerAttemptTime = DateTime.MinValue;

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
                var mgr = new UpdateManager(
                    new GithubSource("https://github.com/CrowKing63/Orbital", null, false));
                var updateInfo = await mgr.CheckForUpdatesAsync();
                if (updateInfo != null)
                {
                    await mgr.DownloadUpdatesAsync(updateInfo);
                    _updateManager = mgr;
                    _pendingUpdate = updateInfo;
                    mgr.WaitExitThenApplyUpdates(updateInfo, silent: true, restart: true);
                    Dispatcher.Invoke(() =>
                    {
                        RebuildTrayMenu();
                        _notifyIcon.ShowBalloonTip(
                            8000,
                            Loc.Get("Str_UpdateBalloonTitle"),
                            string.Format(Loc.Get("Str_UpdateBalloonMsg"), updateInfo.TargetFullRelease.Version),
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

            _notifyIcon.ContextMenuStrip = new WinForms.ContextMenuStrip();
            _notifyIcon.DoubleClick += (s, e) => Dispatcher.Invoke(OpenSettingsWindow);

            RebuildTrayMenu();
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

            string restartLabel = _pendingUpdate != null
                ? $"{Loc.Get("Str_TrayRestart")} (v{_pendingUpdate.TargetFullRelease.Version})"
                : Loc.Get("Str_TrayRestart");
            menu.Items.Add(restartLabel, null, (s, e) =>
            {
                if (_pendingUpdate != null)
                    _updateManager?.ApplyUpdatesAndRestart(_pendingUpdate);
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

            // Editable-control check is deferred to the background thread inside TriggerSelectionMenu
            TriggerSelectionMenu(e.X, e.Y, isKeyboard: false, requireEditable: true);
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
            string dragStartHwndClass = "", bool? forceHasText = null)
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
            if ((DateTime.UtcNow - _lastDismissTime).TotalMilliseconds < 150)
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

                bool isEditable = false;
                bool hasText = false;

                if (isKeyboard && forceHasText.HasValue)
                {
                    // Long-press path: already verified editable, no text selection.
                    isEditable = true;
                    hasText = forceHasText.Value;
                }
                else if (isKeyboard)
                {
                    isEditable = true;
                    hasText = true; // keyboard selection always produces a text selection
                }
                else
                {
                    // Single attempt with a short adaptive delay.
                    // Browsers hit the fast-path in CheckEditability (zero UIA IPC).
                    // Native apps (Notepad, Word) are fast with CacheRequest.
                    // Double-click needs a slightly longer wait for word selection to settle.
                    int delay = requireEditable ? 80 : 25;
                    if (delay > 0) Thread.Sleep(delay);
                    if (token.IsCancellationRequested) return;

                    (bool canSelect, bool canWrite, bool selHasText) =
                        CheckEditability(editCheckX, editCheckY, dragStartHwndClass);
                    if (!canSelect) return;
                    isEditable = canWrite;
                    hasText = selHasText;
                }

                Dispatcher.BeginInvoke(() =>
                {
                    if (!token.IsCancellationRequested)
                        _radialMenu.ShowAtCursor(screenX, screenY, isEditable, hasText);
                });
            }, token);
        }

        /// <summary>
        /// Returns (canSelect, canWrite) for the UI element at the given screen point.
        /// canSelect — text is selected and can be acted on.
        ///             For Edit controls this is always true (Paste is useful even without selection).
        /// canWrite  — element accepts text input; controls whether Cut/Paste actions are shown.
        ///
        /// Only ControlType.Edit, Document, and Text-inside-Document are considered.
        /// Everything else (game windows, taskbar, desktop, etc.) returns (false, false).
        /// </summary>
        /// <summary>
        /// Returns (canSelect, canWrite, hasText) for the UI element at the given screen point.
        /// canSelect — popup may be shown at all.
        /// canWrite  — element accepts text input (Cut / Paste / Replace work).
        /// hasText   — a non-empty text selection currently exists (LLM / selection-based actions work).
        /// </summary>
        private static (bool canSelect, bool canWrite, bool hasText) CheckEditability(
            int screenX, int screenY, string hwndClass = "")
        {
            // Fast path: skip expensive UIA IPC for known browser windows.
            // Browsers are always treated as read-only Document with optimistic hasText.
            if (!string.IsNullOrEmpty(hwndClass) && s_browserClasses.Contains(hwndClass))
                return (true, false, true);

            try
            {
                IntPtr hwnd = WindowFromPoint(new POINT { X = screenX, Y = screenY });
                string rootClass = GetRootWindowClass(hwnd);
                string controlClass = GetWindowClass(hwnd);
                string processName = GetProcessNameFromHwnd(hwnd);

                if (s_excludedRootWindowClasses.Contains(rootClass) || s_excludedControlClasses.Contains(controlClass))
                    return (false, false, false);

                var point = new System.Windows.Point(screenX, screenY);

                // Use CacheRequest to batch property/pattern retrieval in a single cross-process
                // round-trip. This cuts IPC from ~4 separate calls down to 1, which is the main
                // cause of mouse stutter when dragging over Chromium-based apps.
                var cacheRequest = new CacheRequest();
                cacheRequest.Add(AutomationElement.ControlTypeProperty);
                cacheRequest.Add(AutomationElement.IsKeyboardFocusableProperty);
                cacheRequest.Add(TextPattern.Pattern);
                cacheRequest.Add(ValuePattern.Pattern);

                using (cacheRequest.Activate())
                {
                    var element = AutomationElement.FromPoint(point);
                    if (element == null) return (false, false, false);

                    var controlType = element.GetCachedPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;

                    // Writable text input — always show popup (Paste is useful even without selection).
                    if (controlType == ControlType.Edit || controlType == ControlType.ComboBox)
                    {
                        bool sel = HasRealTextSelection(element);
                        return (true, true, sel);
                    }

                    if (controlType == ControlType.Document)
                    {
                        bool isWritable;
                        var vp = TryGetCachedPattern<ValuePattern>(element, ValuePattern.Pattern);
                        if (vp != null)
                            isWritable = !vp.Current.IsReadOnly;
                        else
                            isWritable = (bool)element.GetCachedPropertyValue(AutomationElement.IsKeyboardFocusableProperty);

                        bool sel = HasRealTextSelection(element);
                        return isWritable ? (true, true, sel) : (true, false, true);
                    }

                    // WinUI 3 / UWP text areas (e.g. new Sticky Notes) report as Pane
                    // but expose TextPattern.
                    if (controlType == ControlType.Pane)
                    {
                        if (TryGetCachedPattern<TextPattern>(element, TextPattern.Pattern) != null)
                        {
                            bool sel = HasRealTextSelection(element);
                            return (true, true, sel);
                        }
                    }

                    // Parent fallback — parent wasn't in the cache, so live calls are required.
                    {
                        var parent = TreeWalker.ControlViewWalker.GetParent(element);
                        if (parent != null)
                        {
                            var parentType = parent.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;
                            if (parentType == ControlType.Edit)
                            {
                                bool sel = HasRealTextSelection(parent);
                                return (true, true, sel);
                            }
                            if (parentType == ControlType.Document)
                            {
                                bool isWritable;
                                if (parent.TryGetCurrentPattern(ValuePattern.Pattern, out var vpo2) && vpo2 is ValuePattern vp2)
                                    isWritable = !vp2.Current.IsReadOnly;
                                else
                                    isWritable = (bool)parent.GetCurrentPropertyValue(AutomationElement.IsKeyboardFocusableProperty);
                                bool sel = HasRealTextSelection(parent);
                                return isWritable ? (true, true, sel) : (true, false, true);
                            }
                            if (controlType == ControlType.Text && parentType == ControlType.Group)
                                return (true, false, true);
                        }
                    }
                }

                bool allowClipboardFallback =
                    s_clipboardFallbackEditorClasses.Contains(controlClass) ||
                    s_clipboardFallbackEditorClasses.Contains(rootClass) ||
                    (!string.IsNullOrEmpty(processName) && s_clipboardFallbackEditorProcesses.Contains(processName));
                if (allowClipboardFallback)
                {
                    string selectedText = ClipboardHelper.GetSelectedText();
                    if (!string.IsNullOrWhiteSpace(selectedText))
                        return (true, true, true);
                }

                return (false, false, false);
            }
            catch { return (false, false, false); }
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
        /// Returns true when the cursor is over an editable text control — used by the long-press path.
        /// Unlike CheckEditability, this does NOT require an active text selection.
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
