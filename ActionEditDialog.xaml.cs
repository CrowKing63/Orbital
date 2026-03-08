using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace Orbit
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
                PromptBox.Text = existing.PromptFormat;
                RequiresSelectionCheck.IsChecked = existing.IsSelectionRequired;

                foreach (ComboBoxItem item in ResultActionBox.Items)
                {
                    if (item.Content?.ToString() == existing.ResultAction)
                    {
                        ResultActionBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Please enter a button name.", "Orbit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new ActionProfile
            {
                Name = NameBox.Text.Trim(),
                PromptFormat = PromptBox.Text,
                ResultAction = (ResultActionBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Popup",
                RequiresSelection = RequiresSelectionCheck.IsChecked
            };

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
