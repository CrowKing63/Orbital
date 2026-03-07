using System.Windows;
using System.Windows.Controls;

namespace Orbit
{
    public partial class ActionEditDialog : Window
    {
        public ActionProfile Result { get; private set; } = new ActionProfile();

        public ActionEditDialog(ActionProfile? existing = null)
        {
            InitializeComponent();

            if (existing != null)
            {
                NameBox.Text = existing.Name;
                PromptBox.Text = existing.PromptFormat;

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
                MessageBox.Show("버튼 이름을 입력하세요.", "Orbit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new ActionProfile
            {
                Name = NameBox.Text.Trim(),
                PromptFormat = PromptBox.Text,
                ResultAction = (ResultActionBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Popup"
            };

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
