using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Linq;
using Microsoft.Win32;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace DesktopFences
{
    public partial class MainWindow : Window
    {
        private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp", ".ico" };

        public enum DockState { Floating, Top, Bottom, Left, Right }

        private readonly DispatcherTimer _saveTimer = new();
        private readonly DispatcherTimer _portalDebounceTimer = new();
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly List<string> _currentFiles = [];
        private readonly List<StackPanel> _selectedItems = [];
        private readonly SolidColorBrush _highlightBrush = new(System.Windows.Media.Color.FromArgb(85, 0, 120, 215));

        private readonly System.Threading.SemaphoreSlim _iconSemaphore = new(4, 4);

        public string FenceId { get; } = string.Empty;
        private readonly string _saveFilePath = string.Empty;
        public double CurrentIconSize { get; set; } = 48;

        private bool _isRolledUp; 
        private double _expandedHeight = 300;
        private double _expandedWidth = 250;
        private double _expandedLeft; 
        private double _expandedTop;
        private const double HEADER_SIZE = 35;

        private DockState _dockState = DockState.Floating;
        private bool _isTemporarilyRevealed;
        private string _saveSortMethod = "None";
        private bool _isContextMenuOpen;
        private bool _isDeleted;

        private bool _isPortal;
        private string _portalPath = "";
        private FileSystemWatcher? _portalWatcher;

        private Color _currentFenceColor = Colors.Black;
        private double _currentOpacity = 0.7;
        private bool _autoMatchColor;

        private bool _globalGhostMode; 
        private int _ghostModeOverride;

        private System.Windows.Point _selectionStartPoint;
        private bool _isDraggingSelectionBox; 
        private bool _isManualDragging; 
        private System.Windows.Point _manualDragStartMouse;
        private double _manualDragStartLeft;
        private double _manualDragStartTop;
        private bool _wasRolledUpBeforeDrag;

        [JsonIgnore] public string FenceTitle { get => TitleText.Text; set => TitleText.Text = value; }
        [JsonIgnore] public string FenceSortMethod { get => _saveSortMethod; set => _saveSortMethod = value; }
        [JsonIgnore] public string AutoSortExtensions { get; set; } = "";
        [JsonIgnore] public bool ShowSearch { get; set; } = true;
        [JsonIgnore] public Color FenceColor { get => _currentFenceColor; }
        [JsonIgnore] public double FenceOpacity { get => _currentOpacity; }
        [JsonIgnore] public bool AutoMatchColor { get => _autoMatchColor; set => _autoMatchColor = value; }
        [JsonIgnore] public int GhostModeOverride { get => _ghostModeOverride; set => _ghostModeOverride = value; }

        public string GetFenceId() { return FenceId; }
        public bool GetIsRolledUp() { return _isRolledUp; }

        public static readonly DependencyProperty GlassOpacityProperty =
            DependencyProperty.Register("GlassOpacity", typeof(double), typeof(MainWindow),
            new PropertyMetadata(1.0, OnGlassOpacityChanged));

        public double GlassOpacity
        {
            get { return (double)GetValue(GlassOpacityProperty); }
            set { SetValue(GlassOpacityProperty, value); }
        }

        private static void OnGlassOpacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MainWindow window)
            {
                window.ApplyAcrylicToHwnd(new WindowInteropHelper(window).Handle, window._currentFenceColor, window._currentOpacity * (double)e.NewValue);
            }
        }

        public void SetFenceColor(Color color, double opacity) { _currentFenceColor = color; _currentOpacity = opacity; ApplyAcrylicBlur(color, opacity); SaveFenceState(); }
        public void DashboardSaveAndRefresh() { ApplySorting(); SaveFenceState(); AnimateGhostMode(false); }
        public void UpdateGlobalGhostMode(bool isEnabled) { _globalGhostMode = isEnabled; AnimateGhostMode(false); }

        public void UpdateSearchVisibility()
        {
            if (SearchIcon is not null) SearchIcon.Visibility = ShowSearch ? Visibility.Visible : Visibility.Collapsed;
            if (SearchBox is not null) { SearchBox.Visibility = Visibility.Collapsed; SearchBox.Text = string.Empty; }
        }

        public void DashboardDelete()
        {
            ProcessVaultOnDeletion();
            _isDeleted = true;
            if (File.Exists(_saveFilePath)) File.Delete(_saveFilePath);
            this.Close();
        }

        public void RestoreSnapshot(double newLeft, double newTop, double newWidth, double newHeight, bool newIsRolledUp)
        {
            this.BeginAnimation(Window.LeftProperty, null); this.BeginAnimation(Window.TopProperty, null);
            this.BeginAnimation(Window.WidthProperty, null); this.BeginAnimation(Window.HeightProperty, null);

            _isRolledUp = false; _isTemporarilyRevealed = false;
            this.Left = newLeft; this.Top = newTop; this.Width = newWidth; this.Height = newHeight;

            DetermineDockState(); UpdateDockOrientation();
            _expandedLeft = this.Left; _expandedTop = this.Top; _expandedWidth = this.Width; _expandedHeight = this.Height;

            if (newIsRolledUp) ToggleRollUp();
            SaveFenceState();
        }

        private void ProcessVaultOnDeletion()
        {
            if (_isPortal) return;
            try
            {
                string globalConfigPath = Path.Combine(App.ConfigFolder, "global_config.json");
                bool restoreFiles = true;

                if (File.Exists(globalConfigPath))
                {
                    try
                    {
                        string json = File.ReadAllText(globalConfigPath);
                        if (JsonSerializer.Deserialize<GlobalConfig>(json) is GlobalConfig config) restoreFiles = config.RestoreFilesOnDelete;
                    }
                    catch { }
                }

                string storageDir = Path.Combine(App.ConfigFolder, "Storage", FenceId);

                if (Directory.Exists(storageDir))
                {
                    if (restoreFiles)
                    {
                        var allEntries = Directory.GetFileSystemEntries(storageDir);
                        bool hasRealFiles = false;

                        foreach (string file in allEntries)
                        {
                            if (!file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                            {
                                hasRealFiles = true;
                                break;
                            }
                        }

                        if (hasRealFiles)
                        {
                            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                            string safeTitle = string.Join("_", TitleText.Text.Split(Path.GetInvalidFileNameChars()));
                            if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "Recovered Fence";

                            string restoreDir = Path.Combine(desktopPath, $"{safeTitle}");

                            int counter = 1;
                            while (Directory.Exists(restoreDir) || File.Exists(restoreDir)) { restoreDir = Path.Combine(desktopPath, $"{safeTitle} ({counter})"); counter++; }
                            Directory.CreateDirectory(restoreDir);

                            foreach (string file in allEntries)
                            {
                                if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                                {
                                    File.Delete(file);
                                }
                                else
                                {
                                    string dest = Path.Combine(restoreDir, Path.GetFileName(file));
                                    if (File.Exists(file)) File.Move(file, dest);
                                    else if (Directory.Exists(file)) Directory.Move(file, dest);
                                }
                            }
                        }

                        if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
                    }
                    else
                    {
                        SendToRecycleBin(storageDir);
                    }
                }
            }
            catch (Exception ex) { LogError("ProcessVaultOnDeletion", ex); }
        }

        public MainWindow()
        {
            InitializeComponent();
            FenceId = "DESIGNER";
            _saveFilePath = Path.Combine(App.ConfigFolder, "Fences", "designer.json");

            _saveTimer.Interval = TimeSpan.FromSeconds(1.5);
            _saveTimer.Tick += async (s, e) => { _saveTimer.Stop(); await PerformDiskWriteAsync(); };

            _portalDebounceTimer.Interval = TimeSpan.FromMilliseconds(200);
            _portalDebounceTimer.Tick += (s, e) => { _portalDebounceTimer.Stop(); RefreshPortalUI(); };
        }

        public MainWindow(string fenceId) : this()
        {
            FenceId = fenceId;
            _saveFilePath = Path.Combine(App.ConfigFolder, "Fences", $"{FenceId}.json");
        }

        private static void LogError(string context, Exception ex)
        {
            try
            {
                string logPath = Path.Combine(App.ConfigFolder, "error.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex.Message}\n");
            }
            catch { }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), null);
            SearchBox.Text = ""; SearchBox.Visibility = Visibility.Collapsed;

            if (_isRolledUp && _isTemporarilyRevealed) { AnimateRollUp(); _isTemporarilyRevealed = false; }
            AnimateGhostMode(false);
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox textBox && !textBox.IsMouseOver)
            {
                Keyboard.ClearFocus();
                FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), null);
            }
        }

        private void ApplyAcrylicToHwnd(IntPtr hwnd, Color color, double opacity)
        {
            if (hwnd == IntPtr.Zero) return;
            byte a = (byte)(opacity * 255);
            uint gradientColor = (uint)((a << 24) | (color.B << 16) | (color.G << 8) | color.R);
            var accent = new NativeMethods.AccentPolicy { AccentState = NativeMethods.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, GradientColor = gradientColor };
            var accentStructSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new NativeMethods.WindowCompositionAttributeData { Attribute = NativeMethods.WindowCompositionAttribute.WCA_ACCENT_POLICY, SizeOfData = accentStructSize, Data = accentPtr };
            NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(accentPtr);
            int cornerPreference = (int)NativeMethods.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, (int)NativeMethods.DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
        }

        private void ApplyAcrylicBlur(Color color, double opacity)
        {
            _currentFenceColor = color; _currentOpacity = opacity;
            var windowHelper = new WindowInteropHelper(this);
            if (windowHelper.Handle == IntPtr.Zero) return;
            ApplyAcrylicToHwnd(windowHelper.Handle, color, opacity * GlassOpacity);
            MainBorder.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        }

        private void ContextMenu_Opened_ApplyBlur(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu menu)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (PresentationSource.FromVisual(menu) is HwndSource source && source.Handle != IntPtr.Zero)
                        ApplyAcrylicToHwnd(source.Handle, Color.FromRgb(26, 26, 26), 0.8);
                }), DispatcherPriority.Background);
            }
        }

        private void Submenu_Opened_ApplyBlur(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is MenuItem menuItem)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (menuItem.Template.FindName("PART_Popup", menuItem) is System.Windows.Controls.Primitives.Popup popup && popup.Child != null)
                    {
                        if (PresentationSource.FromVisual(popup.Child) is HwndSource source && source.Handle != IntPtr.Zero)
                            ApplyAcrylicToHwnd(source.Handle, Color.FromRgb(26, 26, 26), 0.8);
                    }
                }), DispatcherPriority.Background);
            }
        }

        private async Task LoadFenceStateAsync()
        {
            if (File.Exists(_saveFilePath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_saveFilePath);
                    if (JsonSerializer.Deserialize<FenceData>(json) is FenceData data)
                    {
                        this.Left = data.Left; this.Top = data.Top;
                        bool isOnScreen = false;
                        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                        {
                            if (screen.WorkingArea.Contains((int)this.Left, (int)this.Top) || screen.WorkingArea.Contains((int)(this.Left + data.Width), (int)this.Top))
                            { isOnScreen = true; break; }
                        }
                        if (!isOnScreen) { this.Left = 100; this.Top = 100; }

                        _isRolledUp = data.IsRolledUp;
                        _expandedHeight = data.ExpandedHeight; _expandedWidth = data.ExpandedWidth > 0 ? data.ExpandedWidth : 250;
                        _expandedLeft = data.ExpandedWidth > 0 ? data.ExpandedLeft : data.Left; _expandedTop = data.ExpandedWidth > 0 ? data.ExpandedTop : data.Top;
                        TitleText.Text = data.Title; _saveSortMethod = data.SortMethod;
                        AutoSortExtensions = data.AutoSortExtensions; ShowSearch = data.ShowSearch;

                        if (SearchIcon is not null) SearchIcon.Visibility = ShowSearch ? Visibility.Visible : Visibility.Collapsed;
                        if (SearchBox is not null) SearchBox.Visibility = Visibility.Collapsed;

                        CurrentIconSize = data.IconSize > 0 ? data.IconSize : 48;
                        _autoMatchColor = data.AutoMatchColor; _ghostModeOverride = data.GhostModeOverride;

                        try { _currentFenceColor = (Color)ColorConverter.ConvertFromString(data.HexColor); } catch { }
                        _currentOpacity = data.Opacity;

                        _isPortal = data.IsPortal; _portalPath = data.PortalPath;

                        if (_isRolledUp) { if (data.Width <= 40) { this.Width = HEADER_SIZE; this.Height = data.Height; } else { this.Height = HEADER_SIZE; this.Width = data.Width; } }
                        else { this.Height = data.Height; this.Width = data.Width; }

                        if (_isPortal && !string.IsNullOrEmpty(_portalPath)) SetupPortalWatcher();
                        else { _currentFiles.Clear(); foreach (string file in data.Files) { if (File.Exists(file) || Directory.Exists(file)) { _currentFiles.Add(file); } } }

                        DetermineDockState(); UpdateDockOrientation(); ApplySorting();
                    }
                }
                catch (Exception ex) { LogError("LoadFenceStateAsync", ex); }
            }
        }

        private void SaveFenceState() { _saveTimer.Stop(); _saveTimer.Start(); }

        private async Task PerformDiskWriteAsync()
        {
            if (_isDeleted || TitleText is null) return;
            try
            {
                FenceData data = new()
                {
                    Left = this.Left,
                    Top = this.Top,
                    Width = this.Width,
                    Height = this.Height,
                    IsRolledUp = _isRolledUp,
                    ExpandedHeight = _expandedHeight,
                    ExpandedWidth = _expandedWidth,
                    ExpandedLeft = _expandedLeft,
                    ExpandedTop = _expandedTop,
                    Title = TitleText.Text,
                    HexColor = $"#{_currentFenceColor.R:X2}{_currentFenceColor.G:X2}{_currentFenceColor.B:X2}",
                    Opacity = _currentOpacity,
                    SortMethod = _saveSortMethod,
                    AutoSortExtensions = AutoSortExtensions,
                    IconSize = CurrentIconSize,
                    ShowSearch = ShowSearch,
                    Files = _currentFiles.ToList(),
                    IsPortal = _isPortal,
                    PortalPath = _portalPath,
                    AutoMatchColor = _autoMatchColor,
                    GhostModeOverride = _ghostModeOverride
                };

                string tempPath = _saveFilePath + ".tmp";
                string saveDir = Path.GetDirectoryName(_saveFilePath)!;
                Directory.CreateDirectory(saveDir);

                await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(data, _jsonOptions));
                if (File.Exists(_saveFilePath)) File.Delete(_saveFilePath);
                File.Move(tempPath, _saveFilePath);
            }
            catch (Exception ex) { LogError("PerformDiskWriteAsync", ex); }
        }

        private void DetermineDockState()
        {
            var helper = new WindowInteropHelper(this);
            var workArea = System.Windows.Forms.Screen.FromHandle(helper.Handle).WorkingArea;
            double dpiX = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            int physLeft = (int)(this.Left * dpiX); int physTop = (int)(this.Top * dpiY);
            int physRight = (int)((this.Left + this.Width) * dpiX); int physBottom = (int)((this.Top + this.Height) * dpiY);
            int thresh = 35; int dLeft = Math.Abs(physLeft - workArea.Left); int dRight = Math.Abs(physRight - workArea.Right);
            int dTop = Math.Abs(physTop - workArea.Top); int dBottom = Math.Abs(physBottom - workArea.Bottom);
            int minH = Math.Min(dLeft, dRight); int minV = Math.Min(dTop, dBottom);

            if (minH >= thresh && minV >= thresh) _dockState = DockState.Floating;
            else if (minH < thresh && minV < thresh)
            {
                if (_dockState == DockState.Left || _dockState == DockState.Right) _dockState = dRight < dLeft ? DockState.Right : DockState.Left;
                else if (_dockState == DockState.Top || _dockState == DockState.Bottom) _dockState = dBottom < dTop ? DockState.Bottom : DockState.Top;
                else _dockState = this.Width > this.Height ? (dBottom <= dTop ? DockState.Bottom : DockState.Top) : (dRight <= dLeft ? DockState.Right : DockState.Left);
            }
            else if (minV < minH) _dockState = dBottom < dTop ? DockState.Bottom : DockState.Top;
            else if (minH < minV) _dockState = dRight < dLeft ? DockState.Right : DockState.Left;
            else _dockState = this.Width > this.Height ? (dBottom <= dTop ? DockState.Bottom : DockState.Top) : (dRight <= dLeft ? DockState.Right : DockState.Left);
        }

        private void UpdateDockOrientation()
        {
            if (_dockState == DockState.Left)
            {
                HeaderRowTop.Height = new GridLength(0); HeaderRowBottom.Height = new GridLength(0);
                HeaderColLeft.Width = new GridLength(HEADER_SIZE); HeaderColRight.Width = new GridLength(0);
                Grid.SetRow(HeaderBorder, 0); Grid.SetRowSpan(HeaderBorder, 3); Grid.SetColumn(HeaderBorder, 0); Grid.SetColumnSpan(HeaderBorder, 1);
                HeaderGrid.LayoutTransform = new RotateTransform(-90); HeaderBorder.CornerRadius = new CornerRadius(8, 0, 0, 8);
                if (SearchBox is not null) { Grid.SetRow(SearchBox, 0); Grid.SetRowSpan(SearchBox, 3); Grid.SetColumn(SearchBox, 0); Grid.SetColumnSpan(SearchBox, 1); SearchBox.LayoutTransform = new RotateTransform(-90); SearchBox.HorizontalAlignment = HorizontalAlignment.Center; SearchBox.VerticalAlignment = VerticalAlignment.Center; SearchBox.Margin = new Thickness(0); SearchBox.Width = 140; }
            }
            else if (_dockState == DockState.Right)
            {
                HeaderRowTop.Height = new GridLength(0); HeaderRowBottom.Height = new GridLength(0);
                HeaderColLeft.Width = new GridLength(0); HeaderColRight.Width = new GridLength(HEADER_SIZE);
                Grid.SetRow(HeaderBorder, 0); Grid.SetRowSpan(HeaderBorder, 3); Grid.SetColumn(HeaderBorder, 2); Grid.SetColumnSpan(HeaderBorder, 1);
                HeaderGrid.LayoutTransform = new RotateTransform(90); HeaderBorder.CornerRadius = new CornerRadius(0, 8, 8, 0);
                if (SearchBox is not null) { Grid.SetRow(SearchBox, 0); Grid.SetRowSpan(SearchBox, 3); Grid.SetColumn(SearchBox, 2); Grid.SetColumnSpan(SearchBox, 1); SearchBox.LayoutTransform = new RotateTransform(90); SearchBox.HorizontalAlignment = HorizontalAlignment.Center; SearchBox.VerticalAlignment = VerticalAlignment.Center; SearchBox.Margin = new Thickness(0); SearchBox.Width = 140; }
            }
            else if (_dockState == DockState.Bottom)
            {
                HeaderRowTop.Height = new GridLength(0); HeaderRowBottom.Height = new GridLength(HEADER_SIZE);
                HeaderColLeft.Width = new GridLength(0); HeaderColRight.Width = new GridLength(0);
                Grid.SetRow(HeaderBorder, 2); Grid.SetRowSpan(HeaderBorder, 1); Grid.SetColumn(HeaderBorder, 0); Grid.SetColumnSpan(HeaderBorder, 3);
                HeaderGrid.LayoutTransform = Transform.Identity; HeaderBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);
                if (SearchBox is not null) { Grid.SetRow(SearchBox, 2); Grid.SetRowSpan(SearchBox, 1); Grid.SetColumn(SearchBox, 0); Grid.SetColumnSpan(SearchBox, 3); SearchBox.LayoutTransform = Transform.Identity; SearchBox.HorizontalAlignment = HorizontalAlignment.Right; SearchBox.VerticalAlignment = VerticalAlignment.Bottom; SearchBox.Margin = new Thickness(0, 0, 35, 4); SearchBox.Width = 160; }
            }
            else
            {
                HeaderRowTop.Height = new GridLength(HEADER_SIZE); HeaderRowBottom.Height = new GridLength(0);
                HeaderColLeft.Width = new GridLength(0); HeaderColRight.Width = new GridLength(0);
                Grid.SetRow(HeaderBorder, 0); Grid.SetRowSpan(HeaderBorder, 1); Grid.SetColumn(HeaderBorder, 0); Grid.SetColumnSpan(HeaderBorder, 3);
                HeaderGrid.LayoutTransform = Transform.Identity; HeaderBorder.CornerRadius = new CornerRadius(8, 8, 0, 0);
                if (SearchBox is not null) { Grid.SetRow(SearchBox, 0); Grid.SetRowSpan(SearchBox, 1); Grid.SetColumn(SearchBox, 0); Grid.SetColumnSpan(SearchBox, 3); SearchBox.LayoutTransform = Transform.Identity; SearchBox.HorizontalAlignment = HorizontalAlignment.Right; SearchBox.VerticalAlignment = VerticalAlignment.Top; SearchBox.Margin = new Thickness(0, 4, 35, 0); SearchBox.Width = 160; }
            }
        }

        private void AnimateGhostMode(bool isHovering)
        {
            bool useGhost = _globalGhostMode;
            if (_ghostModeOverride == 1) useGhost = true;
            if (_ghostModeOverride == 2) useGhost = false;

            if (!useGhost)
            {
                if (MainGrid.Opacity < 1.0) { MainGrid.BeginAnimation(OpacityProperty, null); this.BeginAnimation(GlassOpacityProperty, null); MainGrid.Opacity = 1.0; GlassOpacity = 1.0; }
                return;
            }

            double targetOpacity = isHovering ? 1.0 : 0.01;
            double targetGlass = isHovering ? 1.0 : 0.0;
            DoubleAnimation animOpacity = new() { To = targetOpacity, Duration = TimeSpan.FromMilliseconds(250) };
            DoubleAnimation animGlass = new() { To = targetGlass, Duration = TimeSpan.FromMilliseconds(250) };
            MainGrid.BeginAnimation(OpacityProperty, animOpacity); this.BeginAnimation(GlassOpacityProperty, animGlass);
        }

        private void AnimateRollUp()
        {
            double d = 250; QuarticEase ease = new() { EasingMode = EasingMode.EaseInOut };
            if (_dockState == DockState.Left) { this.BeginAnimation(Window.WidthProperty, new DoubleAnimation { To = HEADER_SIZE, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease }); }
            else if (_dockState == DockState.Right) { this.BeginAnimation(Window.WidthProperty, new DoubleAnimation { To = HEADER_SIZE, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease }); this.BeginAnimation(Window.LeftProperty, new DoubleAnimation { To = _expandedLeft + (_expandedWidth - HEADER_SIZE), Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease }); }
            else if (_dockState == DockState.Bottom) { this.BeginAnimation(Window.HeightProperty, new DoubleAnimation { To = HEADER_SIZE, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease }); this.BeginAnimation(Window.TopProperty, new DoubleAnimation { To = _expandedTop + (_expandedHeight - HEADER_SIZE), Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease }); }
            else { this.BeginAnimation(Window.HeightProperty, new DoubleAnimation { To = HEADER_SIZE, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease }); }
        }

        private void AnimateReveal()
        {
            double d = 250; QuarticEase ease = new() { EasingMode = EasingMode.EaseInOut };
            if (_dockState == DockState.Left) { this.BeginAnimation(Window.WidthProperty, new DoubleAnimation { To = _expandedWidth, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease }); }
            else if (_dockState == DockState.Right) { this.BeginAnimation(Window.WidthProperty, new DoubleAnimation { To = _expandedWidth, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease }); this.BeginAnimation(Window.LeftProperty, new DoubleAnimation { To = _expandedLeft, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease }); }
            else if (_dockState == DockState.Bottom) { this.BeginAnimation(Window.HeightProperty, new DoubleAnimation { To = _expandedHeight, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease }); this.BeginAnimation(Window.TopProperty, new DoubleAnimation { To = _expandedTop, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease }); }
            else { this.BeginAnimation(Window.HeightProperty, new DoubleAnimation { To = _expandedHeight, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease }); }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Keyboard.ClearFocus(); FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), null);
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2) { ToggleRollUp(); return; }
                this.BeginAnimation(Window.LeftProperty, null); this.BeginAnimation(Window.TopProperty, null);
                this.BeginAnimation(Window.WidthProperty, null); this.BeginAnimation(Window.HeightProperty, null);
                _wasRolledUpBeforeDrag = _isRolledUp || _isTemporarilyRevealed;
                if (_wasRolledUpBeforeDrag) { this.Width = _expandedWidth; this.Height = _expandedHeight; this.Left = _expandedLeft; this.Top = _expandedTop; }
                _isRolledUp = false; _isTemporarilyRevealed = false; _isManualDragging = true;
                _manualDragStartMouse = new System.Windows.Point(System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y);
                _manualDragStartLeft = this.Left; _manualDragStartTop = this.Top; HeaderBorder.CaptureMouse();
            }
        }

        private void Header_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isManualDragging)
            {
                PresentationSource? source = PresentationSource.FromVisual(this);
                double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                double mouseDX = (System.Windows.Forms.Cursor.Position.X - _manualDragStartMouse.X) / dpiX;
                double mouseDY = (System.Windows.Forms.Cursor.Position.Y - _manualDragStartMouse.Y) / dpiY;

                double newLeft = _manualDragStartLeft + mouseDX;
                double newTop = _manualDragStartTop + mouseDY;

                double snapThresh = 25;
                double? snappedLeft = null;
                double? snappedTop = null;

                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    double sLeft = screen.WorkingArea.Left / dpiX;
                    double sTop = screen.WorkingArea.Top / dpiY;
                    double sRight = screen.WorkingArea.Right / dpiX;
                    double sBottom = screen.WorkingArea.Bottom / dpiY;

                    if (Math.Abs(newLeft - sLeft) < snapThresh) snappedLeft = sLeft;
                    else if (Math.Abs((newLeft + this.Width) - sRight) < snapThresh) snappedLeft = sRight - this.Width;

                    if (Math.Abs(newTop - sTop) < snapThresh) snappedTop = sTop;
                    else if (Math.Abs((newTop + this.Height) - sBottom) < snapThresh) snappedTop = sBottom - this.Height;
                }

                this.Left = snappedLeft ?? newLeft;
                this.Top = snappedTop ?? newTop;

                DockState oldState = _dockState;
                DetermineDockState();
                if (oldState != _dockState) UpdateDockOrientation();
            }
        }

        private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isManualDragging)
            {
                _isManualDragging = false; HeaderBorder.ReleaseMouseCapture();
                double savedExpandedWidth = _expandedWidth; double savedExpandedHeight = _expandedHeight;
                DetermineDockState(); UpdateDockOrientation();
                _expandedWidth = savedExpandedWidth; _expandedHeight = savedExpandedHeight;

                var helper = new WindowInteropHelper(this);
                var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle).WorkingArea;
                double dpiX = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double dpiY = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                if (_dockState == DockState.Bottom) { _expandedTop = (screen.Bottom / dpiY) - _expandedHeight; _expandedLeft = this.Left; }
                else if (_dockState == DockState.Right) { _expandedLeft = (screen.Right / dpiX) - _expandedWidth; _expandedTop = this.Top; }
                else if (_dockState == DockState.Left) { _expandedLeft = screen.Left / dpiX; _expandedTop = this.Top; }
                else if (_dockState == DockState.Top) { _expandedTop = screen.Top / dpiY; _expandedLeft = this.Left; }
                else { _expandedLeft = this.Left; _expandedTop = this.Top; }

                if (_wasRolledUpBeforeDrag)
                {
                    _isRolledUp = true;
                    if (this.IsMouseOver) { _isTemporarilyRevealed = true; if (_dockState == DockState.Left || _dockState == DockState.Right) this.Width = _expandedWidth; else this.Height = _expandedHeight; this.Left = _expandedLeft; this.Top = _expandedTop; }
                    else { _isTemporarilyRevealed = false; if (_dockState == DockState.Bottom) { this.Height = HEADER_SIZE; this.Top = _expandedTop + (_expandedHeight - HEADER_SIZE); } else if (_dockState == DockState.Right) { this.Width = HEADER_SIZE; this.Left = _expandedLeft + (_expandedWidth - HEADER_SIZE); } else { this.Height = HEADER_SIZE; this.Width = _expandedWidth; this.Left = _expandedLeft; this.Top = _expandedTop; } AnimateRollUp(); }
                }
                else { this.Left = _expandedLeft; this.Top = _expandedTop; }
                SaveFenceState();
                if (Application.Current is App myApp) myApp.SaveCurrentLayout();
            }
        }

        private void ToggleRollUp()
        {
            if (_isRolledUp) { AnimateReveal(); _isRolledUp = false; _isTemporarilyRevealed = false; }
            else { DetermineDockState(); UpdateDockOrientation(); this.BeginAnimation(Window.HeightProperty, null); this.BeginAnimation(Window.WidthProperty, null); this.BeginAnimation(Window.LeftProperty, null); this.BeginAnimation(Window.TopProperty, null); _expandedHeight = this.Height; _expandedWidth = this.Width; _expandedLeft = this.Left; _expandedTop = this.Top; AnimateRollUp(); _isRolledUp = true; _isTemporarilyRevealed = false; }
            SaveFenceState();
        }

        private void StartNativeResize(int direction)
        {
            if (_isRolledUp && !_isTemporarilyRevealed) return;
            this.BeginAnimation(Window.HeightProperty, null); this.BeginAnimation(Window.WidthProperty, null); this.BeginAnimation(Window.LeftProperty, null); this.BeginAnimation(Window.TopProperty, null);

            bool wasRolledUp = _isRolledUp || _isTemporarilyRevealed;
            if (_isRolledUp || _isTemporarilyRevealed) { _isRolledUp = false; _isTemporarilyRevealed = false; }

            NativeMethods.ReleaseCapture();
            var helper = new WindowInteropHelper(this);
            NativeMethods.SendMessage(helper.Handle, NativeMethods.WM_SYSCOMMAND, (IntPtr)(NativeMethods.SC_SIZE + direction), IntPtr.Zero);

            DetermineDockState(); UpdateDockOrientation();
            _expandedLeft = this.Left; _expandedTop = this.Top; _expandedWidth = this.Width; _expandedHeight = this.Height;

            if (wasRolledUp) { _isRolledUp = true; _isTemporarilyRevealed = true; }
            SaveFenceState();
            if (Application.Current is App myApp) myApp.SaveCurrentLayout();
        }

        private void Resize_Left(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(1); }
        private void Resize_Right(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(2); }
        private void Resize_Top(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(3); }
        private void Resize_TopLeft(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(4); }
        private void Resize_TopRight(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(5); }
        private void Resize_Bottom(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(6); }
        private void Resize_BottomLeft(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(7); }
        private void Resize_BottomRight(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(8); }

        private void TitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) return; e.Handled = true; TitleText.Visibility = Visibility.Collapsed;
            TextBox renameBox = new() { Text = TitleText.Text, Width = 140, Height = 22, Background = new SolidColorBrush(Color.FromArgb(51, 0, 0, 0)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromArgb(255, 85, 85, 85)), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Left, FontFamily = TitleText.FontFamily, FontWeight = FontWeights.SemiBold, Padding = new Thickness(4, 0, 0, 0) };
            TitleStack.Children.Add(renameBox);
            Dispatcher.BeginInvoke(new Action(() => { renameBox.Focus(); Keyboard.Focus(renameBox); renameBox.SelectAll(); }), DispatcherPriority.Input);
            renameBox.KeyDown += (s2, e2) => { if (e2.Key == Key.Enter) { TitleText.Text = renameBox.Text; TitleStack.Children.Remove(renameBox); TitleText.Visibility = Visibility.Visible; SaveFenceState(); } else if (e2.Key == Key.Escape) { TitleStack.Children.Remove(renameBox); TitleText.Visibility = Visibility.Visible; } };
            renameBox.LostFocus += (s2, e2) => { if (TitleStack.Children.Contains(renameBox)) { TitleText.Text = renameBox.Text; TitleStack.Children.Remove(renameBox); TitleText.Visibility = Visibility.Visible; SaveFenceState(); } };
        }

        private void SearchIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (SearchBox.Visibility == Visibility.Visible) { SearchBox.Text = ""; SearchBox.Visibility = Visibility.Collapsed; }
            else { SearchBox.Visibility = Visibility.Visible; SearchBox.Focus(); }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { ApplySorting(); }
        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            System.Threading.Tasks.Task.Delay(150).ContinueWith(_ => {
                Dispatcher.Invoke(() => {
                    if (!SearchBox.IsFocused && string.IsNullOrWhiteSpace(SearchBox.Text)) { SearchBox.Visibility = Visibility.Collapsed; if (!this.IsMouseOver && _isRolledUp && _isTemporarilyRevealed) { AnimateRollUp(); _isTemporarilyRevealed = false; } }
                });
            });
        }
        private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { SearchBox.Text = ""; SearchBox.Visibility = Visibility.Collapsed; e.Handled = true; MainGrid.Focus(); } }

        private void Window_MouseEnter(object sender, MouseEventArgs e) { AnimateGhostMode(true); if (_isRolledUp && !_isTemporarilyRevealed) { AnimateReveal(); _isTemporarilyRevealed = true; } }
        private void Window_MouseLeave(object sender, MouseEventArgs e) { if (string.IsNullOrWhiteSpace(SearchBox.Text) && !SearchBox.IsKeyboardFocusWithin) { SearchBox.Visibility = Visibility.Collapsed; } if (_isContextMenuOpen || _isDraggingSelectionBox || SearchBox.IsKeyboardFocusWithin) return; AnimateGhostMode(false); if (_isRolledUp && _isTemporarilyRevealed) { AnimateRollUp(); _isTemporarilyRevealed = false; } }
        private void Window_DragEnter(object sender, DragEventArgs e) { AnimateGhostMode(true); if (_isRolledUp && !_isTemporarilyRevealed) { AnimateReveal(); _isTemporarilyRevealed = true; } }
        private void Window_DragOver(object sender, DragEventArgs e) { AnimateGhostMode(true); if (_isRolledUp && !_isTemporarilyRevealed) { AnimateReveal(); _isTemporarilyRevealed = true; } }
        private void Window_DragLeave(object sender, DragEventArgs e) { Point pt = e.GetPosition(this); if (pt.X <= 2 || pt.Y <= 2 || pt.X >= this.ActualWidth - 2 || pt.Y >= this.ActualHeight - 2) { AnimateGhostMode(false); if (_isRolledUp && _isTemporarilyRevealed) { AnimateRollUp(); _isTemporarilyRevealed = false; } } }

        private void HandleMenuClosed() { _isContextMenuOpen = false; if (!this.IsMouseOver && !_isDraggingSelectionBox) { AnimateGhostMode(false); if (_isRolledUp && _isTemporarilyRevealed) { AnimateRollUp(); _isTemporarilyRevealed = false; } } }
        private void MenuIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { e.Handled = true; _isContextMenuOpen = true; HeaderContextMenu.PlacementTarget = sender as UIElement ?? HeaderBorder; HeaderContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; HeaderContextMenu.IsOpen = true; }

        private void Menu_AutoOrganize_Click(object sender, RoutedEventArgs e) { AutoOrganize(); }

        private void AutoOrganize()
        {
            if (string.IsNullOrWhiteSpace(AutoSortExtensions)) { MessageBox.Show("Please set up Auto-Sort extensions in Settings first (e.g. .jpg, .png).", "Auto Organize", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string[] files = Directory.GetFiles(desktopPath);
                string[] extensions = AutoSortExtensions.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
                bool moved = false;
                foreach (string file in files) { string ext = Path.GetExtension(file); foreach (string target in extensions) { if (string.Equals(ext, target, StringComparison.OrdinalIgnoreCase) || string.Equals(ext, "." + target, StringComparison.OrdinalIgnoreCase)) { try { string vaultedPath = MoveToVault(file); if (!_currentFiles.Contains(vaultedPath)) { _currentFiles.Add(vaultedPath); moved = true; } } catch { } break; } } }
                if (moved) { ApplySorting(); SaveFenceState(); MessageBox.Show("Desktop files organized successfully into vault!", "Auto Organize", MessageBoxButton.OK, MessageBoxImage.Information); } else MessageBox.Show("No new files found on the desktop matching your extensions.", "Auto Organize", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show($"Error organizing files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void Menu_NewFence_Click(object sender, RoutedEventArgs e) { MainWindow newFence = new(Guid.NewGuid().ToString()) { Left = this.Left + 50, Top = this.Top + 50 }; newFence.Show(); }
        private void Menu_DeleteFence_Click(object sender, RoutedEventArgs e) { if (MessageBox.Show("Permanently delete this fence?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) DashboardDelete(); }
        private void Menu_RollUp_Click(object sender, RoutedEventArgs e) { ToggleRollUp(); }
        private void Menu_Sort_Click(object sender, RoutedEventArgs e) { if (sender is not MenuItem clickedSort || clickedSort.Tag is null) return; _saveSortMethod = clickedSort.Tag.ToString() ?? "None"; ApplySorting(); SaveFenceState(); }
        private void Menu_IconSize_Click(object sender, RoutedEventArgs e) { if (sender is not MenuItem clickedItem || clickedItem.Tag is null) return; string tagStr = clickedItem.Tag.ToString() ?? ""; if (tagStr == "Default") { CurrentIconSize = 48; } else if (double.TryParse(tagStr, out double newSize)) { CurrentIconSize = newSize; } ApplySorting(); SaveFenceState(); }
        private void Menu_Settings_Click(object sender, RoutedEventArgs e) { SettingsWindow settings = new(this) { Owner = this }; settings.ShowDialog(); }
        private void Menu_EditColor_Click(object sender, RoutedEventArgs e) { SettingsWindow settings = new(this) { Owner = this }; settings.MainTabControl.SelectedIndex = 4; settings.ShowDialog(); }

        private void Menu_NewPortal_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Select a folder to mirror in the Portal Fence" };
            if (dialog.ShowDialog() == true) { MainWindow portalFence = new(Guid.NewGuid().ToString()) { Left = this.Left + 50, Top = this.Top + 50 }; portalFence.MakePortal(dialog.FolderName); portalFence.Show(); }
        }

        public void MakePortal(string path) { _isPortal = true; _portalPath = path; TitleText.Text = new DirectoryInfo(path).Name + " (Portal)"; SetupPortalWatcher(); SaveFenceState(); }

        private async void SetupPortalWatcher()
        {
            if (!Directory.Exists(_portalPath)) return;
            try
            {
                string[] files = await Task.Run(() => Directory.GetFiles(_portalPath));
                _currentFiles.Clear();
                _currentFiles.AddRange(files);
                ApplySorting();
            }
            catch { }

            try
            {
                _portalWatcher = new FileSystemWatcher(_portalPath) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName, EnableRaisingEvents = true };
                _portalWatcher.Created += Portal_FileChanged; _portalWatcher.Deleted += Portal_FileChanged; _portalWatcher.Renamed += Portal_FileChanged;

                _portalWatcher.Error += (s, e) => {
                    Dispatcher.Invoke(() => {
                        try { _portalWatcher?.Dispose(); } catch { }
                        _portalWatcher = null;
                        DispatcherTimer retryTimer = new();
                        retryTimer.Interval = TimeSpan.FromSeconds(5);
                        retryTimer.Tick += (senderTimer, args) => {
                            if (Directory.Exists(_portalPath))
                            {
                                retryTimer.Stop();
                                SetupPortalWatcher();
                            }
                        };
                        retryTimer.Start();
                    });
                };
            }
            catch { }
        }

        private void Portal_FileChanged(object sender, FileSystemEventArgs e) { Dispatcher.InvokeAsync(() => { _portalDebounceTimer.Stop(); _portalDebounceTimer.Start(); }, DispatcherPriority.Background); }

        private async void RefreshPortalUI()
        {
            if (!Directory.Exists(_portalPath)) return;
            try
            {
                string[] newFiles = await Task.Run(() => Directory.GetFiles(_portalPath));

                var currentSet = new HashSet<string>(_currentFiles, StringComparer.OrdinalIgnoreCase);
                var newSet = new HashSet<string>(newFiles, StringComparer.OrdinalIgnoreCase);

                var toAdd = newFiles.Where(f => !currentSet.Contains(f)).ToList();
                var toRemove = _currentFiles.Where(f => !newSet.Contains(f)).ToList();

                if (toAdd.Count == 0 && toRemove.Count == 0) return;

                foreach (var f in toRemove)
                {
                    _currentFiles.Remove(f);
                    var panel = IconPanel.Children.OfType<StackPanel>().FirstOrDefault(p => string.Equals(p.ToolTip?.ToString(), f, StringComparison.OrdinalIgnoreCase));
                    if (panel != null) IconPanel.Children.Remove(panel);
                }

                foreach (var f in toAdd)
                {
                    _currentFiles.Add(f);
                    AddIconToUI(f);
                }

                if (_saveSortMethod != "None" && _saveSortMethod != "Custom" && _saveSortMethod != "DateAdd")
                {
                    ApplySorting();
                }
            }
            catch { }
        }

        private string MoveToVault(string originalPath)
        {
            string storageDir = Path.Combine(App.ConfigFolder, "Storage", FenceId);
            Directory.CreateDirectory(storageDir);
            if (Path.GetDirectoryName(originalPath) == storageDir) return originalPath;

            string fileName = Path.GetFileName(originalPath);
            string newPath = Path.Combine(storageDir, fileName);
            int counter = 1;

            while (File.Exists(newPath) || Directory.Exists(newPath))
            {
                string nameOnly = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                newPath = Path.Combine(storageDir, $"{nameOnly} ({counter}){ext}");
                counter++;
            }

            try
            {
                string sourceRoot = Path.GetPathRoot(originalPath) ?? "";
                string destRoot = Path.GetPathRoot(newPath) ?? "";

                if (sourceRoot.Equals(destRoot, StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(originalPath)) Directory.Move(originalPath, newPath);
                    else if (File.Exists(originalPath)) File.Move(originalPath, newPath);
                }
                else
                {
                    NativeMethods.SHFILEOPSTRUCT shf = new()
                    {
                        hwnd = new WindowInteropHelper(this).Handle,
                        wFunc = 0x0001, // FO_MOVE
                        pFrom = originalPath + '\0' + '\0',
                        pTo = newPath + '\0' + '\0',
                        fFlags = 0x0000
                    };
                    int res = NativeMethods.SHFileOperation(ref shf);
                    if (res != 0) return originalPath;
                }
            }
            catch (Exception ex)
            {
                LogError("MoveToVault", ex);
                return originalPath;
            }

            return newPath;
        }

        private void SendToRecycleBin(string path)
        {
            try { if (!File.Exists(path) && !Directory.Exists(path)) return; NativeMethods.SHFILEOPSTRUCT shf = new() { hwnd = new WindowInteropHelper(this).Handle, wFunc = 0x0003, pFrom = path + '\0' + '\0', fFlags = 0x0040 | 0x0010 | 0x0004 }; NativeMethods.SHFileOperation(ref shf); } catch (Exception ex) { LogError($"Recycle Bin Error: {path}", ex); }
        }

        private void RemoveFileFromVaultAndList(string filePath) { try { SendToRecycleBin(filePath); } catch { } _currentFiles.Remove(filePath); }

        private void ApplySorting()
        {
            if (_saveSortMethod != "None" && _saveSortMethod != "DateAdd" && _saveSortMethod != "Custom")
            {
                try
                {
                    switch (_saveSortMethod)
                    {
                        case "Name": _currentFiles.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase)); break;
                        case "Size": _currentFiles.Sort((a, b) => GetFileSize(b).CompareTo(GetFileSize(a))); break;
                        case "Type": _currentFiles.Sort((a, b) => string.Compare(Path.GetExtension(a), Path.GetExtension(b), StringComparison.OrdinalIgnoreCase)); break;
                        case "DateMod": _currentFiles.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a))); break;
                        case "DateCre": _currentFiles.Sort((a, b) => File.GetCreationTime(b).CompareTo(File.GetCreationTime(a))); break;
                    }
                }
                catch { }
            }
            RefreshIconUI();
        }

        private static long GetFileSize(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }

        public void RefreshIconUI()
        {
            var existingPanels = IconPanel.Children.OfType<StackPanel>().ToList();
            IconPanel.Children.Clear();
            ClearSelection();

            if (_currentFiles.Count == 0)
            {
                IconPanel.Children.Add(new TextBlock { Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#88FFFFFF")!, Text = "Drop shortcuts here..." });
                return;
            }

            string searchQuery = SearchBox.Text;
            foreach (string file in _currentFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrWhiteSpace(searchQuery) && !fileName.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)) continue;

                var panel = existingPanels.FirstOrDefault(p => string.Equals(p.ToolTip?.ToString(), file, StringComparison.OrdinalIgnoreCase));

                if (panel != null && panel.Width == CurrentIconSize + 24)
                {
                    IconPanel.Children.Add(panel);
                }
                else
                {
                    AddIconToUI(file);
                }
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Handled) return;
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                string? sourceId = e.Data.GetData("FenceSourceId") as string; bool isInternal = (sourceId == FenceId); int insertIndex = _currentFiles.Count; Point dropPoint = e.GetPosition(IconPanel);
                for (int i = 0; i < IconPanel.Children.Count; i++) { if (IconPanel.Children[i] is FrameworkElement child) { Point childPos = child.TranslatePoint(new Point(0, 0), IconPanel); if (dropPoint.Y >= childPos.Y && dropPoint.Y <= childPos.Y + child.ActualHeight) { if (dropPoint.X < childPos.X + (child.ActualWidth / 2)) { insertIndex = i; break; } } else if (dropPoint.Y < childPos.Y) { insertIndex = i; break; } } }
                string? targetPath = null; if (insertIndex < _currentFiles.Count) targetPath = _currentFiles[insertIndex];
                if (isInternal) { foreach (string f in files) _currentFiles.Remove(f); int finalIndex = _currentFiles.Count; if (targetPath is not null) { finalIndex = _currentFiles.IndexOf(targetPath); if (finalIndex < 0) finalIndex = _currentFiles.Count; } foreach (string f in files) { _currentFiles.Insert(finalIndex, f); finalIndex++; } _saveSortMethod = "Custom"; ApplySorting(); SaveFenceState(); e.Effects = DragDropEffects.None; e.Handled = true; return; }
                List<string> processedFiles = [];
                foreach (string file in files) { try { if (!_isPortal) { string vaultedPath = MoveToVault(file); if (!_currentFiles.Contains(vaultedPath)) processedFiles.Add(vaultedPath); } else { string fileName = Path.GetFileName(file); string dest = Path.Combine(_portalPath, fileName); if (file != dest && !File.Exists(dest) && !Directory.Exists(dest)) { if (Directory.Exists(file)) Directory.Move(file, dest); else File.Move(file, dest); } if (!_currentFiles.Contains(dest)) processedFiles.Add(dest); } } catch { if (!_currentFiles.Contains(file)) processedFiles.Add(file); } }
                int extFinalIndex = _currentFiles.Count; if (targetPath is not null) { extFinalIndex = _currentFiles.IndexOf(targetPath); if (extFinalIndex < 0) extFinalIndex = _currentFiles.Count; }
                foreach (string pf in processedFiles) { _currentFiles.Insert(extFinalIndex, pf); extFinalIndex++; }
                _saveSortMethod = "Custom"; ApplySorting(); SaveFenceState();
                if (!string.IsNullOrEmpty(sourceId) && sourceId != FenceId) e.Effects = DragDropEffects.Move; else e.Effects = DragDropEffects.None;
            }
        }

        private async void AddIconToUI(string file)
        {
            string currentFilePath = file; double containerWidth = CurrentIconSize + 24;
            StackPanel itemContainer = new() { Orientation = Orientation.Vertical, Margin = new Thickness(8), Width = containerWidth, ToolTip = currentFilePath, Background = System.Windows.Media.Brushes.Transparent, AllowDrop = true };
            double imageHeight = CurrentIconSize;
            System.Windows.Controls.Image iconImage = new() { Width = CurrentIconSize, Height = imageHeight, Margin = new Thickness(0, 0, 0, 5), SnapsToDevicePixels = true, UseLayoutRounding = true, Stretch = Stretch.Uniform };
            Border imageBorder = new Border { CornerRadius = new CornerRadius(4), BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), BorderThickness = new Thickness(0), Child = iconImage, ClipToBounds = true };
            TextBlock textBlock = new() { Text = Path.GetFileNameWithoutExtension(currentFilePath), Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 35 };
            itemContainer.Children.Add(imageBorder); itemContainer.Children.Add(textBlock); IconPanel.Children.Add(itemContainer);

            try
            {
                ImageSource? fetchedSource = null;

                await _iconSemaphore.WaitAsync();
                try
                {
                    fetchedSource = await System.Threading.Tasks.Task.Run(() => GetHighResIcon(currentFilePath));
                }
                finally
                {
                    _iconSemaphore.Release();
                }

                if (fetchedSource is not null) { iconImage.Source = fetchedSource; RenderOptions.SetBitmapScalingMode(iconImage, BitmapScalingMode.HighQuality); }
            }
            catch (Exception ex) { LogError($"Async Icon Failure: {currentFilePath}", ex); }

            ContextMenu rightClickMenu = new(); rightClickMenu.Opened += ContextMenu_Opened_ApplyBlur; rightClickMenu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(Submenu_Opened_ApplyBlur));
            MenuItem renameItem = new() { Header = "Rename File" }; MenuItem removeItem = new() { Header = "Remove from Fence" };
            removeItem.Click += (s, args) => { if (_isPortal) { if (MessageBox.Show("This is a Folder Portal.\n\nDeleting items here will send the actual files on your computer to the Recycle Bin. Are you sure you want to delete?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; } if (_selectedItems.Count > 1 && _selectedItems.Contains(itemContainer)) { foreach (StackPanel selectedPanel in _selectedItems.ToList()) { if (selectedPanel.ToolTip is string path) RemoveFileFromVaultAndList(path); } } else { RemoveFileFromVaultAndList(currentFilePath); } ClearSelection(); ApplySorting(); SaveFenceState(); };

            void TriggerRename()
            {
                textBlock.Visibility = Visibility.Collapsed;
                TextBox renameBox = new() { Text = textBlock.Text, Width = containerWidth, Margin = new Thickness(-5, 0, -5, 0), Background = new SolidColorBrush(Color.FromArgb(51, 0, 0, 0)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromArgb(255, 85, 85, 85)), BorderThickness = new Thickness(1), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(2) };
                itemContainer.Children.Add(renameBox);
                renameBox.Focus();
                renameBox.SelectAll();
                bool isHandlingRename = false;

                void CommitRename()
                {
                    if (isHandlingRename) return;
                    isHandlingRename = true;
                    try
                    {
                        string? dir = Path.GetDirectoryName(currentFilePath);
                        string ext = Path.GetExtension(currentFilePath);
                        if (dir is not null)
                        {
                            string newPath = Path.Combine(dir, renameBox.Text + ext);
                            if (currentFilePath != newPath && !File.Exists(newPath) && !Directory.Exists(newPath))
                            {
                                if (Directory.Exists(currentFilePath)) Directory.Move(currentFilePath, newPath);
                                else File.Move(currentFilePath, newPath);
                                _currentFiles.Remove(currentFilePath);
                                _currentFiles.Add(newPath);
                                ApplySorting();
                                SaveFenceState();
                                return;
                            }
                        }
                    }
                    catch { }

                    if (itemContainer.Children.Contains(renameBox))
                    {
                        itemContainer.Children.Remove(renameBox);
                        textBlock.Visibility = Visibility.Visible;
                    }
                }

                renameBox.KeyDown += (s2, e2) => { if (e2.Key == Key.Enter) { CommitRename(); } else if (e2.Key == Key.Escape) { isHandlingRename = true; if (itemContainer.Children.Contains(renameBox)) { itemContainer.Children.Remove(renameBox); textBlock.Visibility = Visibility.Visible; } } };
                renameBox.LostFocus += (s2, e2) => { CommitRename(); };
            }

            renameItem.Click += (s, args) => TriggerRename(); rightClickMenu.Items.Add(renameItem); rightClickMenu.Items.Add(removeItem); itemContainer.ContextMenu = rightClickMenu; rightClickMenu.Opened += (s, args) => _isContextMenuOpen = true; rightClickMenu.Closed += (s, args) => HandleMenuClosed();
            textBlock.MouseLeftButtonDown += (s, args) => { if (args.ClickCount == 2) return; args.Handled = true; TriggerRename(); };
            System.Windows.Point? dragStartPoint = null;

            itemContainer.PreviewMouseLeftButtonDown += (s, args) => { Keyboard.ClearFocus(); FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), null); dragStartPoint = args.GetPosition(null); bool isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl); if (isCtrlPressed) { if (_selectedItems.Contains(itemContainer)) { itemContainer.Background = System.Windows.Media.Brushes.Transparent; _selectedItems.Remove(itemContainer); } else { itemContainer.Background = _highlightBrush; _selectedItems.Add(itemContainer); } } else if (!_selectedItems.Contains(itemContainer)) { ClearSelection(); itemContainer.Background = _highlightBrush; _selectedItems.Add(itemContainer); } };
            itemContainer.PreviewMouseRightButtonDown += (s, args) => { if (!_selectedItems.Contains(itemContainer)) { ClearSelection(); itemContainer.Background = _highlightBrush; _selectedItems.Add(itemContainer); } };
            itemContainer.PreviewMouseMove += (s, args) => { if (args.LeftButton == MouseButtonState.Pressed && dragStartPoint.HasValue) { System.Windows.Point currentPoint = args.GetPosition(null); if (Math.Abs(currentPoint.X - dragStartPoint.Value.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(currentPoint.Y - dragStartPoint.Value.Y) > SystemParameters.MinimumVerticalDragDistance) { string[] draggedFiles; if (_selectedItems.Contains(itemContainer) && _selectedItems.Count > 1) { draggedFiles = _selectedItems.Select(panel => panel.ToolTip.ToString()!).ToArray(); } else { draggedFiles = [currentFilePath]; } DataObject dragData = new(DataFormats.FileDrop, draggedFiles); dragData.SetData("FenceSourceId", FenceId); DragDropEffects result = DragDrop.DoDragDrop(itemContainer, dragData, DragDropEffects.Move | DragDropEffects.Copy); if (result == DragDropEffects.Move) { foreach (string f in draggedFiles) _currentFiles.Remove(f); ClearSelection(); ApplySorting(); SaveFenceState(); } dragStartPoint = null; } } };
            itemContainer.MouseDown += (s, args) => { if (args.ClickCount == 2 && args.ChangedButton == MouseButton.Left) { if (File.Exists(currentFilePath) || Directory.Exists(currentFilePath)) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = currentFilePath, UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show($"Could not open file.\nError: {ex.Message}", "Launch Error"); } } else { MessageBox.Show("This file or folder no longer exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning); _currentFiles.Remove(currentFilePath); RefreshIconUI(); SaveFenceState(); } } };
        }

        private static string ResolveShortcutTarget(string shortcutPath)
        {
            try { Type? shellType = Type.GetTypeFromProgID("WScript.Shell"); if (shellType is not null) { dynamic? shell = Activator.CreateInstance(shellType); if (shell is not null) { var shortcut = shell.CreateShortcut(shortcutPath); string iconLocation = shortcut.IconLocation; if (!string.IsNullOrEmpty(iconLocation)) { string[] parts = iconLocation.Split(','); if (parts.Length > 0 && File.Exists(parts[0])) return parts[0]; } string targetPath = shortcut.TargetPath; if (File.Exists(targetPath) || Directory.Exists(targetPath)) return targetPath; } } } catch (Exception ex) { LogError($"Shortcut Resolve Failed: {shortcutPath}", ex); }
            return shortcutPath;
        }

        private static ImageSource? GetHighResIcon(string filePath)
        {
            if (!File.Exists(filePath) && !Directory.Exists(filePath)) return null;
            string ext = Path.GetExtension(filePath);
            if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) { filePath = ResolveShortcutTarget(filePath); ext = Path.GetExtension(filePath); }
            if (_imageExtensions.Contains(ext)) { try { BitmapImage bitmap = new(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 256; bitmap.UriSource = new Uri(filePath, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze(); return bitmap; } catch (Exception ex) { LogError($"Native Image Loader Failed: {filePath}", ex); } }

            try
            {
                Guid iid = new("bcc18b79-ba16-442f-80c4-8a59c30d463b");
                int hr = NativeMethods.SHCreateItemFromParsingName(filePath, IntPtr.Zero, in iid, out NativeMethods.IShellItemImageFactory factory);
                if (hr == 0 && factory is not null)
                {
                    IntPtr hbitmap = IntPtr.Zero;
                    try
                    {
                        try { factory.GetImage(new NativeMethods.NativeSize { Width = 256, Height = 256 }, 0x0, out hbitmap); } catch { }
                        if (hbitmap == IntPtr.Zero) { try { factory.GetImage(new NativeMethods.NativeSize { Width = 256, Height = 256 }, 0x4, out hbitmap); } catch { } }

                        if (hbitmap != IntPtr.Zero)
                        {
                            ImageSource imgSrc = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            imgSrc.Freeze();
                            return imgSrc;
                        }
                    }
                    finally
                    {
                        if (hbitmap != IntPtr.Zero) NativeMethods.DeleteObject(hbitmap);
                    }
                }
            }
            catch (Exception ex) { LogError($"Shell API Factory Failed: {filePath}", ex); }

            if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)) { try { uint sizeRequest = (uint)((16 << 16) | 256); int res = NativeMethods.SHDefExtractIcon(filePath, 0, 0, out IntPtr hIconLarge, out IntPtr hIconSmall, sizeRequest); if (res == 0 && hIconLarge != IntPtr.Zero) { ImageSource imgSrc = Imaging.CreateBitmapSourceFromHIcon(hIconLarge, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); imgSrc.Freeze(); NativeMethods.DestroyIcon(hIconLarge); if (hIconSmall != IntPtr.Zero) NativeMethods.DestroyIcon(hIconSmall); return imgSrc; } } catch (Exception ex) { LogError($"Binary Extraction Failed: {filePath}", ex); } }
            try { NativeMethods.SHFILEINFO shinfo = new(); IntPtr result = NativeMethods.SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), 0x000000100 | 0x000000000); if (result != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero) { ImageSource imgSrc = Imaging.CreateBitmapSourceFromHIcon(shinfo.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); imgSrc.Freeze(); NativeMethods.DestroyIcon(shinfo.hIcon); return imgSrc; } } catch (Exception ex) { LogError($"Win32 Fallback Failed: {filePath}", ex); }
            return null;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string globalConfigPath = Path.Combine(App.ConfigFolder, "global_config.json");
            bool showInTaskbar = true;

            if (File.Exists(globalConfigPath))
            {
                try { string json = await File.ReadAllTextAsync(globalConfigPath); if (JsonSerializer.Deserialize<GlobalConfig>(json) is GlobalConfig config) { showInTaskbar = config.ShowTaskbarIcon; _globalGhostMode = config.EnableGhostMode; } } catch { }
            }

            await LoadFenceStateAsync(); ApplyAcrylicBlur(_currentFenceColor, _currentOpacity);

            var helper = new WindowInteropHelper(this); IntPtr myWindowHandle = helper.Handle; IntPtr exStyle = NativeMethods.GetWindowLongPtr(myWindowHandle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLongPtr(myWindowHandle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | NativeMethods.WS_EX_TOOLWINDOW));
            NativeMethods.SetWindowPos(myWindowHandle, (IntPtr)NativeMethods.HWND_BOTTOM, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            if (HwndSource.FromHwnd(myWindowHandle) is HwndSource source) source.AddHook(new HwndSourceHook(WndProc));

            this.PreviewKeyDown += Window_PreviewKeyDown; this.PreviewMouseDown += Window_PreviewMouseDown;
            IconPanel.MouseRightButtonDown += (s, args) => { if (args.OriginalSource is ScrollViewer || args.OriginalSource is WrapPanel || args.OriginalSource == IconPanel) { ClearSelection(); _isContextMenuOpen = true; HeaderContextMenu.PlacementTarget = IconPanel; HeaderContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint; HeaderContextMenu.IsOpen = true; args.Handled = true; } };

            if (MenuRollUpFence is not null) MenuRollUpFence.IsCheckable = true;

            HeaderContextMenu.Opened += (s, args) => { _isContextMenuOpen = true; if (MenuRollUpFence is not null) MenuRollUpFence.IsChecked = _isRolledUp; };
            HeaderContextMenu.Closed += (s, args) => HandleMenuClosed();
            this.PreviewMouseRightButtonDown += (s, args) => _isContextMenuOpen = true;
            if (SearchBox is not null && MainGrid is not null) { SearchBox.KeyDown += SearchBox_KeyDown; }

            if (!File.Exists(globalConfigPath)) { GlobalConfig newConfig = new() { FirstRunComplete = true, ShowTaskbarIcon = true, RestoreFilesOnDelete = true, EnableGhostMode = false }; File.WriteAllText(globalConfigPath, JsonSerializer.Serialize(newConfig, _jsonOptions)); SettingsWindow settings = new(this, true) { Owner = this }; settings.ShowDialog(); }
            this.ShowInTaskbar = showInTaskbar; if (!this.IsMouseOver) AnimateGhostMode(false);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_WINDOWPOSCHANGING)
            {
                NativeMethods.WINDOWPOS windowPos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam)!;
                windowPos.hwndInsertAfter = (IntPtr)NativeMethods.HWND_BOTTOM;
                if ((windowPos.flags & NativeMethods.SWP_NOMOVE) == 0 && (windowPos.flags & NativeMethods.SWP_NOSIZE) != 0)
                {
                    int snapMargin = 20;
                    foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                    {
                        var workArea = screen.WorkingArea;
                        if (Math.Abs(windowPos.x - workArea.Left) < snapMargin) windowPos.x = workArea.Left;
                        else if (Math.Abs((windowPos.x + windowPos.cx) - workArea.Right) < snapMargin) windowPos.x = workArea.Right - windowPos.cx;

                        if (Math.Abs(windowPos.y - workArea.Top) < snapMargin) windowPos.y = workArea.Top;
                        else if (Math.Abs((windowPos.y + windowPos.cy) - workArea.Bottom) < snapMargin) windowPos.y = workArea.Bottom - windowPos.cy;
                    }
                }
                Marshal.StructureToPtr(windowPos, lParam, true);
            }
            if (msg == NativeMethods.WM_SETTINGCHANGE && wParam.ToInt32() == NativeMethods.SPI_SETDESKWALLPAPER) { if (_autoMatchColor) { Color newColor = ThemeUtility.GetDominantWallpaperColor(); SetFenceColor(newColor, _currentOpacity); } }
            return IntPtr.Zero;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && _selectedItems.Count > 0 && e.OriginalSource is not TextBox) { if (_isPortal) { if (MessageBox.Show("This is a Folder Portal.\n\nDeleting items here will send the actual files on your computer to the Recycle Bin. Are you sure you want to delete?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; } foreach (StackPanel selectedPanel in _selectedItems.ToList()) { if (selectedPanel.ToolTip is string path) RemoveFileFromVaultAndList(path); } ClearSelection(); ApplySorting(); SaveFenceState(); e.Handled = true; return; }
            if (Keyboard.Modifiers == ModifierKeys.Control) { var helper = new WindowInteropHelper(this); var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle); var workArea = screen.WorkingArea; PresentationSource? source = PresentationSource.FromVisual(this); double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0; double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0; double logicalLeft = workArea.Left / dpiX; double logicalTop = workArea.Top / dpiY; double logicalRight = workArea.Right / dpiX; double logicalBottom = workArea.Bottom / dpiY; bool wasHandled = false; this.BeginAnimation(Window.LeftProperty, null); this.BeginAnimation(Window.TopProperty, null); this.BeginAnimation(Window.WidthProperty, null); this.BeginAnimation(Window.HeightProperty, null); if (_isRolledUp) { _isRolledUp = false; _isTemporarilyRevealed = false; this.Width = _expandedWidth; this.Height = _expandedHeight; } if (e.Key == Key.Up) { this.Top = logicalTop; wasHandled = true; } else if (e.Key == Key.Down) { this.Top = logicalBottom - this.Height; wasHandled = true; } else if (e.Key == Key.Left) { this.Left = logicalLeft; wasHandled = true; } else if (e.Key == Key.Right) { this.Left = logicalRight - this.Width; wasHandled = true; } if (wasHandled) { e.Handled = true; DetermineDockState(); UpdateDockOrientation(); _expandedLeft = this.Left; _expandedTop = this.Top; _expandedWidth = this.Width; _expandedHeight = this.Height; SaveFenceState(); } }
        }

        protected override async void OnClosed(EventArgs e)
        {
            _iconSemaphore.Dispose();
            base.OnClosed(e);
        }

        protected override async void OnClosing(System.ComponentModel.CancelEventArgs e) { if (!_isDeleted) await PerformDiskWriteAsync(); base.OnClosing(e); }
        private void ClearSelection() { foreach (var item in _selectedItems) item.Background = System.Windows.Media.Brushes.Transparent; _selectedItems.Clear(); }
        private void IconPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { Keyboard.ClearFocus(); FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), null); if (e.OriginalSource is ScrollViewer || e.OriginalSource is WrapPanel) { ClearSelection(); _selectionStartPoint = e.GetPosition(SelectionCanvas); _isDraggingSelectionBox = true; SelectionBox.Visibility = Visibility.Visible; SelectionBox.Width = 0; SelectionBox.Height = 0; Canvas.SetLeft(SelectionBox, _selectionStartPoint.X); Canvas.SetTop(SelectionBox, _selectionStartPoint.Y); IconPanel.CaptureMouse(); } }
        private void IconPanel_MouseMove(object sender, MouseEventArgs e) { if (_isDraggingSelectionBox) { System.Windows.Point currentPoint = e.GetPosition(SelectionCanvas); double x = Math.Min(currentPoint.X, _selectionStartPoint.X); double y = Math.Min(currentPoint.Y, _selectionStartPoint.Y); double width = Math.Abs(currentPoint.X - _selectionStartPoint.X); double height = Math.Abs(currentPoint.Y - _selectionStartPoint.Y); Canvas.SetLeft(SelectionBox, x); Canvas.SetTop(SelectionBox, y); SelectionBox.Width = width; SelectionBox.Height = height; Rect selectionRect = new(x, y, width, height); foreach (UIElement child in IconPanel.Children) { if (child is StackPanel item) { System.Windows.Point itemPos = item.TranslatePoint(new System.Windows.Point(0, 0), SelectionCanvas); Rect itemRect = new(itemPos, new Size(item.ActualWidth, item.ActualHeight)); if (selectionRect.IntersectsWith(itemRect)) { if (!_selectedItems.Contains(item)) { item.Background = _highlightBrush; _selectedItems.Add(item); } } else if (_selectedItems.Contains(item)) { item.Background = System.Windows.Media.Brushes.Transparent; _selectedItems.Remove(item); } } } } }
        private void IconPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { if (_isDraggingSelectionBox) { _isDraggingSelectionBox = false; SelectionBox.Visibility = Visibility.Collapsed; IconPanel.ReleaseMouseCapture(); } }
    }
}