using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Orbit
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

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int dark = 1;
                DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
            }
            catch { }
        }

        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

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
                ? "API key not configured."
                : $"API key saved. (Last 4: ...{(key.Length >= 4 ? key[^4..] : key)})";

            _suppressEvents = false;

            RefreshActionList();
        }

        private static string DetectProvider(string url)
        {
            if (url.Contains("openrouter.ai")) return "openrouter";
            if (url.Contains("openai.com"))    return "openai";
            return "custom";
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
                ApiKeyStatus.Text = $"API key saved. (Last 4: ...{(key.Length >= 4 ? key[^4..] : key)})";
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

        private void DeleteAction_Click(object sender, RoutedEventArgs e)
        {
            if (ActionsList.SelectedItem is not ActionProfile selected) return;

            var confirm = MessageBox.Show($"Delete action '{selected.Name}'?",
                "Orbit", MessageBoxButton.YesNo, MessageBoxImage.Question);

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
                Filter = "Orbit Action Pack (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".json",
                FileName = "orbit-actions.json",
                Title = "Export Action Pack"
            };

            if (dialog.ShowDialog() == true)
            {
                string error = SettingsManager.ExportActionPack(dialog.FileName);
                if (error == null)
                {
                    MessageBox.Show($"Action pack exported successfully to:\n{dialog.FileName}",
                        "Orbit", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(error, "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportActions_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Orbit Action Pack (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".json",
                Title = "Import Action Pack"
            };

            if (dialog.ShowDialog() == true)
            {
                var result = MessageBox.Show(
                    "Replace existing actions or merge with current actions?\n\n" +
                    "Yes = Replace all existing actions\n" +
                    "No = Merge (skip duplicates by name)\n" +
                    "Cancel = Abort import",
                    "Import Mode",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                bool replaceExisting = (result == MessageBoxResult.Yes);
                string error = SettingsManager.ImportActionPack(dialog.FileName, replaceExisting);

                if (error == null)
                {
                    RefreshActionList();
                    MessageBox.Show(
                        replaceExisting 
                            ? "Action pack imported successfully. All previous actions were replaced."
                            : "Action pack imported successfully. New actions were merged with existing ones.",
                        "Orbit",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(error, "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
