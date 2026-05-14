using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using Orbital.Services;

namespace Orbital
{
    public partial class SettingsWindow : Window
    {
        // 제공자별 Base URL & 추천 모델 목록
        private static readonly Dictionary<string, (string Url, string[] Models)> Providers = new()
        {
            ["openai"] = (
                "https://api.openai.com/v1",
                new[] { "gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-3.5-turbo" }
            ),
            ["openrouter"] = (
                "https://openrouter.ai/api/v1",
                new[]
                {
                    // 무료 모델 자동 라우팅 (기본값)
                    "openrouter/free",
                    // 유료
                    "openai/gpt-4o-mini",
                    "openai/gpt-4o",
                    "anthropic/claude-3.5-sonnet",
                    "anthropic/claude-3-haiku",
                    "google/gemini-flash-1.5",
                    // 무료 (:free 태그)
                    "meta-llama/llama-3.1-8b-instruct:free",
                    "meta-llama/llama-3.3-70b-instruct:free",
                    "deepseek/deepseek-r1:free",
                    "deepseek/deepseek-chat-v3-0324:free",
                    "google/gemini-2.0-flash-exp:free",
                    "qwen/qwen2.5-vl-72b-instruct:free",
                }
            ),
            ["custom"] = ("", new string[0]),
        };

        private bool _suppressEvents = false;
        private uint _capturedVk = 0;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            bool useDarkMode = ResolveCurrentTheme() != "Light";
            WindowBackdropHelper.TryApply(this, useDarkMode, WindowBackdropKind.MainWindow);
        }

