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
        private UpdateInfo? _pendingUpdate;
        private UpdateManager? _updateManager;
        private DateTime _lastDismissTime = DateTime.MinValue;

        private static readonly HashSet<string> _supportedLanguages =
            new() { "en", "ko", "ja", "zh", "es", "fr", "de", "pt", "ru", "it" };

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            bool recovered = SettingsManager.LoadSettings();

            ApplyTheme(SettingsManager.CurrentSettings.Theme);
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
            // Capture drag-start position now; _buttonDownPos may be overwritten by the next click.
            var down = SystemHookManager.LastButtonDownPos;
            var downClass = SystemHookManager.LastButtonDownHwndClass;
            TriggerSelectionMenu(e.X, e.Y, isKeyboard: false, editCheckX: down.X, editCheckY: down.Y, dragStartHwndClass: downClass);
        }

        private void SystemHookManager_OnDoubleClickRelease(object? sender, SystemHookManager.MousePoint e)
        {
            // Editable-control check is deferred to the background thread inside TriggerSelectionMenu
            TriggerSelectionMenu(e.X, e.Y, isKeyboard: false, requireEditable: true);
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
                    // Adaptive UIA polling: retry up to 3 times with progressive delays.
                    // Fast apps (Notepad) succeed on first try; slow apps (browsers, Electron)
                    // succeed on 2nd or 3rd try. Total worst-case delay = 150ms (same as before).
                    int[] delays = requireEditable ? new[] { 0, 80, 70 } : new[] { 0, 50, 50 };
                    bool found = false;
                    foreach (int delay in delays)
                    {
                        if (delay > 0) Thread.Sleep(delay);
                        if (token.IsCancellationRequested) return;

                        (bool canSelect, bool canWrite, bool selHasText) = CheckEditability(editCheckX, editCheckY, dragStartHwndClass);
                        if (canSelect)
                        {
                            isEditable = canWrite;
                            hasText = selHasText;
                            found = true;
                            break;
                        }
                    }
                    if (!found) return;
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
            try
            {
                var element = AutomationElement.FromPoint(new System.Windows.Point(screenX, screenY));
                if (element == null) return (false, false, false);

                var controlType = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;

                // Writable text input — always show popup (Paste is useful even without a selection).
                // hasText reflects whether the user actually selected something right now.
                // ComboBox (e.g. Google search bar, address bars) has an embedded editable field.
                if (controlType == ControlType.Edit || controlType == ControlType.ComboBox)
                {
                    bool sel = HasRealTextSelection(element);
                    return (true, true, sel);
                }

                // Document — writable documents (Notepad, Sticky Notes, Word) are treated like Edit:
                // popup always shows and Paste/Cut are available. Read-only documents (browser pages,
                // PDFs) only show the popup when text is actually selected.
                // Writability is detected via ValuePattern.IsReadOnly when available; otherwise
                // IsKeyboardFocusable is used as a heuristic (editable containers accept keyboard input,
                // while most read-only document containers do not).
                if (controlType == ControlType.Document)
                {
                    bool isWritable;
                    if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var vpo) && vpo is ValuePattern vp)
                        isWritable = !vp.Current.IsReadOnly;
                    else
                        isWritable = (bool)element.GetCurrentPropertyValue(AutomationElement.IsKeyboardFocusableProperty);

                    bool sel = HasRealTextSelection(element);
                    // Writable documents: sel determines hasText (e.g. Notepad with no selection → Paste only).
                    // Read-only documents: always set hasText=true — the user dragged to select, so text
                    // is almost certainly present; the actual content is fetched via clipboard at action time.
                    return isWritable ? (true, true, sel) : (true, false, true);
                }

                // WinUI 3 / UWP text areas (e.g. new Sticky Notes) report as ControlType.Pane
                // but expose TextPattern, making them detectable as text input areas.
                if (controlType == ControlType.Pane)
                {
                    bool hasText = element.TryGetCurrentPattern(TextPattern.Pattern, out _);
                    if (hasText)
                    {
                        bool sel = HasRealTextSelection(element);
                        return (true, true, sel);
                    }
                }

                // For unrecognised leaf types (ControlType.Text, ControlType.Custom, etc.)
                // walk up one level to find an Edit or Document ancestor.
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
                        // Browser body text: leaf=ControlType.Text inside a ControlType.Group.
                        // Chrome renders page text this way; video/image elements do not use Text leaves.
                        if (controlType == ControlType.Text && parentType == ControlType.Group)
                            return (true, false, true);
                    }
                }

                // All other types (game windows, taskbar buttons, desktop, UWP tiles, etc.) — no popup.
                return (false, false, false);
            }
            catch { return (false, false, false); }
        }

        /// <summary>
        /// Returns true when the element's TextPattern reports a non-collapsed (non-empty) selection.
        /// </summary>
        private static bool HasRealTextSelection(AutomationElement element)
        {
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var tpo) || tpo is not TextPattern tp)
                return false;
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
                var element = AutomationElement.FromPoint(new System.Windows.Point(screenX, screenY));
                if (element == null) return false;

                var controlType = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;

                if (controlType == ControlType.Edit || controlType == ControlType.ComboBox)
                    return true;

                // Document is editable when it exposes a writable ValuePattern, or when it is
                // keyboard-focusable (UWP RichEditBox / Sticky Notes lack ValuePattern but do
                // accept keyboard input). Browser read-only pages are not keyboard-focusable
                // at the document level.
                if (controlType == ControlType.Document)
                {
                    if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var vpo) && vpo is ValuePattern vp)
                        return !vp.Current.IsReadOnly;
                    return (bool)element.GetCurrentPropertyValue(AutomationElement.IsKeyboardFocusableProperty);
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
