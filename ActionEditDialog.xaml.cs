using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace Orbital
{
    public partial class ActionEditDialog : Window
    {
        public ActionProfile Result { get; private set; } = new ActionProfile();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int dark = 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            }
            catch { }
        }

        public ActionEditDialog(ActionProfile? existing = null)
        {
            InitializeComponent();

            if (existing != null)
            {
                NameBox.Text = existing.Name;
                IconBox.Text = existing.Icon;
                PromptBox.Text = existing.PromptFormat;
                RequiresSelectionCheck.IsChecked = existing.IsSelectionRequired;

                // Use typed ActionType instead of string comparison
                foreach (ComboBoxItem item in ResultActionBox.Items)
                {
                    if (item.Content?.ToString() == existing.ActionType.ToSerializedString())
                    {
                        ResultActionBox.SelectedItem = item;
                        break;
                    }
                }

                // Set DisplayModeBox selection
                foreach (ComboBoxItem item in DisplayModeBox.Items)
                {
                    if (item.Tag?.ToString() == existing.DisplayMode.ToString())
                    {
                        DisplayModeBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Please enter a button name.", "Orbital", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedActionString = (ResultActionBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Popup";
            var selectedMode = (DisplayModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "TextAndIcon";
            
            Result = new ActionProfile
            {
                Name = NameBox.Text.Trim(),
                Icon = IconBox.Text.Trim(),
                PromptFormat = PromptBox.Text,
                ActionType = ActionTypeExtensions.FromString(selectedActionString),
                RequiresSelection = RequiresSelectionCheck.IsChecked,
                DisplayMode = Enum.TryParse<ButtonDisplayMode>(selectedMode, out var mode) ? mode : ButtonDisplayMode.TextAndIcon
            };

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
