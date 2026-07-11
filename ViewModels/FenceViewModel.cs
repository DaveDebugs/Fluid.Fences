using DesktopFences.Core;

namespace DesktopFences.ViewModels
{
    public class FenceViewModel : ObservableObject
    {
        private string _fenceTitle = "Fluid Fence";

        public string FenceTitle
        {
            get => _fenceTitle;
            set => SetProperty(ref _fenceTitle, value);
        }

        // Example Command: How MVVM handles button clicks without Code-Behind
        public RelayCommand UpdateTitleCommand { get; }

        public FenceViewModel()
        {
            UpdateTitleCommand = new RelayCommand(o =>
            {
                FenceTitle = "Title Updated via MVVM!";
            });
        }
    }
}