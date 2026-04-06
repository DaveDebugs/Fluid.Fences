using System.Windows;
using System.Windows.Controls;

namespace DesktopFences
{
    public partial class SettingsWindow : Window
    {
        private MainWindow? _callingFence;
        private MainWindow? _currentlySelectedFence;
        private bool _isLoadingFenceData = false; // Prevents dropdown from overwriting during load

        public SettingsWindow(MainWindow callingFence)
        {
            InitializeComponent();
            _callingFence = callingFence;
            PresetDropdown.SelectedIndex = 0; // Default to Custom
            LoadAllFences();
        }

        private void LoadAllFences()
        {
            FenceListBox.Items.Clear();

            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow fence && fence.Visibility == Visibility.Visible)
                {
                    ListBoxItem item = new ListBoxItem
                    {
                        Content = fence.FenceTitle,
                        Tag = fence
                    };

                    FenceListBox.Items.Add(item);

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
                _isLoadingFenceData = true;

                TitleInput.Text = fence.FenceTitle;
                AutoSortInput.Text = fence.AutoSortExtensions;

                // Reset dropdown to "Custom" when loading a new fence so we don't accidentally overwrite their rules
                PresetDropdown.SelectedIndex = 0;

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

                _isLoadingFenceData = false;
            }
        }

        // FIXED: The Library of Extension Rules!
        private void PresetDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingFenceData || PresetDropdown.SelectedItem == null) return;

            string selection = ((ComboBoxItem)PresetDropdown.SelectedItem).Content.ToString() ?? "";

            switch (selection)
            {
                case "Images & Photos":
                    AutoSortInput.Text = ".jpg, .jpeg, .png, .gif, .bmp, .webp, .svg, .ico, .tif";
                    break;
                case "Documents":
                    AutoSortInput.Text = ".doc, .docx, .pdf, .txt, .rtf, .odt, .xls, .xlsx, .csv, .ppt, .pptx";
                    break;
                case "Audio & Music":
                    AutoSortInput.Text = ".mp3, .wav, .flac, .ogg, .m4a, .wma";
                    break;
                case "Videos":
                    AutoSortInput.Text = ".mp4, .mkv, .avi, .mov, .wmv, .webm, .m4v";
                    break;
                case "Archives (.zip)":
                    AutoSortInput.Text = ".zip, .rar, .7z, .tar, .gz";
                    break;
                case "Apps & Shortcuts":
                    AutoSortInput.Text = ".lnk, .exe, .url, .bat, .msi";
                    break;
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_currentlySelectedFence != null)
            {
                _currentlySelectedFence.FenceTitle = TitleInput.Text;
                _currentlySelectedFence.AutoSortExtensions = AutoSortInput.Text;

                string sortSelection = ((ComboBoxItem)SortDropdown.SelectedItem).Content.ToString() ?? "None";
                _currentlySelectedFence.FenceSortMethod = sortSelection switch
                {
                    "Date Modified" => "DateMod",
                    "Date Created" => "DateCre",
                    "Date added to Fence" => "DateAdd",
                    "Item Type" => "Type",
                    _ => sortSelection
                };

                _currentlySelectedFence.DashboardSaveAndRefresh();

                if (FenceListBox.SelectedItem is ListBoxItem item)
                {
                    item.Content = TitleInput.Text;
                }

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
                    LoadAllFences();
                }
            }
        }
    }
}