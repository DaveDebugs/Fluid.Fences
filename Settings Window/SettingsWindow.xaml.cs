using System.Windows;
using System.Windows.Controls;

namespace DesktopFences
{
    public partial class SettingsWindow : Window
    {
        // These variables hold the data we will send back to the main fence
        public string SelectedTitle { get; private set; }
        public string SelectedHexColor { get; private set; }

        public SettingsWindow(string currentTitle, string currentHexColor)
        {
            InitializeComponent();

            // 1. Pre-fill the textbox with the fence's current title
            TitleInput.Text = currentTitle;

            // 2. Pre-select the current color in the dropdown menu
            foreach (ComboBoxItem item in ColorDropdown.Items)
            {
                if (item.Tag.ToString() == currentHexColor)
                {
                    ColorDropdown.SelectedItem = item;
                    break;
                }
            }

            // Fallback just in case nothing matched
            if (ColorDropdown.SelectedItem == null) ColorDropdown.SelectedIndex = 0;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Grab the text from the box
            SelectedTitle = TitleInput.Text;

            // Grab the hidden Hex Color tag from the dropdown
            ComboBoxItem selectedColorItem = (ComboBoxItem)ColorDropdown.SelectedItem;
            SelectedHexColor = selectedColorItem.Tag.ToString();

            // Tell WPF that the user successfully clicked Save, then close the window
            this.DialogResult = true;
            this.Close();
        }
    }
}