        private static readonly HttpClient _http = new();

        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
            ShowAppVersion();
            _ = CheckForUpdateAsync();
        }

        private void ShowAppVersion()
        {
            // GetEntryAssembly() returns the managed assembly that started the process.
            // Its Version comes from <AssemblyVersion> in the .csproj and is available
            // even in single-file portable executables (no file-path lookup needed).
            var v = Assembly.GetEntryAssembly()?.GetName().Version;
            TxtVersion.Text = v is null ? string.Empty : $"v{v.Major}.{v.Minor}.{v.Build}";
        }

        private async System.Threading.Tasks.Task CheckForUpdateAsync()
        {
            try
            {
                if (!_http.DefaultRequestHeaders.UserAgent.Any())
                    _http.DefaultRequestHeaders.UserAgent.Add(
                        new ProductInfoHeaderValue("Orbital-UpdateCheck", "1.0"));

                using var response = await _http.GetAsync(
                    "https://api.github.com/repos/CrowKing63/Orbital/releases/latest");
                if (!response.IsSuccessStatusCode) return;

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var tag = doc.RootElement.GetProperty("tag_name").GetString(); // e.g. "v0.1.8"
                var url = doc.RootElement.GetProperty("html_url").GetString();
                if (tag is null || url is null) return;

                var remote = ParseVersion(tag.TrimStart('v'));
                var local  = ParseVersion(TxtVersion.Text.TrimStart('v'));
                if (remote is null || local is null) return;

                if (remote > local)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateHyperlink.NavigateUri = new Uri(url);
                        UpdateHyperlinkText.Text = $"↑ New version available: {tag}";
                        TxtUpdateAvailable.Visibility = Visibility.Visible;
                    });
                }
            }
            catch { /* network unavailable — silently ignore */ }
        }

        private static Version? ParseVersion(string s)
            => Version.TryParse(s, out var v) ? v : null;

        private void LoadSettings()
        {
            _suppressEvents = true;

            string currentUrl   = SettingsManager.CurrentSettings.ApiBaseUrl  ?? "https://api.openai.com/v1";
            string currentModel = SettingsManager.CurrentSettings.ModelName   ?? "gpt-4o-mini";

            // 저장된 URL로 제공자 추론
            string providerTag = DetectProvider(currentUrl);
            SelectProvider(providerTag, currentUrl, currentModel);

            string key = SettingsManager.GetApiKey();
            ApiKeyStatus.Text = string.IsNullOrEmpty(key)
                ? Loc.Get("Str_ApiKeyNotConfigured")
                : string.Format(Loc.Get("Str_ApiKeySaved"), key.Length >= 4 ? key[^4..] : key);

            RunAtStartupCheck.IsChecked  = SettingsManager.CurrentSettings.RunAtStartup;
            SoundEnabledCheck.IsChecked  = SettingsManager.CurrentSettings.SoundEnabled;
            foreach (ComboBoxItem item in PopupPositionBox.Items)
            {
                if (item.Tag?.ToString() == SettingsManager.CurrentSettings.PopupPlacement.ToString())
                {
                    PopupPositionBox.SelectedItem = item;
                    break;
                }
            }
            PopupAutoCloseEnabledCheck.IsChecked = SettingsManager.CurrentSettings.PopupAutoCloseEnabled;
            PopupAutoCloseSecondsBox.Text = SettingsManager.CurrentSettings.PopupAutoCloseSeconds.ToString();
            UpdatePopupAutoCloseControls();

            // Theme selector
            string currentTheme = SettingsManager.CurrentSettings.Theme ?? "Dark";
            foreach (ComboBoxItem item in ThemeBox.Items)
            {
                if (item.Tag?.ToString() == currentTheme)
                {
                    ThemeBox.SelectedItem = item;
                    break;
                }
            }

            // Language selector
            string currentLang = SettingsManager.CurrentSettings.Language ?? "en";
            foreach (ComboBoxItem item in LanguageBox.Items)
            {
                if (item.Tag?.ToString() == currentLang)
                {
                    LanguageBox.SelectedItem = item;
                    break;
                }
            }

            // Hotkey
            uint mods = SettingsManager.CurrentSettings.HotkeyModifiers;
            _capturedVk = SettingsManager.CurrentSettings.HotkeyVirtualKey;
            HotkeyCtrlCheck.IsChecked  = (mods & 0x02) != 0;
            HotkeyAltCheck.IsChecked   = (mods & 0x01) != 0;
            HotkeyShiftCheck.IsChecked = (mods & 0x04) != 0;
            HotkeyKeyBox.Text = VkToLabel(_capturedVk);
            UpdateHotkeyHint();

            // Popup Triggers
            DragTriggerCheck.IsChecked = SettingsManager.CurrentSettings.EnableDragTrigger;
            DoubleClickTriggerCheck.IsChecked = SettingsManager.CurrentSettings.EnableDoubleClickTrigger;
            LongPressTriggerCheck.IsChecked = SettingsManager.CurrentSettings.EnableLongPressTrigger;
            KeyboardTriggerCheck.IsChecked = SettingsManager.CurrentSettings.EnableKeyboardSelectionTrigger;

            _suppressEvents = false;

            RefreshActionList();
        }

        private static string DetectProvider(string url)
        {
            if (url.Contains("openrouter.ai")) return "openrouter";
            if (url.Contains("openai.com"))    return "openai";
            return "custom";
        }

        private static string ResolveCurrentTheme()
        {
            string theme = SettingsManager.CurrentSettings.Theme;
            if (theme != "System")
            {
                return theme;
            }

            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return (key?.GetValue("AppsUseLightTheme") is int v && v == 1) ? "Light" : "Dark";
        }

        private void SelectProvider(string tag, string url, string model)
        {
            // ComboBox에서 해당 태그 항목 선택
            foreach (ComboBoxItem item in ProviderBox.Items)
            {
                if (item.Tag?.ToString() == tag)
                {
                    ProviderBox.SelectedItem = item;
                    break;
                }
            }

            // Base URL 채우기
            BaseUrlBox.Text = url;

            // 모델 목록 채우기
            UpdateModelList(tag, model);
        }

        private void UpdateModelList(string providerTag, string currentModel)
        {
            ModelBox.Items.Clear();

            if (Providers.TryGetValue(providerTag, out var info))
            {
                BaseUrlBox.IsReadOnly = (providerTag != "custom");

                foreach (string m in info.Models)
                    ModelBox.Items.Add(m);
            }

            // 현재 모델을 선택하거나 텍스트로 설정
            ModelBox.Text = currentModel;
        }

        private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents || ProviderBox.SelectedItem is not ComboBoxItem selected) return;

            string tag = selected.Tag?.ToString() ?? "custom";

            if (Providers.TryGetValue(tag, out var info))
            {
                if (tag != "custom")
                    BaseUrlBox.Text = info.Url;

                string prevModel = ModelBox.Text;
                UpdateModelList(tag, info.Models.Length > 0 ? info.Models[0] : prevModel);
            }
        }

        private void RunAtStartupCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            bool enable = RunAtStartupCheck.IsChecked == true;
            SettingsManager.CurrentSettings.RunAtStartup = enable;
            SettingsManager.ApplyStartupRegistry(enable);
            SettingsManager.SaveSettings();
        }

        private void SoundEnabledCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            SettingsManager.CurrentSettings.SoundEnabled = SoundEnabledCheck.IsChecked == true;
            SettingsManager.SaveSettings();
        }

        private void PopupTrigger_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            SettingsManager.CurrentSettings.EnableDragTrigger = DragTriggerCheck.IsChecked == true;
            SettingsManager.CurrentSettings.EnableDoubleClickTrigger = DoubleClickTriggerCheck.IsChecked == true;
            SettingsManager.CurrentSettings.EnableLongPressTrigger = LongPressTriggerCheck.IsChecked == true;
            SettingsManager.CurrentSettings.EnableKeyboardSelectionTrigger = KeyboardTriggerCheck.IsChecked == true;
            SettingsManager.SaveSettings();
        }

        private void PopupPositionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            string mode = (PopupPositionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? nameof(PopupPlacementMode.BottomRight);
            if (Enum.TryParse(mode, out PopupPlacementMode parsed))
            {
                SettingsManager.CurrentSettings.PopupPlacement = parsed;
                SettingsManager.SaveSettings();
            }
        }

        private void PopupAutoCloseSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            SettingsManager.CurrentSettings.PopupAutoCloseEnabled = PopupAutoCloseEnabledCheck.IsChecked == true;
            UpdatePopupAutoCloseControls();
            SettingsManager.SaveSettings();
        }

        private void PopupAutoCloseSecondsBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void PopupAutoCloseSecondsBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;

            if (TryApplyPopupAutoCloseSeconds(PopupAutoCloseSecondsBox.Text))
            {
                return;
            }

            PopupAutoCloseSecondsBox.Text = SettingsManager.CurrentSettings.PopupAutoCloseSeconds.ToString();
        }

        private bool TryApplyPopupAutoCloseSeconds(string? value)
        {
            if (!int.TryParse(value, out int seconds) || seconds < 1)
            {
                return false;
            }

            SettingsManager.CurrentSettings.PopupAutoCloseSeconds = seconds;
            SettingsManager.SaveSettings();
            return true;
        }

        private void UpdatePopupAutoCloseControls()
        {
            PopupAutoCloseSecondsBox.IsEnabled = PopupAutoCloseEnabledCheck.IsChecked == true;
        }

        private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            if (ThemeBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                App.ApplyTheme(tag);
                SettingsManager.SaveSettings();
            }
        }

        private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            if (LanguageBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                App.ApplyLanguage(tag);
                SettingsManager.SaveSettings();
                // Refresh hotkey hint text in new language
                UpdateHotkeyHint();
                // Refresh API key status in new language
                string key = SettingsManager.GetApiKey();
                ApiKeyStatus.Text = string.IsNullOrEmpty(key)
                    ? Loc.Get("Str_ApiKeyNotConfigured")
                    : string.Format(Loc.Get("Str_ApiKeySaved"), key.Length >= 4 ? key[^4..] : key);
            }
        }

        private void SaveApiKey_Click(object sender, RoutedEventArgs e)
        {
            // Base URL 저장
            string baseUrl = BaseUrlBox.Text.Trim();
            if (!string.IsNullOrEmpty(baseUrl))
                SettingsManager.CurrentSettings.ApiBaseUrl = baseUrl;

            // 모델명 저장
            string model = ModelBox.Text.Trim();
            if (!string.IsNullOrEmpty(model))
                SettingsManager.CurrentSettings.ModelName = model;

            // API 키 저장 (입력된 경우만)
            string key = ApiKeyBox.Password;
            if (!string.IsNullOrWhiteSpace(key))
            {
                SettingsManager.SetApiKey(key);
                ApiKeyBox.Password = string.Empty;
                ApiKeyStatus.Text = string.Format(Loc.Get("Str_ApiKeySaved"),
                    key.Length >= 4 ? key[^4..] : key);
            }

            SettingsManager.SaveSettings();
        }

        private void ApiKeyBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                SaveApiKey_Click(sender, new RoutedEventArgs());
        }

        private void RefreshActionList()
        {
            ActionsList.ItemsSource = new List<ActionProfile>(SettingsManager.CurrentSettings.Actions);
        }

        private void AddAction_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ActionEditDialog { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                SettingsManager.CurrentSettings.Actions.Add(dialog.Result);
                SettingsManager.SaveSettings();
                RefreshActionList();
            }
        }

        private void EditAction_Click(object sender, RoutedEventArgs e)
        {
            if (ActionsList.SelectedItem is not ActionProfile selected) return;

            var dialog = new ActionEditDialog(selected) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                int idx = SettingsManager.CurrentSettings.Actions.IndexOf(selected);
                if (idx >= 0)
                {
                    SettingsManager.CurrentSettings.Actions[idx] = dialog.Result;
                    SettingsManager.SaveSettings();
                    RefreshActionList();
                }
            }
        }

        private void MoveActionUp_Click(object sender, RoutedEventArgs e)
        {
            if (ActionsList.SelectedItem is not ActionProfile selected) return;
            var actions = SettingsManager.CurrentSettings.Actions;
            int idx = actions.IndexOf(selected);
            if (idx <= 0) return;
            actions.RemoveAt(idx);
            actions.Insert(idx - 1, selected);
            SettingsManager.SaveSettings();
            RefreshActionList();
            ActionsList.SelectedIndex = idx - 1;
        }

        private void MoveActionDown_Click(object sender, RoutedEventArgs e)
        {
            if (ActionsList.SelectedItem is not ActionProfile selected) return;
            var actions = SettingsManager.CurrentSettings.Actions;
            int idx = actions.IndexOf(selected);
            if (idx < 0 || idx >= actions.Count - 1) return;
            actions.RemoveAt(idx);
            actions.Insert(idx + 1, selected);
            SettingsManager.SaveSettings();
            RefreshActionList();
            ActionsList.SelectedIndex = idx + 1;
        }

        private void DeleteAction_Click(object sender, RoutedEventArgs e)
        {
            if (ActionsList.SelectedItem is not ActionProfile selected) return;

            var confirm = MessageBox.Show(
                string.Format(Loc.Get("Str_DeleteActionConfirm"), selected.Name),
                Loc.Get("Str_AppTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                SettingsManager.CurrentSettings.Actions.Remove(selected);
                SettingsManager.SaveSettings();
                RefreshActionList();
            }
        }

        private void SaveAndClose_Click(object sender, RoutedEventArgs e)
        {
            SaveApiKey_Click(sender, e); // Base URL + 모델도 함께 저장
            Close();
        }

        private void ExportActions_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Orbital Action Pack (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".json",
                FileName = "orbital-actions.json",
                Title = "Export Action Pack"
            };

            if (dialog.ShowDialog() == true)
            {
                string? error = SettingsManager.ExportActionPack(dialog.FileName);
                if (error == null)
                {
                    MessageBox.Show(string.Format(Loc.Get("Str_ExportSuccess"), dialog.FileName),
                        Loc.Get("Str_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(error, Loc.Get("Str_ExportFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportActions_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Orbital Action Pack (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".json",
                Title = "Import Action Pack"
            };

            if (dialog.ShowDialog() == true)
            {
                var result = MessageBox.Show(
                    Loc.Get("Str_ImportModeMsg"),
                    Loc.Get("Str_ImportModeTitle"),
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                bool replaceExisting = (result == MessageBoxResult.Yes);
                string? error = SettingsManager.ImportActionPack(dialog.FileName, replaceExisting);

                if (error == null)
                {
                    RefreshActionList();
                    MessageBox.Show(
                        replaceExisting
                            ? Loc.Get("Str_ImportSuccessReplace")
                            : Loc.Get("Str_ImportSuccessMerge"),
                        Loc.Get("Str_AppTitle"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(error, Loc.Get("Str_ImportFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        // ── Keyboard shortcut UI ─────────────────────────────────────────────────

        private void HotkeyKeyBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var res = FindResource("AccentBrush");
            if (res is SolidColorBrush accent)
            {
                var c = accent.Color;
                HotkeyKeyBox.Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0x22, c.R, c.G, c.B));
            }
            else
            {
                HotkeyKeyBox.Background = System.Windows.SystemColors.HighlightBrush;
            }
        }

        private void HotkeyKeyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            HotkeyKeyBox.ClearValue(System.Windows.Controls.TextBox.BackgroundProperty);
        }

        private void HotkeyKeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;

            // Clear on Delete / Backspace
            if (e.Key == Key.Back || e.Key == Key.Delete)
            {
                _capturedVk = 0;
                HotkeyKeyBox.Text = string.Empty;
                SaveHotkeySettings();
                return;
            }

            // Ignore standalone modifier keys
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or
                         Key.LeftAlt  or Key.RightAlt  or
                         Key.LeftShift or Key.RightShift or
                         Key.LWin or Key.RWin or
                         Key.System)
                return;

            Key key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            _capturedVk = (uint)KeyInterop.VirtualKeyFromKey(key);
            HotkeyKeyBox.Text = VkToLabel(_capturedVk);
            SaveHotkeySettings();
        }

        private void Hotkey_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            SaveHotkeySettings();
        }

        private void HotkeyKey_Clear(object sender, RoutedEventArgs e)
        {
            _capturedVk = 0;
            HotkeyKeyBox.Text = string.Empty;
            SaveHotkeySettings();
        }

        private void SaveHotkeySettings()
        {
            uint mods = 0;
            if (HotkeyCtrlCheck.IsChecked  == true) mods |= 0x02;
            if (HotkeyAltCheck.IsChecked   == true) mods |= 0x01;
            if (HotkeyShiftCheck.IsChecked == true) mods |= 0x04;

            SettingsManager.CurrentSettings.HotkeyModifiers  = mods;
            SettingsManager.CurrentSettings.HotkeyVirtualKey = _capturedVk;
            SettingsManager.SaveSettings();
            App.ApplyHotkeySettings();
            UpdateHotkeyHint();
        }

        private void UpdateHotkeyHint()
        {
            if (_capturedVk == 0)
            {
                HotkeyHint.Text = Loc.Get("Str_HotkeyNone");
                return;
            }

            var parts = new List<string>();
            if (HotkeyCtrlCheck.IsChecked  == true) parts.Add("Ctrl");
            if (HotkeyAltCheck.IsChecked   == true) parts.Add("Alt");
            if (HotkeyShiftCheck.IsChecked == true) parts.Add("Shift");
            parts.Add(VkToLabel(_capturedVk));
            HotkeyHint.Text = string.Format(Loc.Get("Str_HotkeyActive"), string.Join(" + ", parts));
        }

        private static string VkToLabel(uint vk)
        {
            if (vk == 0) return string.Empty;
            Key key = KeyInterop.KeyFromVirtualKey((int)vk);
            // Return a human-readable label for common keys (key names stay in English)
            return key switch
            {
                Key.Space  => "Space",
                Key.Return => "Enter",
                Key.Escape => "Esc",
                Key.Tab    => "Tab",
                _ => key.ToString()
            };
        }
    }
}
