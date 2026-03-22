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
                    _radialMenu.Hide();
            });
        }

        private void SystemHookManager_OnMouseUp(object? sender, SystemHookManager.MousePoint e)
        {
            TriggerSelectionMenu(e.X, e.Y, isKeyboard: false);
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
            // Run IsOverEditableControl on a background thread to avoid blocking the hook callback
            System.Threading.Tasks.Task.Run(() =>
            {
                if (!IsOverEditableControl(e.X, e.Y)) return;
                Dispatcher.Invoke(() =>
                {
                    _radialMenu.ShowAtCursor(e.X, e.Y, isEditable: true, hasText: false);
                });
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

        private void SystemHookManager_OnClipboardShortcut(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_radialMenu.IsVisible)
                    _radialMenu.Hide();
            });
        }

        /// <summary>
        /// Shows the popup immediately (no clipboard/UI Automation calls before display).
        /// Selected text is read lazily when the user clicks an action button.
        /// <paramref name="isKeyboard"/> = true means a keyboard selection triggered this
        /// (always editable, so Paste/Cut are shown).
        /// <paramref name="requireEditable"/> = true (double-click) checks editable on a
        /// background thread and skips the popup if the target is read-only.
        /// </summary>
        private void TriggerSelectionMenu(int screenX, int screenY, bool isKeyboard, bool requireEditable = false)
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

                bool isEditable;
                bool hasText;
                if (isKeyboard)
                {
                    isEditable = true;
                    hasText = true; // keyboard selection always produces a text selection
                }
                else
                {
                    // Double-click word selection needs a brief moment to commit before we query it.
                    if (requireEditable)
                        Thread.Sleep(80);

                    if (token.IsCancellationRequested) return;

                    (bool canSelect, bool canWrite, bool selHasText) = CheckEditability(screenX, screenY);
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
        private static (bool canSelect, bool canWrite, bool hasText) CheckEditability(int screenX, int screenY)
        {
            try
            {
                var element = AutomationElement.FromPoint(new System.Windows.Point(screenX, screenY));
                if (element == null) return (false, false, false);

                var controlType = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;

                // Writable text input — always show popup (Paste is useful even without a selection).
                // hasText reflects whether the user actually selected something right now.
                if (controlType == ControlType.Edit)
                {
                    bool sel = HasRealTextSelection(element);
                    return (true, true, sel);
                }

                // Document (browser page, PDF, Word, etc.) — show only when text is actually selected.
                if (controlType == ControlType.Document)
                {
                    bool sel = HasRealTextSelection(element);
                    return (sel, false, sel);
                }

                // Leaf text span (e.g. Chrome renders individual runs as ControlType.Text).
                // Walk up ONE level; only proceed if the parent is a Document.
                if (controlType == ControlType.Text)
                {
                    var parent = TreeWalker.ContentViewWalker.GetParent(element);
                    if (parent != null)
                    {
                        var parentType = parent.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;
                        if (parentType == ControlType.Document)
                        {
                            bool sel = HasRealTextSelection(parent);
                            return (sel, false, sel);
                        }
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

        private static bool IsOverEditableControl(int screenX, int screenY)
            => CheckEditability(screenX, screenY).canSelect;

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
