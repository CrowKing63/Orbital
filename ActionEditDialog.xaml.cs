using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Orbital.Services;

namespace Orbital
{
    public partial class ActionEditDialog : Window
    {
        public ActionProfile Result { get; private set; } = new ActionProfile();

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            bool useDarkMode = ResolveCurrentTheme() != "Light";
            WindowBackdropHelper.TryApply(this, useDarkMode, WindowBackdropKind.TransientWindow);
        }

        public ActionEditDialog(ActionProfile? existing = null)
        {
            InitializeComponent();

            if (existing != null)
            {
                PopulateFields(existing);
            }
        }

        private void PopulateFields(ActionProfile profile)
        {
            NameBox.Text = profile.Name;
            IconBox.Text = profile.Icon;
            PromptBox.Text = profile.PromptFormat;
            RequiresSelectionCheck.IsChecked = profile.IsSelectionRequired;
            CleanOutputCheck.IsChecked = profile.CleanOutput;

            // Match by Tag (serialized value) so localized Content doesn't break selection
            foreach (ComboBoxItem item in ResultActionBox.Items)
            {
                if (item.Tag?.ToString() == profile.ActionType.ToSerializedString())
                {
                    ResultActionBox.SelectedItem = item;
                    break;
                }
            }

            // Set DisplayModeBox selection
            foreach (ComboBoxItem item in DisplayModeBox.Items)
            {
                if (item.Tag?.ToString() == profile.DisplayMode.ToString())
                {
                    DisplayModeBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void LoadPreset_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = LoadPresetButton, Placement = PlacementMode.Bottom };
            foreach (var preset in ActionPresets.All)
            {
                string display = string.IsNullOrEmpty(preset.Icon)
                    ? preset.Name
                    : $"{preset.Icon}  {preset.Name}";
                var item = new MenuItem { Header = display, Tag = preset };
                item.Click += (_, _) => PopulateFields((ActionProfile)((MenuItem)item).Tag!);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show(Loc.Get("Str_ButtonNameRequired"), Loc.Get("Str_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedActionString = (ResultActionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Popup";
            var selectedMode = (DisplayModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "TextAndIcon";
            
            Result = new ActionProfile
            {
                Name = NameBox.Text.Trim(),
                Icon = ParseIconInput(IconBox.Text.Trim()),
                PromptFormat = PromptBox.Text,
                ActionType = ActionTypeExtensions.FromString(selectedActionString),
                RequiresSelection = RequiresSelectionCheck.IsChecked,
                DisplayMode = Enum.TryParse<ButtonDisplayMode>(selectedMode, out var mode) ? mode : ButtonDisplayMode.TextAndIcon,
                CleanOutput = CleanOutputCheck.IsChecked == true
            };

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        // Converts "\uE8C8" or "U+E8C8" typed as text into the actual Unicode character.
        private static string ParseIconInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            return Regex.Replace(input, @"(?:\\u|U\+)([0-9a-fA-F]{4,5})", m =>
                char.ConvertFromUtf32(Convert.ToInt32(m.Groups[1].Value, 16)));
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
    }
}
