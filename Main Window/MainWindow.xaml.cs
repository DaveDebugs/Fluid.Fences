using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Linq;

namespace DesktopFences
{
    public partial class MainWindow : Window
    {
        #region Native Win32 APIs

        internal enum AccentState { ACCENT_DISABLED = 0, ACCENT_ENABLE_GRADIENT = 1, ACCENT_ENABLE_TRANSPARENTGRADIENT = 2, ACCENT_ENABLE_BLURBEHIND = 3, ACCENT_ENABLE_ACRYLICBLURBEHIND = 4, ACCENT_INVALID_STATE = 5 }
        [StructLayout(LayoutKind.Sequential)] internal struct AccentPolicy { public AccentState AccentState; public uint AccentFlags; public uint GradientColor; public uint AnimationId; }
        [StructLayout(LayoutKind.Sequential)] internal struct WindowCompositionAttributeData { public WindowCompositionAttribute Attribute; public IntPtr Data; public int SizeOfData; }
        internal enum WindowCompositionAttribute { WCA_ACCENT_POLICY = 19 }
        internal enum DWMWINDOWATTRIBUTE { DWMWA_WINDOW_CORNER_PREFERENCE = 33 }
        internal enum DWM_WINDOW_CORNER_PREFERENCE { DWMWCP_DEFAULT = 0, DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3 }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [LibraryImport("dwmapi.dll")] internal static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        [LibraryImport("user32.dll")] internal static partial int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")] private static partial IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static partial bool ReleaseCapture();

        [GeneratedComInterface, Guid("bcc18b79-ba16-442f-80c4-8a59c30d463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal partial interface IShellItemImageFactory { void GetImage(in NativeSize size, int flags, out IntPtr phbm); }

        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, in Guid riid, out IShellItemImageFactory ppv);

        [StructLayout(LayoutKind.Sequential)] internal struct NativeSize { public int Width; public int Height; }
        [LibraryImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static partial bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern int SHDefExtractIcon(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential)] public struct WINDOWPOS { public IntPtr hwnd; public IntPtr hwndInsertAfter; public int x; public int y; public int cx; public int cy; public uint flags; }
        #endregion

        private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp", ".ico" };

        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_SIZE = 0xF000;
        private const int HWND_BOTTOM = 1;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int WM_WINDOWPOSCHANGING = 0x0046;

        public enum DockState { Floating, Top, Bottom, Left, Right }

        private readonly System.Windows.Threading.DispatcherTimer _saveTimer = new();
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly List<string> _currentFiles = new();
        private readonly List<StackPanel> _selectedItems = new();
        private readonly SolidColorBrush _highlightBrush = new(System.Windows.Media.Color.FromArgb(85, 0, 120, 215));

        private readonly string _saveDirectory = string.Empty;
        private readonly string _saveFilePath = string.Empty;
        private readonly string _fenceId = string.Empty;

        private bool _isRolledUp = false;
        private double _expandedHeight = 300;
        private double _expandedWidth = 250;
        private double _expandedLeft = 0;
        private double _expandedTop = 0;
        private const double HEADER_SIZE = 35;

        private DockState _dockState = DockState.Floating;
        private bool _isTemporarilyRevealed = false;
        private string _saveSortMethod = "None";
        private bool _isContextMenuOpen = false;
        private bool _isDeleted = false;

        private Color _currentFenceColor = Colors.Black;
        private double _currentOpacity = 0.7;
        private double _currentIconSize = 48;

        private System.Windows.Point _selectionStartPoint;
        private bool _isDraggingSelectionBox = false;
        private bool _isManualDragging = false;
        private System.Windows.Point _manualDragStartMouse;
        private double _manualDragStartLeft;
        private double _manualDragStartTop;
        private bool _wasRolledUpBeforeDrag = false;

        [System.Text.Json.Serialization.JsonIgnore] public string FenceTitle { get => TitleText.Text; set => TitleText.Text = value; }
        [System.Text.Json.Serialization.JsonIgnore] public string FenceSortMethod { get => _saveSortMethod; set => _saveSortMethod = value; }
        [System.Text.Json.Serialization.JsonIgnore] public string AutoSortExtensions { get; set; } = "";
        [System.Text.Json.Serialization.JsonIgnore] public bool ShowSearch { get; set; } = true;
        [System.Text.Json.Serialization.JsonIgnore] public Color FenceColor { get => _currentFenceColor; }
        [System.Text.Json.Serialization.JsonIgnore] public double FenceOpacity { get => _currentOpacity; }

        public void SetFenceColor(Color color, double opacity) { ApplyAcrylicBlur(color, opacity); SaveFenceState(); }
        public void DashboardSaveAndRefresh() { ApplySorting(); SaveFenceState(); }
        public void DashboardDelete() { _isDeleted = true; if (File.Exists(_saveFilePath)) File.Delete(_saveFilePath); this.Close(); }
        public void AddFileFromAutoSort(string filePath) { if (!_currentFiles.Contains(filePath)) _currentFiles.Add(filePath); }

        public MainWindow()
        {
            InitializeComponent();
            _fenceId = "DESIGNER";
            _saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences", "Fences");
            _saveFilePath = Path.Combine(_saveDirectory, "designer.json");

            _saveTimer.Interval = TimeSpan.FromSeconds(1.5);
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); PerformDiskWrite(); };
        }

        public MainWindow(string fenceId) : this()
        {
            _fenceId = fenceId;
            _saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences", "Fences");
            _saveFilePath = Path.Combine(_saveDirectory, $"{_fenceId}.json");
        }

