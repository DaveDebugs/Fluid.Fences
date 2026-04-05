using System.Windows;
using System.Windows.Controls;

namespace DesktopFences
{
    public partial class SettingsWindow : Window
    {
        private MainWindow? _callingFence;
        private MainWindow? _currentlySelectedFence;

        public SettingsWindow(MainWindow callingFence)
        {
            InitializeComponent();
            _callingFence = callingFence;

            // Start the engine!
            LoadAllFences();
        }

        private void LoadAllFences()
        {
            FenceListBox.Items.Clear();

            // Ask the operating system for every open window in this application
            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow fence && fence.Visibility == Visibility.Visible)
                {
                    // Create a list item and securely attach the literal window object to its 'Tag'
                    ListBoxItem item = new ListBoxItem
                    {
                        Content = fence.FenceTitle,
                        Tag = fence
                    };

                    FenceListBox.Items.Add(item);

                    // Automatically highlight the fence that the user clicked 'Settings' on
                    if (fence == _callingFence)
                    {
                        FenceListBox.SelectedItem = item;
                    }
                }
            }
        }

        private void FenceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FenceListBox.SelectedItem is ListBoxItem selectedItem && selectedItem.Tag is MainWindow fence)
            {
                _currentlySelectedFence = fence;

                // Load the specific fence's details into the textboxes
                TitleInput.Text = fence.FenceTitle;

                // Match the dropdown to the fence's saved sort rule
                foreach (ComboBoxItem item in SortDropdown.Items)
                {
                    string itemText = item.Content.ToString() ?? "";
                    if (itemText == fence.FenceSortMethod ||
                       (fence.FenceSortMethod == "DateMod" && itemText == "Date Modified") ||
                       (fence.FenceSortMethod == "DateCre" && itemText == "Date Created") ||
                       (fence.FenceSortMethod == "DateAdd" && itemText == "Date added to Fence") ||
                       (fence.FenceSortMethod == "Type" && itemText == "Item Type"))
                    {
                        SortDropdown.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_currentlySelectedFence != null)
            {
                // 1. Push the new title back to the live window
                _currentlySelectedFence.FenceTitle = TitleInput.Text;

                // 2. Translate and push the new sorting rule
                string sortSelection = ((ComboBoxItem)SortDropdown.SelectedItem).Content.ToString() ?? "None";
                _currentlySelectedFence.FenceSortMethod = sortSelection switch
                {
                    "Date Modified" => "DateMod",
                    "Date Created" => "DateCre",
                    "Date added to Fence" => "DateAdd",
                    "Item Type" => "Type",
                    _ => sortSelection
                };

                // 3. Command the window to securely save its own new data to the hard drive
                _currentlySelectedFence.DashboardSaveAndRefresh();

                // 4. Instantly rename the item in the listbox so you see the change
                if (FenceListBox.SelectedItem is ListBoxItem item)
                {
                    item.Content = TitleInput.Text;
                }

                // Give a satisfying confirmation!
                MessageBox.Show("Settings successfully applied to " + TitleInput.Text + "!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DeleteFenceBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentlySelectedFence != null)
            {
                if (MessageBox.Show($"Are you absolutely sure you want to permanently delete the '{_currentlySelectedFence.FenceTitle}' fence? This cannot be undone.", "Delete Fence", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    _currentlySelectedFence.DashboardDelete();
                    LoadAllFences(); // Refresh the list so the deleted fence disappears
                }
            }
        }
    }
}