        #region Architecture: Diagnostics & Logging
        // Fix 3: The Silent Failure Logger. Captures hidden errors without crashing the app.
        private void LogError(string context, Exception ex)
        {
            try
            {
                string logPath = Path.Combine(_saveDirectory, "error.log");
                Directory.CreateDirectory(_saveDirectory);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex.Message}\n");
            }
            catch { /* Fail gracefully if writing to the log fails */ }
        }
        #endregion

        #region Core Window Mechanics & DWM Blur
        private void Window_Deactivated(object sender, EventArgs e) { Keyboard.ClearFocus(); MainGrid.Focus(); }

        private void ApplyAcrylicToHwnd(IntPtr hwnd, Color color, double opacity)
        {
            if (hwnd == IntPtr.Zero) return;
            byte a = (byte)(opacity * 255);
            uint gradientColor = (uint)((a << 24) | (color.B << 16) | (color.G << 8) | color.R);
            var accent = new AccentPolicy { AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, GradientColor = gradientColor };
            var accentStructSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WindowCompositionAttributeData { Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY, SizeOfData = accentStructSize, Data = accentPtr };
            SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(accentPtr);
            int cornerPreference = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, (int)DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
        }

        private void ApplyAcrylicBlur(Color color, double opacity)
        {
            _currentFenceColor = color; _currentOpacity = opacity;
            var windowHelper = new WindowInteropHelper(this);
            if (windowHelper.Handle == IntPtr.Zero) return;
            ApplyAcrylicToHwnd(windowHelper.Handle, color, opacity);
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
                }), System.Windows.Threading.DispatcherPriority.Background);
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
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        #endregion

        #region Disk Saving & Loading
        private void LoadFenceState()
        {
            if (File.Exists(_saveFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_saveFilePath);
                    if (JsonSerializer.Deserialize<FenceData>(json) is FenceData data)
                    {
                        this.Left = data.Left; this.Top = data.Top;

                        bool isOnScreen = false;
                        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                        {
                            if (screen.WorkingArea.Contains((int)this.Left, (int)this.Top) ||
                                screen.WorkingArea.Contains((int)(this.Left + data.Width), (int)this.Top))
                            {
                                isOnScreen = true;
                                break;
                            }
                        }
                        if (!isOnScreen) { this.Left = 100; this.Top = 100; }

                        _isRolledUp = data.IsRolledUp;
                        _expandedHeight = data.ExpandedHeight; _expandedWidth = data.ExpandedWidth > 0 ? data.ExpandedWidth : 250;
                        _expandedLeft = data.ExpandedWidth > 0 ? data.ExpandedLeft : data.Left; _expandedTop = data.ExpandedWidth > 0 ? data.ExpandedTop : data.Top;
                        TitleText.Text = data.Title; _saveSortMethod = data.SortMethod;
                        AutoSortExtensions = data.AutoSortExtensions;
                        ShowSearch = data.ShowSearch;
                        SearchPanel.Visibility = ShowSearch ? Visibility.Visible : Visibility.Collapsed;
                        _currentIconSize = data.IconSize > 0 ? data.IconSize : 48;

                        try { _currentFenceColor = (Color)ColorConverter.ConvertFromString(data.HexColor); } catch { }
                        _currentOpacity = data.Opacity;

                        if (_isRolledUp) { if (data.Width <= 40) { this.Width = HEADER_SIZE; this.Height = data.Height; } else { this.Height = HEADER_SIZE; this.Width = data.Width; } }
                        else { this.Height = data.Height; this.Width = data.Width; }

                        _currentFiles.Clear(); foreach (string file in data.Files) { if (File.Exists(file) || Directory.Exists(file)) { _currentFiles.Add(file); } }
                        DetermineDockState(); UpdateDockOrientation(); ApplySorting();
                    }
                }
                catch (Exception ex) { LogError("LoadFenceState", ex); }
            }
        }

        private void SaveFenceState() { _saveTimer.Stop(); _saveTimer.Start(); }

        private void PerformDiskWrite()
        {
            if (_isDeleted || TitleText == null) return;
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
                    IconSize = _currentIconSize,
                    ShowSearch = ShowSearch,
                    Files = _currentFiles
                };
                Directory.CreateDirectory(_saveDirectory);
                File.WriteAllText(_saveFilePath, JsonSerializer.Serialize(data, _jsonOptions));
            }
            catch (Exception ex) { LogError("PerformDiskWrite", ex); }
        }
        #endregion

        #region Docking, Physics & Dragging
        private void DetermineDockState()
        {
            var helper = new WindowInteropHelper(this);
            var workArea = System.Windows.Forms.Screen.FromHandle(helper.Handle).WorkingArea;
            double dpiX = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            int physLeft = (int)(this.Left * dpiX); int physTop = (int)(this.Top * dpiY);
            int physRight = (int)((this.Left + this.Width) * dpiX); int physBottom = (int)((this.Top + this.Height) * dpiY);

            int thresh = 35;
            int dLeft = Math.Abs(physLeft - workArea.Left); int dRight = Math.Abs(physRight - workArea.Right);
            int dTop = Math.Abs(physTop - workArea.Top); int dBottom = Math.Abs(physBottom - workArea.Bottom);

            int minH = Math.Min(dLeft, dRight); int minV = Math.Min(dTop, dBottom);

            if (minH >= thresh && minV >= thresh)
            {
                _dockState = DockState.Floating;
            }
            else if (minH < thresh && minV < thresh)
            {
                // Corner Stickiness Hysteresis
                if (_dockState == DockState.Left || _dockState == DockState.Right)
                    _dockState = dRight < dLeft ? DockState.Right : DockState.Left;
                else if (_dockState == DockState.Top || _dockState == DockState.Bottom)
                    _dockState = dBottom < dTop ? DockState.Bottom : DockState.Top;
                else
                    _dockState = this.Width > this.Height ? (dBottom <= dTop ? DockState.Bottom : DockState.Top) : (dRight <= dLeft ? DockState.Right : DockState.Left);
            }
            else if (minV < minH)
            {
                _dockState = dBottom < dTop ? DockState.Bottom : DockState.Top;
            }
            else if (minH < minV)
            {
                _dockState = dRight < dLeft ? DockState.Right : DockState.Left;
            }
            else
            {
                _dockState = this.Width > this.Height ? (dBottom <= dTop ? DockState.Bottom : DockState.Top) : (dRight <= dLeft ? DockState.Right : DockState.Left);
            }
        }

        private void UpdateDockOrientation()
        {
            if (_dockState == DockState.Left)
            {
                HeaderRowTop.Height = new GridLength(0); HeaderRowBottom.Height = new GridLength(0);
                HeaderColLeft.Width = new GridLength(HEADER_SIZE); HeaderColRight.Width = new GridLength(0);
                Grid.SetRow(HeaderBorder, 0); Grid.SetRowSpan(HeaderBorder, 3); Grid.SetColumn(HeaderBorder, 0); Grid.SetColumnSpan(HeaderBorder, 1);
                HeaderGrid.LayoutTransform = new RotateTransform(-90); HeaderBorder.CornerRadius = new CornerRadius(8, 0, 0, 8);

                if (SearchBox != null && SearchBox.Parent == MainGrid)
                {
                    SearchBox.HorizontalAlignment = HorizontalAlignment.Left; SearchBox.VerticalAlignment = VerticalAlignment.Top; SearchBox.Margin = new Thickness(HEADER_SIZE + 5, 35, 0, 0);
                }
            }
            else if (_dockState == DockState.Right)
            {
                HeaderRowTop.Height = new GridLength(0); HeaderRowBottom.Height = new GridLength(0);
                HeaderColLeft.Width = new GridLength(0); HeaderColRight.Width = new GridLength(HEADER_SIZE);
                Grid.SetRow(HeaderBorder, 0); Grid.SetRowSpan(HeaderBorder, 3); Grid.SetColumn(HeaderBorder, 2); Grid.SetColumnSpan(HeaderBorder, 1);
                HeaderGrid.LayoutTransform = new RotateTransform(90); HeaderBorder.CornerRadius = new CornerRadius(0, 8, 8, 0);

                if (SearchBox != null && SearchBox.Parent == MainGrid)
                {
                    SearchBox.HorizontalAlignment = HorizontalAlignment.Right; SearchBox.VerticalAlignment = VerticalAlignment.Top; SearchBox.Margin = new Thickness(0, 35, HEADER_SIZE + 5, 0);
                }
            }
            else if (_dockState == DockState.Bottom)
            {
                HeaderRowTop.Height = new GridLength(0); HeaderRowBottom.Height = new GridLength(HEADER_SIZE);
                HeaderColLeft.Width = new GridLength(0); HeaderColRight.Width = new GridLength(0);
                Grid.SetRow(HeaderBorder, 2); Grid.SetRowSpan(HeaderBorder, 1); Grid.SetColumn(HeaderBorder, 0); Grid.SetColumnSpan(HeaderBorder, 3);
                HeaderGrid.LayoutTransform = Transform.Identity; HeaderBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);

                if (SearchBox != null && SearchBox.Parent == MainGrid)
                {
                    SearchBox.HorizontalAlignment = HorizontalAlignment.Right; SearchBox.VerticalAlignment = VerticalAlignment.Bottom; SearchBox.Margin = new Thickness(0, 0, 35, 4);
                }
            }
            else
            {
                HeaderRowTop.Height = new GridLength(HEADER_SIZE); HeaderRowBottom.Height = new GridLength(0);
                HeaderColLeft.Width = new GridLength(0); HeaderColRight.Width = new GridLength(0);
                Grid.SetRow(HeaderBorder, 0); Grid.SetRowSpan(HeaderBorder, 1); Grid.SetColumn(HeaderBorder, 0); Grid.SetColumnSpan(HeaderBorder, 3);
                HeaderGrid.LayoutTransform = Transform.Identity; HeaderBorder.CornerRadius = new CornerRadius(8, 8, 0, 0);

                if (SearchBox != null && SearchBox.Parent == MainGrid)
                {
                    SearchBox.HorizontalAlignment = HorizontalAlignment.Right; SearchBox.VerticalAlignment = VerticalAlignment.Top; SearchBox.Margin = new Thickness(0, 4, 35, 0);
                }
            }
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
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2) { ToggleRollUp(); return; }

                this.BeginAnimation(Window.LeftProperty, null); this.BeginAnimation(Window.TopProperty, null);
                this.BeginAnimation(Window.WidthProperty, null); this.BeginAnimation(Window.HeightProperty, null);

                _wasRolledUpBeforeDrag = _isRolledUp || _isTemporarilyRevealed;

                if (_wasRolledUpBeforeDrag)
                {
                    this.Width = _expandedWidth; this.Height = _expandedHeight; this.Left = _expandedLeft; this.Top = _expandedTop;
                }

                _isRolledUp = false; _isTemporarilyRevealed = false;
                _isManualDragging = true;
                _manualDragStartMouse = new System.Windows.Point(System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y);
                _manualDragStartLeft = this.Left; _manualDragStartTop = this.Top;
                HeaderBorder.CaptureMouse();
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

                var helper = new WindowInteropHelper(this);
                var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle).WorkingArea;
                double screenLeft = screen.Left / dpiX; double screenTop = screen.Top / dpiY;
                double screenRight = screen.Right / dpiX; double screenBottom = screen.Bottom / dpiY;

                double snapThresh = 25;

                if (Math.Abs(newLeft - screenLeft) < snapThresh) newLeft = screenLeft;
                else if (Math.Abs((newLeft + this.Width) - screenRight) < snapThresh) newLeft = screenRight - this.Width;

                if (Math.Abs(newTop - screenTop) < snapThresh) newTop = screenTop;
                else if (Math.Abs((newTop + this.Height) - screenBottom) < snapThresh) newTop = screenBottom - this.Height;

                if (newLeft < screenLeft) newLeft = screenLeft;
                if (newLeft + this.Width > screenRight) newLeft = screenRight - this.Width;
                if (newTop < screenTop) newTop = screenTop;
                if (newTop + this.Height > screenBottom) newTop = screenBottom - this.Height;

                this.Left = newLeft; this.Top = newTop;

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
                    if (this.IsMouseOver)
                    {
                        _isTemporarilyRevealed = true;
                        if (_dockState == DockState.Left || _dockState == DockState.Right) this.Width = _expandedWidth;
                        else this.Height = _expandedHeight;
                        this.Left = _expandedLeft; this.Top = _expandedTop;
                    }
                    else
                    {
                        _isTemporarilyRevealed = false;
                        if (_dockState == DockState.Bottom) { this.Height = HEADER_SIZE; this.Top = _expandedTop + (_expandedHeight - HEADER_SIZE); }
                        else if (_dockState == DockState.Right) { this.Width = HEADER_SIZE; this.Left = _expandedLeft + (_expandedWidth - HEADER_SIZE); }
                        else { this.Height = HEADER_SIZE; this.Width = _expandedWidth; this.Left = _expandedLeft; this.Top = _expandedTop; }
                        AnimateRollUp();
                    }
                }
                else { this.Left = _expandedLeft; this.Top = _expandedTop; }

                SaveFenceState();
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

            ReleaseCapture();
            var helper = new WindowInteropHelper(this);
            SendMessage(helper.Handle, WM_SYSCOMMAND, (IntPtr)(SC_SIZE + direction), IntPtr.Zero);

            DetermineDockState(); UpdateDockOrientation();
            _expandedLeft = this.Left; _expandedTop = this.Top; _expandedWidth = this.Width; _expandedHeight = this.Height;

            if (wasRolledUp) { _isRolledUp = true; _isTemporarilyRevealed = true; }
            SaveFenceState();
        }

        private void Resize_Left(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(1); }
        private void Resize_Right(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(2); }
        private void Resize_Top(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(3); }
        private void Resize_TopLeft(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(4); }
        private void Resize_TopRight(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(5); }
        private void Resize_Bottom(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(6); }
        private void Resize_BottomLeft(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(7); }
        private void Resize_BottomRight(object sender, MouseButtonEventArgs e) { e.Handled = true; StartNativeResize(8); }
        #endregion

        #region User Interaction & Menus
        private void TitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) return;
            e.Handled = true; TitleText.Visibility = Visibility.Collapsed;
            TextBox renameBox = new() { Text = TitleText.Text, Width = 140, Height = 22, Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)), Foreground = Brushes.White, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Left, FontFamily = TitleText.FontFamily, FontWeight = FontWeights.SemiBold, Padding = new Thickness(2, 0, 0, 0) };
            TitleStack.Children.Add(renameBox); renameBox.Focus(); renameBox.SelectAll();
            renameBox.KeyDown += (s2, e2) => { if (e2.Key == Key.Enter) { TitleText.Text = renameBox.Text; TitleStack.Children.Remove(renameBox); TitleText.Visibility = Visibility.Visible; SaveFenceState(); } else if (e2.Key == Key.Escape) { TitleStack.Children.Remove(renameBox); TitleText.Visibility = Visibility.Visible; } };
            renameBox.LostFocus += (s2, e2) => { if (TitleStack.Children.Contains(renameBox)) { TitleText.Text = renameBox.Text; TitleStack.Children.Remove(renameBox); TitleText.Visibility = Visibility.Visible; SaveFenceState(); } };
        }

        private void SearchIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (SearchBox.Visibility == Visibility.Visible) { SearchBox.Text = ""; SearchBox.Visibility = Visibility.Collapsed; }
            else { SearchBox.Visibility = Visibility.Visible; SearchBox.Focus(); }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { RefreshIconUI(); }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text) && !SearchBox.IsKeyboardFocusWithin)
            {
                System.Threading.Tasks.Task.Delay(150).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() => { if (!SearchBox.IsFocused && string.IsNullOrWhiteSpace(SearchBox.Text)) SearchBox.Visibility = Visibility.Collapsed; });
                });
            }
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { SearchBox.Text = ""; SearchBox.Visibility = Visibility.Collapsed; e.Handled = true; MainGrid.Focus(); }
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e) { if (_isRolledUp && !_isTemporarilyRevealed) { AnimateReveal(); _isTemporarilyRevealed = true; } }
        private void Window_MouseLeave(object sender, MouseEventArgs e) { if (_isContextMenuOpen || _isDraggingSelectionBox || SearchBox.IsFocused) return; if (_isRolledUp && _isTemporarilyRevealed) { AnimateRollUp(); _isTemporarilyRevealed = false; } }
        private void Window_DragEnter(object sender, DragEventArgs e) { if (_isRolledUp && !_isTemporarilyRevealed) { AnimateReveal(); _isTemporarilyRevealed = true; } }
        private void Window_DragOver(object sender, DragEventArgs e) { if (_isRolledUp && !_isTemporarilyRevealed) { AnimateReveal(); _isTemporarilyRevealed = true; } }
        private void Window_DragLeave(object sender, DragEventArgs e) { Point pt = e.GetPosition(this); if (pt.X <= 2 || pt.Y <= 2 || pt.X >= this.ActualWidth - 2 || pt.Y >= this.ActualHeight - 2) { if (_isRolledUp && _isTemporarilyRevealed) { AnimateRollUp(); _isTemporarilyRevealed = false; } } }
        private void HandleMenuClosed() { _isContextMenuOpen = false; if (!this.IsMouseOver && _isRolledUp && _isTemporarilyRevealed && !_isDraggingSelectionBox) { AnimateRollUp(); _isTemporarilyRevealed = false; } }

        private void MenuIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; _isContextMenuOpen = true;
            HeaderContextMenu.PlacementTarget = sender as UIElement ?? HeaderBorder;
            HeaderContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            HeaderContextMenu.IsOpen = true;
        }

        private void AutoOrganize()
        {
            if (string.IsNullOrWhiteSpace(AutoSortExtensions)) { MessageBox.Show("Please set up Auto-Sort extensions in Settings first (e.g. .jpg, .png).", "Auto Organize", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string[] files = Directory.GetFiles(desktopPath);
                string[] extensions = AutoSortExtensions.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

                bool moved = false;
                foreach (string file in files)
                {
                    string ext = Path.GetExtension(file);
                    foreach (string target in extensions)
                    {
                        if (string.Equals(ext, target, StringComparison.OrdinalIgnoreCase) || string.Equals(ext, "." + target, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!_currentFiles.Contains(file)) { _currentFiles.Add(file); moved = true; }
                            break;
                        }
                    }
                }

                if (moved) { ApplySorting(); SaveFenceState(); MessageBox.Show("Desktop files organized successfully!", "Auto Organize", MessageBoxButton.OK, MessageBoxImage.Information); }
                else MessageBox.Show("No new files found on the desktop matching your extensions.", "Auto Organize", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show($"Error organizing files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void Menu_NewFence_Click(object sender, RoutedEventArgs e) { MainWindow newFence = new(Guid.NewGuid().ToString()); newFence.Left = this.Left + 50; newFence.Top = this.Top + 50; newFence.Show(); }
        private void Menu_DeleteFence_Click(object sender, RoutedEventArgs e) { if (MessageBox.Show("Permanently delete this fence?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) { _isDeleted = true; if (File.Exists(_saveFilePath)) File.Delete(_saveFilePath); this.Close(); } }
        private void Menu_RollUp_Click(object sender, RoutedEventArgs e) { ToggleRollUp(); }
        private void Menu_Sort_Click(object sender, RoutedEventArgs e) { if (sender is not MenuItem clickedSort || clickedSort.Tag == null) return; _saveSortMethod = clickedSort.Tag.ToString() ?? "None"; ApplySorting(); SaveFenceState(); }

        private void Menu_IconSize_Click(object sender, RoutedEventArgs e) { if (sender is not MenuItem clickedItem || clickedItem.Tag == null) return; if (double.TryParse(clickedItem.Tag.ToString(), out double newSize)) { _currentIconSize = newSize; RefreshIconUI(); SaveFenceState(); } }
        private void Menu_Settings_Click(object sender, RoutedEventArgs e) { SettingsWindow settings = new(this); settings.Owner = this; settings.ShowDialog(); }

        private void Menu_EditColor_Click(object sender, RoutedEventArgs e) { SolidColorBrush currentBrush = new(_currentFenceColor) { Opacity = _currentOpacity }; ColorPickerWindow picker = new(currentBrush); picker.Owner = this; if (picker.ShowDialog() == true && picker.SelectedBrush != null) { ApplyAcrylicBlur(picker.SelectedBrush.Color, picker.SelectedBrush.Opacity); SaveFenceState(); } }
        #endregion

        #region Async UI Rendering & File Icon Engine
        private void ApplySorting() { if (_saveSortMethod != "None" && _saveSortMethod != "DateAdd") { try { switch (_saveSortMethod) { case "Name": _currentFiles.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase)); break; case "Size": _currentFiles.Sort((a, b) => GetFileSize(b).CompareTo(GetFileSize(a))); break; case "Type": _currentFiles.Sort((a, b) => string.Compare(Path.GetExtension(a), Path.GetExtension(b), StringComparison.OrdinalIgnoreCase)); break; case "DateMod": _currentFiles.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a))); break; case "DateCre": _currentFiles.Sort((a, b) => File.GetCreationTime(b).CompareTo(File.GetCreationTime(a))); break; } } catch { } } RefreshIconUI(); }
        private static long GetFileSize(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }

        private void RefreshIconUI()
        {
            IconPanel.Children.Clear(); ClearSelection();
            if (_currentFiles.Count == 0) { IconPanel.Children.Add(new TextBlock { Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#88FFFFFF")!, Text = "Drop shortcuts here..." }); return; }

            string searchQuery = SearchBox.Text;
            foreach (string file in _currentFiles)
            {
                if (!string.IsNullOrWhiteSpace(searchQuery) && !Path.GetFileNameWithoutExtension(file).Contains(searchQuery, StringComparison.OrdinalIgnoreCase)) continue;
                AddIconToUI(file);
            }
        }

        private void Window_Drop(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files) { foreach (string file in files) { if (!_currentFiles.Contains(file)) { _currentFiles.Add(file); } } ApplySorting(); SaveFenceState(); string? sourceId = e.Data.GetData("FenceSourceId") as string; if (!string.IsNullOrEmpty(sourceId) && sourceId != _fenceId) e.Effects = DragDropEffects.Move; else e.Effects = DragDropEffects.None; } }

        private async void AddIconToUI(string file)
        {
            string currentFilePath = file; double containerWidth = _currentIconSize + 24;
            StackPanel itemContainer = new() { Orientation = Orientation.Vertical, Margin = new Thickness(8), Width = containerWidth, ToolTip = currentFilePath, Background = System.Windows.Media.Brushes.Transparent };

            System.Windows.Controls.Image iconImage = new()
            {
                Width = _currentIconSize,
                Height = _currentIconSize,
                Margin = new Thickness(0, 0, 0, 5),
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };

            TextBlock textBlock = new() { Text = Path.GetFileNameWithoutExtension(currentFilePath), Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, FontSize = 11, TextWrapping = TextWrapping.Wrap };

            itemContainer.Children.Add(iconImage);
            itemContainer.Children.Add(textBlock);
            IconPanel.Children.Add(itemContainer);

            // Fetch the heavy icon asynchronously in the background
            try
            {
                ImageSource? fetchedSource = await System.Threading.Tasks.Task.Run(() => GetHighResIcon(currentFilePath));
                if (fetchedSource != null)
                {
                    iconImage.Source = fetchedSource;
                    RenderOptions.SetBitmapScalingMode(iconImage, BitmapScalingMode.HighQuality);
                }
            }
            catch (Exception ex) { LogError($"Async Icon Failure: {currentFilePath}", ex); }

            ContextMenu rightClickMenu = new(); rightClickMenu.Opened += ContextMenu_Opened_ApplyBlur; rightClickMenu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(Submenu_Opened_ApplyBlur));
            MenuItem renameItem = new() { Header = "Rename File" };
            MenuItem removeItem = new() { Header = "Remove from Fence" };

            removeItem.Click += (s, args) =>
            {
                if (_selectedItems.Count > 1 && _selectedItems.Contains(itemContainer)) { foreach (StackPanel selectedPanel in _selectedItems) { if (selectedPanel.ToolTip is string path) _currentFiles.Remove(path); } } else { _currentFiles.Remove(currentFilePath); }
                ClearSelection(); ApplySorting(); SaveFenceState();
            };

            void TriggerRename()
            {
                textBlock.Visibility = Visibility.Collapsed;
                TextBox renameBox = new() { Text = textBlock.Text, Width = containerWidth, Margin = new Thickness(-5, 0, -5, 0), Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)), Foreground = Brushes.White, BorderThickness = new Thickness(0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
                itemContainer.Children.Add(renameBox); renameBox.Focus(); renameBox.SelectAll();

                bool isHandlingRename = false;
                void CommitRename()
                {
                    if (isHandlingRename) return; isHandlingRename = true;
                    try
                    {
                        string? dir = Path.GetDirectoryName(currentFilePath); string ext = Path.GetExtension(currentFilePath);
                        if (dir != null) { string newPath = Path.Combine(dir, renameBox.Text + ext); if (currentFilePath != newPath && !File.Exists(newPath)) { File.Move(currentFilePath, newPath); _currentFiles.Remove(currentFilePath); _currentFiles.Add(newPath); ApplySorting(); SaveFenceState(); return; } }
                    }
                    catch { MessageBox.Show("Could not rename file.", "Error"); }

                    if (itemContainer.Children.Contains(renameBox)) { itemContainer.Children.Remove(renameBox); textBlock.Visibility = Visibility.Visible; }
                }

                renameBox.KeyDown += (s2, e2) => { if (e2.Key == Key.Enter) { CommitRename(); } else if (e2.Key == Key.Escape) { isHandlingRename = true; if (itemContainer.Children.Contains(renameBox)) { itemContainer.Children.Remove(renameBox); textBlock.Visibility = Visibility.Visible; } } };
                renameBox.LostFocus += (s2, e2) => { CommitRename(); };
            }

            renameItem.Click += (s, args) => TriggerRename(); rightClickMenu.Items.Add(renameItem); rightClickMenu.Items.Add(removeItem); itemContainer.ContextMenu = rightClickMenu; rightClickMenu.Opened += (s, args) => _isContextMenuOpen = true; rightClickMenu.Closed += (s, args) => HandleMenuClosed();
            textBlock.MouseLeftButtonDown += (s, args) => { if (args.ClickCount == 2) return; args.Handled = true; TriggerRename(); };
            System.Windows.Point? dragStartPoint = null;

            itemContainer.PreviewMouseLeftButtonDown += (s, args) => { dragStartPoint = args.GetPosition(null); bool isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl); if (isCtrlPressed) { if (_selectedItems.Contains(itemContainer)) { itemContainer.Background = System.Windows.Media.Brushes.Transparent; _selectedItems.Remove(itemContainer); } else { itemContainer.Background = _highlightBrush; _selectedItems.Add(itemContainer); } } else if (!_selectedItems.Contains(itemContainer)) { ClearSelection(); itemContainer.Background = _highlightBrush; _selectedItems.Add(itemContainer); } };

            itemContainer.PreviewMouseRightButtonDown += (s, args) =>
            {
                if (!_selectedItems.Contains(itemContainer))
                {
                    ClearSelection();
                    itemContainer.Background = _highlightBrush;
                    _selectedItems.Add(itemContainer);
                }
            };

            itemContainer.PreviewMouseMove += (s, args) =>
            {
                if (args.LeftButton == MouseButtonState.Pressed && dragStartPoint.HasValue)
                {
                    System.Windows.Point currentPoint = args.GetPosition(null);
                    if (Math.Abs(currentPoint.X - dragStartPoint.Value.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(currentPoint.Y - dragStartPoint.Value.Y) > SystemParameters.MinimumVerticalDragDistance)
                    {

                        string[] draggedFiles;
                        if (_selectedItems.Contains(itemContainer) && _selectedItems.Count > 1)
                        {
                            draggedFiles = _selectedItems.Select(panel => panel.ToolTip.ToString()!).ToArray();
                        }
                        else
                        {
                            draggedFiles = new[] { currentFilePath };
                        }

                        DataObject dragData = new(DataFormats.FileDrop, draggedFiles);
                        dragData.SetData("FenceSourceId", _fenceId);
                        DragDropEffects result = DragDrop.DoDragDrop(itemContainer, dragData, DragDropEffects.Move | DragDropEffects.Copy);

                        if (result == DragDropEffects.Move)
                        {
                            foreach (string f in draggedFiles) _currentFiles.Remove(f);
                            ClearSelection();
                            ApplySorting();
                            SaveFenceState();
                        }
                        dragStartPoint = null;
                    }
                }
            };
        }

        private string ResolveShortcutTarget(string shortcutPath)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        var shortcut = shell.CreateShortcut(shortcutPath);
                        string iconLocation = shortcut.IconLocation;
                        if (!string.IsNullOrEmpty(iconLocation)) { string[] parts = iconLocation.Split(','); if (parts.Length > 0 && File.Exists(parts[0])) return parts[0]; }
                        string targetPath = shortcut.TargetPath;
                        if (File.Exists(targetPath) || Directory.Exists(targetPath)) return targetPath;
                    }
                }
            }
            catch (Exception ex) { LogError($"Shortcut Resolve Failed: {shortcutPath}", ex); }
            return shortcutPath;
        }

        private ImageSource? GetHighResIcon(string filePath)
        {
            if (!File.Exists(filePath) && !Directory.Exists(filePath)) return null;
            string ext = Path.GetExtension(filePath);

            if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                filePath = ResolveShortcutTarget(filePath);
                ext = Path.GetExtension(filePath);
            }

            if (_imageExtensions.Contains(ext))
            {
                try
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 256;
                    bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
                catch (Exception ex) { LogError($"Native Image Loader Failed: {filePath}", ex); }
            }

            try
            {
                Guid iid = new("bcc18b79-ba16-442f-80c4-8a59c30d463b");
                int hr = SHCreateItemFromParsingName(filePath, IntPtr.Zero, in iid, out IShellItemImageFactory factory);

                if (hr == 0 && factory != null)
                {
                    IntPtr hbitmap = IntPtr.Zero;
                    try { factory.GetImage(new NativeSize { Width = 256, Height = 256 }, 0x0, out hbitmap); } catch { }
                    if (hbitmap == IntPtr.Zero) { try { factory.GetImage(new NativeSize { Width = 256, Height = 256 }, 0x4, out hbitmap); } catch { } }

                    if (hbitmap != IntPtr.Zero)
                    {
                        ImageSource imgSrc = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        DeleteObject(hbitmap);
                        imgSrc.Freeze();
                        return imgSrc;
                    }
                }
            }
            catch (Exception ex) { LogError($"Shell API Factory Failed: {filePath}", ex); }

            if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    uint sizeRequest = (uint)((16 << 16) | 256);
                    int res = SHDefExtractIcon(filePath, 0, 0, out IntPtr hIconLarge, out IntPtr hIconSmall, sizeRequest);

                    if (res == 0 && hIconLarge != IntPtr.Zero)
                    {
                        ImageSource imgSrc = Imaging.CreateBitmapSourceFromHIcon(hIconLarge, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        imgSrc.Freeze();
                        DestroyIcon(hIconLarge);
                        if (hIconSmall != IntPtr.Zero) DestroyIcon(hIconSmall);
                        return imgSrc;
                    }
                }
                catch (Exception ex) { LogError($"Binary Extraction Failed: {filePath}", ex); }
            }

            try
            {
                SHFILEINFO shinfo = new SHFILEINFO();
                IntPtr result = SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), 0x000000100 | 0x000000000);

                if (result != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    ImageSource imgSrc = Imaging.CreateBitmapSourceFromHIcon(shinfo.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    imgSrc.Freeze();
                    DestroyIcon(shinfo.hIcon);
                    return imgSrc;
                }
            }
            catch (Exception ex) { LogError($"Win32 Fallback Failed: {filePath}", ex); }

            return null;
        }
        #endregion

        #region Window Start & End Lifecycle
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

            LoadFenceState(); ApplyAcrylicBlur(_currentFenceColor, _currentOpacity);
            var helper = new WindowInteropHelper(this);
            IntPtr myWindowHandle = helper.Handle; // Tell Windows this is a background Tool Window, which prevents Wallpaper Engine from treating it like a focused game!
            IntPtr exStyle = GetWindowLongPtr(myWindowHandle, GWL_EXSTYLE);
            SetWindowLongPtr(myWindowHandle, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_TOOLWINDOW));

            SetWindowPos(myWindowHandle, (IntPtr)HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            HwndSource? source = HwndSource.FromHwnd(myWindowHandle); if (source != null) source.AddHook(new HwndSourceHook(WndProc));
            this.PreviewKeyDown += Window_PreviewKeyDown; 


            IconPanel.MouseRightButtonDown += (s, args) => {
                if (args.OriginalSource is ScrollViewer || args.OriginalSource is WrapPanel || args.OriginalSource == IconPanel)
                {
                    ClearSelection();
                    _isContextMenuOpen = true;
                    HeaderContextMenu.PlacementTarget = IconPanel;
                    HeaderContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                    HeaderContextMenu.IsOpen = true;
                    args.Handled = true;
                }
            };

            if (MenuRollUpFence != null) MenuRollUpFence.IsCheckable = true;

            MenuItem autoOrganizeItem = new MenuItem { Header = "Auto Organize (Pull from Desktop)" };
            autoOrganizeItem.Click += (s, args) => AutoOrganize();
            if (HeaderContextMenu != null)
            {
                HeaderContextMenu.Items.Insert(0, autoOrganizeItem);
                HeaderContextMenu.Items.Insert(1, new Separator());
            }

            HeaderContextMenu.Opened += (s, args) =>
            {
                _isContextMenuOpen = true;
                if (MenuRollUpFence != null) MenuRollUpFence.IsChecked = _isRolledUp;
            };

            HeaderContextMenu.Closed += (s, args) => HandleMenuClosed();
            this.PreviewMouseRightButtonDown += (s, args) => _isContextMenuOpen = true;

            if (SearchBox != null && MainGrid != null)
            {
                SearchBox.KeyDown += SearchBox_KeyDown;

                DependencyObject parent = LogicalTreeHelper.GetParent(SearchBox);
                if (parent is Panel panel && panel != MainGrid)
                {
                    panel.Children.Remove(SearchBox);
                    MainGrid.Children.Add(SearchBox);

                    Grid.SetRow(SearchBox, 0); Grid.SetRowSpan(SearchBox, 10);
                    Grid.SetColumn(SearchBox, 0); Grid.SetColumnSpan(SearchBox, 10);

                    SearchBox.Width = 160; SearchBox.Height = 26;
                    Panel.SetZIndex(SearchBox, 999);
                }
            }

            string configFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences");
            string globalConfigPath = Path.Combine(configFolder, "global_config.json");

            bool showInTaskbar = true;

            if (!File.Exists(globalConfigPath))
            {
                Directory.CreateDirectory(configFolder);
                GlobalConfig newConfig = new() { FirstRunComplete = true, ShowTaskbarIcon = true };
                File.WriteAllText(globalConfigPath, JsonSerializer.Serialize(newConfig, _jsonOptions));

                SettingsWindow settings = new(this, true);
                settings.Owner = this; settings.ShowDialog();
            }
            else
            {
                try
                {
                    string json = File.ReadAllText(globalConfigPath);
                    if (JsonSerializer.Deserialize<GlobalConfig>(json) is GlobalConfig config)
                    {
                        showInTaskbar = config.ShowTaskbarIcon;
                    }
                }
                catch { }
            }

            this.ShowInTaskbar = showInTaskbar;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WINDOWPOSCHANGING) { WINDOWPOS windowPos = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS))!; windowPos.hwndInsertAfter = (IntPtr)HWND_BOTTOM; if ((windowPos.flags & SWP_NOMOVE) == 0 && (windowPos.flags & SWP_NOSIZE) != 0) { int snapMargin = 20; var screen = System.Windows.Forms.Screen.FromHandle(hwnd); var workArea = screen.WorkingArea; if (Math.Abs(windowPos.x - workArea.Left) < snapMargin) windowPos.x = workArea.Left; if (Math.Abs(windowPos.y - workArea.Top) < snapMargin) windowPos.y = workArea.Top; if (Math.Abs((windowPos.x + windowPos.cx) - workArea.Right) < snapMargin) windowPos.x = workArea.Right - windowPos.cx; if (Math.Abs((windowPos.y + windowPos.cy) - workArea.Bottom) < snapMargin) windowPos.y = workArea.Bottom - windowPos.cy; } Marshal.StructureToPtr(windowPos, lParam, true); }
            return IntPtr.Zero;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && _selectedItems.Count > 0 && !(e.OriginalSource is TextBox))
            {
                foreach (StackPanel selectedPanel in _selectedItems)
                {
                    if (selectedPanel.ToolTip is string path) _currentFiles.Remove(path);
                }
                ClearSelection(); ApplySorting(); SaveFenceState(); e.Handled = true; return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control) { var helper = new WindowInteropHelper(this); var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle); var workArea = screen.WorkingArea; PresentationSource? source = PresentationSource.FromVisual(this); double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0; double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0; double logicalLeft = workArea.Left / dpiX; double logicalTop = workArea.Top / dpiY; double logicalRight = workArea.Right / dpiX; double logicalBottom = workArea.Bottom / dpiY; bool wasHandled = false; this.BeginAnimation(Window.LeftProperty, null); this.BeginAnimation(Window.TopProperty, null); this.BeginAnimation(Window.WidthProperty, null); this.BeginAnimation(Window.HeightProperty, null); if (_isRolledUp) { _isRolledUp = false; _isTemporarilyRevealed = false; this.Width = _expandedWidth; this.Height = _expandedHeight; } if (e.Key == Key.Up) { this.Top = logicalTop; wasHandled = true; } else if (e.Key == Key.Down) { this.Top = logicalBottom - this.Height; wasHandled = true; } else if (e.Key == Key.Left) { this.Left = logicalLeft; wasHandled = true; } else if (e.Key == Key.Right) { this.Left = logicalRight - this.Width; wasHandled = true; } if (wasHandled) { e.Handled = true; DetermineDockState(); UpdateDockOrientation(); _expandedLeft = this.Left; _expandedTop = this.Top; _expandedWidth = this.Width; _expandedHeight = this.Height; SaveFenceState(); } }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { if (!_isDeleted) PerformDiskWrite(); base.OnClosing(e); }
        private void ClearSelection() { foreach (var item in _selectedItems) item.Background = System.Windows.Media.Brushes.Transparent; _selectedItems.Clear(); }
        private void IconPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is ScrollViewer || e.OriginalSource is WrapPanel) { ClearSelection(); _selectionStartPoint = e.GetPosition(SelectionCanvas); _isDraggingSelectionBox = true; SelectionBox.Visibility = Visibility.Visible; SelectionBox.Width = 0; SelectionBox.Height = 0; Canvas.SetLeft(SelectionBox, _selectionStartPoint.X); Canvas.SetTop(SelectionBox, _selectionStartPoint.Y); IconPanel.CaptureMouse(); } }
        private void IconPanel_MouseMove(object sender, MouseEventArgs e) { if (_isDraggingSelectionBox) { System.Windows.Point currentPoint = e.GetPosition(SelectionCanvas); double x = Math.Min(currentPoint.X, _selectionStartPoint.X); double y = Math.Min(currentPoint.Y, _selectionStartPoint.Y); double width = Math.Abs(currentPoint.X - _selectionStartPoint.X); double height = Math.Abs(currentPoint.Y - _selectionStartPoint.Y); Canvas.SetLeft(SelectionBox, x); Canvas.SetTop(SelectionBox, y); SelectionBox.Width = width; SelectionBox.Height = height; Rect selectionRect = new Rect(x, y, width, height); foreach (UIElement child in IconPanel.Children) { if (child is StackPanel item) { System.Windows.Point itemPos = item.TranslatePoint(new System.Windows.Point(0, 0), SelectionCanvas); Rect itemRect = new Rect(itemPos, new Size(item.ActualWidth, item.ActualHeight)); if (selectionRect.IntersectsWith(itemRect)) { if (!_selectedItems.Contains(item)) { item.Background = _highlightBrush; _selectedItems.Add(item); } } else if (_selectedItems.Contains(item)) { item.Background = System.Windows.Media.Brushes.Transparent; _selectedItems.Remove(item); } } } } }
        private void IconPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { if (_isDraggingSelectionBox) { _isDraggingSelectionBox = false; SelectionBox.Visibility = Visibility.Collapsed; IconPanel.ReleaseMouseCapture(); } }
        #endregion
    }

    #region Data Models
    public class FenceData
    {
        [System.Text.Json.Serialization.JsonPropertyName("Left")] public double Left { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Top")] public double Top { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Width")] public double Width { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Height")] public double Height { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("IsRolledUp")] public bool IsRolledUp { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("ExpandedHeight")] public double ExpandedHeight { get; set; } = 300;
        [System.Text.Json.Serialization.JsonPropertyName("ExpandedWidth")] public double ExpandedWidth { get; set; } = 250;
        [System.Text.Json.Serialization.JsonPropertyName("ExpandedLeft")] public double ExpandedLeft { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("ExpandedTop")] public double ExpandedTop { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("Title")] public string Title { get; set; } = "Fluid Fence";
        [System.Text.Json.Serialization.JsonPropertyName("HexColor")] public string HexColor { get; set; } = "#000000";
        [System.Text.Json.Serialization.JsonPropertyName("Opacity")] public double Opacity { get; set; } = 0.7;
        [System.Text.Json.Serialization.JsonPropertyName("SortMethod")] public string SortMethod { get; set; } = "None";
        [System.Text.Json.Serialization.JsonPropertyName("AutoSortExtensions")] public string AutoSortExtensions { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("IconSize")] public double IconSize { get; set; } = 48;
        [System.Text.Json.Serialization.JsonPropertyName("ShowSearch")] public bool ShowSearch { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("Files")] public List<string> Files { get; set; } = new();
    }

    public class GlobalConfig
    {
        [System.Text.Json.Serialization.JsonPropertyName("FirstRunComplete")] public bool FirstRunComplete { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("ShowTaskbarIcon")] public bool ShowTaskbarIcon { get; set; } = true;
    }
    #endregion
}