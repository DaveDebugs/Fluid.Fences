using System;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media.Animation;

namespace DesktopFences
{
    public partial class MainWindow : Window
    {
        internal enum AccentState { ACCENT_DISABLED = 0, ACCENT_ENABLE_GRADIENT = 1, ACCENT_ENABLE_TRANSPARENTGRADIENT = 2, ACCENT_ENABLE_BLURBEHIND = 3, ACCENT_ENABLE_ACRYLICBLURBEHIND = 4, ACCENT_INVALID_STATE = 5 }
        [StructLayout(LayoutKind.Sequential)] internal struct AccentPolicy { public AccentState AccentState; public uint AccentFlags; public uint GradientColor; public uint AnimationId; }
        [StructLayout(LayoutKind.Sequential)] internal struct WindowCompositionAttributeData { public WindowCompositionAttribute Attribute; public IntPtr Data; public int SizeOfData; }
        internal enum WindowCompositionAttribute { WCA_ACCENT_POLICY = 19 }
        internal enum DWMWINDOWATTRIBUTE { DWMWA_WINDOW_CORNER_PREFERENCE = 33 }
        internal enum DWM_WINDOW_CORNER_PREFERENCE { DWMWCP_DEFAULT = 0, DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3 }

        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        [DllImport("user32.dll")] internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30d463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItemImageFactory { void GetImage([In] NativeSize size, [In] int flags, [Out] out IntPtr phbm); }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        internal static extern void SHCreateItemFromParsingName([In, MarshalAs(UnmanagedType.LPWStr)] string pszPath, [In] IntPtr pbc, [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid, [Out, MarshalAs(UnmanagedType.Interface, IidParameterIndex = 2)] out IShellItemImageFactory ppv);

        [StructLayout(LayoutKind.Sequential)] internal struct NativeSize { public int Width; public int Height; }
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);

        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_SIZE = 0xF000;
        private const int HWND_BOTTOM = 1;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int WM_WINDOWPOSCHANGING = 0x0046;

        [StructLayout(LayoutKind.Sequential)] public struct WINDOWPOS { public IntPtr hwnd; public IntPtr hwndInsertAfter; public int x; public int y; public int cx; public int cy; public uint flags; }

        public enum DockState { Floating, Top, Bottom, Left, Right }

        private List<string> _currentFiles = new List<string>();
        private string _saveDirectory = string.Empty;
        private string _saveFilePath = string.Empty;
        private string _fenceId = string.Empty;

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

        private List<StackPanel> _selectedItems = new List<StackPanel>();
        private System.Windows.Point _selectionStartPoint;
        private bool _isDraggingSelectionBox = false;
        private SolidColorBrush _highlightBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(85, 0, 120, 215));

        public string FenceTitle { get => TitleText.Text; set => TitleText.Text = value; }
        public string FenceSortMethod { get => _saveSortMethod; set => _saveSortMethod = value; }
        public string AutoSortExtensions { get; set; } = "";

        public void DashboardSaveAndRefresh() { ApplySorting(); SaveFenceState(); }
        public void DashboardDelete() { _isDeleted = true; if (File.Exists(_saveFilePath)) File.Delete(_saveFilePath); this.Close(); }
        public void AddFileFromAutoSort(string filePath) { if (!_currentFiles.Contains(filePath)) _currentFiles.Add(filePath); }

        public MainWindow()
        {
            InitializeComponent();
            _fenceId = "DESIGNER";
            _saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences", "Fences");
            _saveFilePath = Path.Combine(_saveDirectory, "designer.json");
        }

        public MainWindow(string fenceId) : this()
        {
            _fenceId = fenceId;
            _saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences", "Fences");
            _saveFilePath = Path.Combine(_saveDirectory, $"{_fenceId}.json");
        }

        private void MainGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            MainGrid.Focus();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            Keyboard.ClearFocus();
            MainGrid.Focus();
        }

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
        }

        private void ContextMenu_Opened_ApplyBlur(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu menu)
            {
                HwndSource? source = PresentationSource.FromVisual(menu) as HwndSource;
                if (source != null && source.Handle != IntPtr.Zero)
                {
                    ApplyAcrylicToHwnd(source.Handle, Color.FromRgb(26, 26, 26), 0.7);
                }
            }
        }

        // ======================================================================
        // FIXED: The new Submenu Window Hunter!
        // ======================================================================
        private void Submenu_Opened_ApplyBlur(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is MenuItem menuItem)
            {
                // We use the Dispatcher to wait a tiny fraction of a second, 
                // allowing WPF time to actually draw the new popup window before we grab its handle!
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var popup = menuItem.Template.FindName("PART_Popup", menuItem) as System.Windows.Controls.Primitives.Popup;
                    if (popup != null && popup.Child != null)
                    {
                        HwndSource? source = PresentationSource.FromVisual(popup.Child) as HwndSource;
                        if (source != null && source.Handle != IntPtr.Zero)
                        {
                            ApplyAcrylicToHwnd(source.Handle, Color.FromRgb(26, 26, 26), 0.7);
                        }
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void LoadFenceState()
        {
            if (File.Exists(_saveFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_saveFilePath);
                    FenceData? data = JsonSerializer.Deserialize<FenceData>(json);
                    if (data != null)
                    {
                        this.Left = data.Left; this.Top = data.Top; _isRolledUp = data.IsRolledUp;
                        _expandedHeight = data.ExpandedHeight; _expandedWidth = data.ExpandedWidth > 0 ? data.ExpandedWidth : 250;
                        _expandedLeft = data.ExpandedWidth > 0 ? data.ExpandedLeft : data.Left; _expandedTop = data.ExpandedWidth > 0 ? data.ExpandedTop : data.Top;
                        TitleText.Text = data.Title; _saveSortMethod = data.SortMethod;
                        AutoSortExtensions = data.AutoSortExtensions;

                        try { _currentFenceColor = (Color)ColorConverter.ConvertFromString(data.HexColor); } catch { }
                        _currentOpacity = data.Opacity;

                        if (_isRolledUp) { if (data.Width <= 40) { this.Width = HEADER_SIZE; this.Height = data.Height; } else { this.Height = HEADER_SIZE; this.Width = data.Width; } }
                        else { this.Height = data.Height; this.Width = data.Width; }

                        _currentFiles.Clear(); foreach (string file in data.Files) { if (File.Exists(file) || Directory.Exists(file)) { _currentFiles.Add(file); } }
                        DetermineDockState(); UpdateDockOrientation(); ApplySorting();
                    }
                }
                catch { }
            }
        }

        private void SaveFenceState()
        {
            if (_isDeleted) return;
            try
            {
                FenceData data = new FenceData
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
                    Files = _currentFiles
                };
                Directory.CreateDirectory(_saveDirectory);
                File.WriteAllText(_saveFilePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private void DetermineDockState()
        {
            var workArea = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
            double dpiX = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            int physLeft = (int)(this.Left * dpiX); int physTop = (int)(this.Top * dpiY);
            int physRight = (int)((this.Left + this.Width) * dpiX); int physBottom = (int)((this.Top + this.Height) * dpiY);
            int thresh = 25;

            if (Math.Abs(physLeft - workArea.Left) < thresh) _dockState = DockState.Left;
            else if (Math.Abs(physRight - workArea.Right) < thresh) _dockState = DockState.Right;
            else if (Math.Abs(physTop - workArea.Top) < thresh) _dockState = DockState.Top;
            else if (Math.Abs(physBottom - workArea.Bottom) < thresh) _dockState = DockState.Bottom;
            else _dockState = DockState.Floating;
        }

        private void UpdateDockOrientation()
        {
            if (_dockState == DockState.Left)
            {
                HeaderRowTop.Height = new GridLength(0); HeaderRowBottom.Height = new GridLength(0);
                HeaderColLeft.Width = new GridLength(HEADER_SIZE); HeaderColRight.Width = new GridLength(0);
                Grid.SetRow(HeaderBorder, 0); Grid.SetRowSpan(HeaderBorder, 3); Grid.SetColumn(HeaderBorder, 0); Grid.SetColumnSpan(HeaderBorder, 1);
                HeaderGrid.LayoutTransform = new RotateTransform(-90); HeaderBorder.CornerRadius = new CornerRadius(8, 0, 0, 8);
            }
            else if (_dockState == DockState.Right)
            {
                HeaderRowTop.Height = new GridLength(0); HeaderRowBottom.Height = new GridLength(0);
                HeaderColLeft.Width = new GridLength(0); HeaderColRight.Width = new GridLength(HEADER_SIZE);
                Grid.SetRow(HeaderBorder, 0); Grid.SetRowSpan(HeaderBorder, 3); Grid.SetColumn(HeaderBorder, 2); Grid.SetColumnSpan(HeaderBorder, 1);
                HeaderGrid.LayoutTransform = new RotateTransform(90); HeaderBorder.CornerRadius = new CornerRadius(0, 8, 8, 0);
            }
            else if (_dockState == DockState.Bottom)
            {
                HeaderRowTop.Height = new GridLength(0); HeaderRowBottom.Height = new GridLength(HEADER_SIZE);
                HeaderColLeft.Width = new GridLength(0); HeaderColRight.Width = new GridLength(0);
                Grid.SetRow(HeaderBorder, 2); Grid.SetRowSpan(HeaderBorder, 1); Grid.SetColumn(HeaderBorder, 0); Grid.SetColumnSpan(HeaderBorder, 3);
                HeaderGrid.LayoutTransform = Transform.Identity; HeaderBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);
            }
            else
            {
                HeaderRowTop.Height = new GridLength(HEADER_SIZE); HeaderRowBottom.Height = new GridLength(0);
                HeaderColLeft.Width = new GridLength(0); HeaderColRight.Width = new GridLength(0);
                Grid.SetRow(HeaderBorder, 0); Grid.SetRowSpan(HeaderBorder, 1); Grid.SetColumn(HeaderBorder, 0); Grid.SetColumnSpan(HeaderBorder, 3);
                HeaderGrid.LayoutTransform = Transform.Identity; HeaderBorder.CornerRadius = new CornerRadius(8, 8, 0, 0);
            }
        }

        private void AnimateRollUp()
        {
            double d = 250; QuarticEase ease = new QuarticEase { EasingMode = EasingMode.EaseInOut };
            if (_dockState == DockState.Left)
            {
                this.BeginAnimation(Window.WidthProperty, new DoubleAnimation { To = HEADER_SIZE, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
            }
            else if (_dockState == DockState.Right)
            {
                this.BeginAnimation(Window.WidthProperty, new DoubleAnimation { To = HEADER_SIZE, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
                this.BeginAnimation(Window.LeftProperty, new DoubleAnimation { To = _expandedLeft + (_expandedWidth - HEADER_SIZE), Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
            }
            else if (_dockState == DockState.Bottom)
            {
                this.BeginAnimation(Window.HeightProperty, new DoubleAnimation { To = HEADER_SIZE, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
                this.BeginAnimation(Window.TopProperty, new DoubleAnimation { To = _expandedTop + (_expandedHeight - HEADER_SIZE), Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
            }
            else
            {
                this.BeginAnimation(Window.HeightProperty, new DoubleAnimation { To = HEADER_SIZE, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
            }
        }

        private void AnimateReveal()
        {
            double d = 250; QuarticEase ease = new QuarticEase { EasingMode = EasingMode.EaseInOut };
            if (_dockState == DockState.Left)
            {
                this.BeginAnimation(Window.WidthProperty, new DoubleAnimation { To = _expandedWidth, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
            }
            else if (_dockState == DockState.Right)
            {
                this.BeginAnimation(Window.WidthProperty, new DoubleAnimation { To = _expandedWidth, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
                this.BeginAnimation(Window.LeftProperty, new DoubleAnimation { To = _expandedLeft, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
            }
            else if (_dockState == DockState.Bottom)
            {
                this.BeginAnimation(Window.HeightProperty, new DoubleAnimation { To = _expandedHeight, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
                this.BeginAnimation(Window.TopProperty, new DoubleAnimation { To = _expandedTop, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
            }
            else
            {
                this.BeginAnimation(Window.HeightProperty, new DoubleAnimation { To = _expandedHeight, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2) { ToggleRollUp(); return; }

                this.BeginAnimation(Window.LeftProperty, null); this.BeginAnimation(Window.TopProperty, null);
                this.BeginAnimation(Window.WidthProperty, null); this.BeginAnimation(Window.HeightProperty, null);

                bool wasRolledUpBeforeDrag = _isRolledUp || _isTemporarilyRevealed;

                if (wasRolledUpBeforeDrag)
                {
                    this.Width = _expandedWidth; this.Height = _expandedHeight;
                    if (_dockState == DockState.Right) this.Left = this.Left - (_expandedWidth - HEADER_SIZE);
                    if (_dockState == DockState.Bottom) this.Top = this.Top - (_expandedHeight - HEADER_SIZE);
                }

                _isRolledUp = false; _isTemporarilyRevealed = false;
                _dockState = DockState.Floating; UpdateDockOrientation();

                this.DragMove();

                DetermineDockState(); UpdateDockOrientation();
                _expandedLeft = this.Left; _expandedTop = this.Top; _expandedWidth = this.Width; _expandedHeight = this.Height;

                if (wasRolledUpBeforeDrag)
                {
                    _isRolledUp = true;
                    if (this.IsMouseOver)
                    {
                        _isTemporarilyRevealed = true;
                        if (_dockState == DockState.Left || _dockState == DockState.Right) this.Width = _expandedWidth;
                        else this.Height = _expandedHeight;
                    }
                    else
                    {
                        _isTemporarilyRevealed = false;
                        AnimateRollUp();
                    }
                }
                SaveFenceState();
            }
        }

        private void ToggleRollUp()
        {
            if (_isRolledUp) { AnimateReveal(); _isRolledUp = false; _isTemporarilyRevealed = false; }
            else
            {
                DetermineDockState(); UpdateDockOrientation();
                this.BeginAnimation(Window.HeightProperty, null); this.BeginAnimation(Window.WidthProperty, null);
                this.BeginAnimation(Window.LeftProperty, null); this.BeginAnimation(Window.TopProperty, null);
                _expandedHeight = this.Height; _expandedWidth = this.Width; _expandedLeft = this.Left; _expandedTop = this.Top;
                AnimateRollUp();
                _isRolledUp = true; _isTemporarilyRevealed = false;
            }
            SaveFenceState();
        }

        private void StartNativeResize(int direction)
        {
            if (_isRolledUp && !_isTemporarilyRevealed) return;

            this.BeginAnimation(Window.HeightProperty, null); this.BeginAnimation(Window.WidthProperty, null);
            this.BeginAnimation(Window.LeftProperty, null); this.BeginAnimation(Window.TopProperty, null);

            if (_isRolledUp || _isTemporarilyRevealed) { _isRolledUp = false; _isTemporarilyRevealed = false; }

            ReleaseCapture();
            SendMessage(new WindowInteropHelper(this).Handle, WM_SYSCOMMAND, (IntPtr)(SC_SIZE + direction), IntPtr.Zero);
            DetermineDockState(); UpdateDockOrientation();
            _expandedLeft = this.Left; _expandedTop = this.Top; _expandedWidth = this.Width; _expandedHeight = this.Height;
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

        private void TitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) return;
            e.Handled = true;
            TitleText.Visibility = Visibility.Collapsed;

            TextBox renameBox = new TextBox
            {
                Text = TitleText.Text,
                Width = 140,
                Height = 22,
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Left,
                FontFamily = TitleText.FontFamily,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(2, 0, 0, 0)
            };

            TitleStack.Children.Add(renameBox);
            renameBox.Focus();
            renameBox.SelectAll();

            renameBox.KeyDown += (s2, e2) => {
                if (e2.Key == Key.Enter)
                {
                    TitleText.Text = renameBox.Text;
                    TitleStack.Children.Remove(renameBox);
                    TitleText.Visibility = Visibility.Visible;
                    SaveFenceState();
                }
                else if (e2.Key == Key.Escape)
                {
                    TitleStack.Children.Remove(renameBox);
                    TitleText.Visibility = Visibility.Visible;
                }
            };

            renameBox.LostFocus += (s2, e2) => {
                if (TitleStack.Children.Contains(renameBox))
                {
                    TitleText.Text = renameBox.Text;
                    TitleStack.Children.Remove(renameBox);
                    TitleText.Visibility = Visibility.Visible;
                    SaveFenceState();
                }
            };
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e) { if (_isRolledUp && !_isTemporarilyRevealed) { AnimateReveal(); _isTemporarilyRevealed = true; } }
        private void Window_MouseLeave(object sender, MouseEventArgs e) { if (_isContextMenuOpen || _isDraggingSelectionBox) return; if (_isRolledUp && _isTemporarilyRevealed) { AnimateRollUp(); _isTemporarilyRevealed = false; } }
        private void Window_DragEnter(object sender, DragEventArgs e) { if (_isRolledUp && !_isTemporarilyRevealed) { AnimateReveal(); _isTemporarilyRevealed = true; } }
        private void Window_DragOver(object sender, DragEventArgs e) { if (_isRolledUp && !_isTemporarilyRevealed) { AnimateReveal(); _isTemporarilyRevealed = true; } }
        private void Window_DragLeave(object sender, DragEventArgs e) { Point pt = e.GetPosition(this); if (pt.X <= 2 || pt.Y <= 2 || pt.X >= this.ActualWidth - 2 || pt.Y >= this.ActualHeight - 2) { if (_isRolledUp && _isTemporarilyRevealed) { AnimateRollUp(); _isTemporarilyRevealed = false; } } }
        private void HandleMenuClosed() { _isContextMenuOpen = false; if (!this.IsMouseOver && _isRolledUp && _isTemporarilyRevealed && !_isDraggingSelectionBox) { AnimateRollUp(); _isTemporarilyRevealed = false; } }
        private void MenuIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { e.Handled = true; HeaderContextMenu.IsOpen = true; }
        private void Menu_NewFence_Click(object sender, RoutedEventArgs e) { MainWindow newFence = new MainWindow(Guid.NewGuid().ToString()); newFence.Left = this.Left + 50; newFence.Top = this.Top + 50; newFence.Show(); }
        private void Menu_DeleteFence_Click(object sender, RoutedEventArgs e) { if (MessageBox.Show("Permanently delete this fence?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) { _isDeleted = true; if (File.Exists(_saveFilePath)) File.Delete(_saveFilePath); this.Close(); } }
        private void Menu_RollUp_Click(object sender, RoutedEventArgs e) { ToggleRollUp(); }
        private void Menu_Sort_Click(object sender, RoutedEventArgs e) { if (sender is not MenuItem clickedSort || clickedSort.Tag == null) return; _saveSortMethod = clickedSort.Tag.ToString() ?? "None"; ApplySorting(); SaveFenceState(); }
        private void Menu_Settings_Click(object sender, RoutedEventArgs e) { SettingsWindow settings = new SettingsWindow(this); settings.Owner = this; settings.ShowDialog(); }
        private void Menu_CopyColor_Click(object sender, RoutedEventArgs e) { string hexColor = $"#{_currentFenceColor.R:X2}{_currentFenceColor.G:X2}{_currentFenceColor.B:X2}"; for (int i = 0; i < 10; i++) { try { Clipboard.SetText(hexColor); return; } catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(10); } } }
        private void Menu_Opacity_Click(object sender, RoutedEventArgs e) { if (sender is not MenuItem clickedItem || clickedItem.Tag == null) return; if (double.TryParse(clickedItem.Tag.ToString(), out double newOpacity)) { ApplyAcrylicBlur(_currentFenceColor, newOpacity); SaveFenceState(); } }
        private void Menu_EditColor_Click(object sender, RoutedEventArgs e) { SolidColorBrush currentBrush = new SolidColorBrush(_currentFenceColor) { Opacity = _currentOpacity }; ColorPickerWindow picker = new ColorPickerWindow(currentBrush); picker.Owner = this; if (picker.ShowDialog() == true && picker.SelectedBrush != null) { ApplyAcrylicBlur(picker.SelectedBrush.Color, picker.SelectedBrush.Opacity); SaveFenceState(); } }

        private void ApplySorting() { if (_saveSortMethod != "None" && _saveSortMethod != "DateAdd") { try { switch (_saveSortMethod) { case "Name": _currentFiles.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase)); break; case "Size": _currentFiles.Sort((a, b) => GetFileSize(b).CompareTo(GetFileSize(a))); break; case "Type": _currentFiles.Sort((a, b) => string.Compare(Path.GetExtension(a), Path.GetExtension(b), StringComparison.OrdinalIgnoreCase)); break; case "DateMod": _currentFiles.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a))); break; case "DateCre": _currentFiles.Sort((a, b) => File.GetCreationTime(b).CompareTo(File.GetCreationTime(a))); break; } } catch { } } RefreshIconUI(); }
        private long GetFileSize(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }
        private void RefreshIconUI() { IconPanel.Children.Clear(); ClearSelection(); if (_currentFiles.Count == 0) { IconPanel.Children.Add(new TextBlock { Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#88FFFFFF")!, Text = "Drop shortcuts here..." }); return; } foreach (string file in _currentFiles) { AddIconToUI(file); } }
        private void Window_Drop(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files != null) { foreach (string file in files) { if (!_currentFiles.Contains(file)) { _currentFiles.Add(file); } } ApplySorting(); SaveFenceState(); } string? sourceId = e.Data.GetData("FenceSourceId") as string; if (!string.IsNullOrEmpty(sourceId) && sourceId != _fenceId) e.Effects = DragDropEffects.Move; else e.Effects = DragDropEffects.None; } }

        private ImageSource? GetHighResIcon(string filePath)
        {
            try
            {
                Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30d463b");
                SHCreateItemFromParsingName(filePath, IntPtr.Zero, iid, out IShellItemImageFactory factory);
                factory.GetImage(new NativeSize { Width = 128, Height = 128 }, 0x0, out IntPtr hbitmap);
                ImageSource imgSrc = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                DeleteObject(hbitmap);
                return imgSrc;
            }
            catch
            {
                try { using (System.Drawing.Icon? sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath)) { if (sysIcon == null) return null; return Imaging.CreateBitmapSourceFromHIcon(sysIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); } } catch { return null; }
            }
        }

        private void AddIconToUI(string file)
        {
            string currentFilePath = file;
            StackPanel itemContainer = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8), Width = 72, ToolTip = currentFilePath, Background = System.Windows.Media.Brushes.Transparent };
            System.Windows.Controls.Image iconImage = new System.Windows.Controls.Image { Source = GetHighResIcon(currentFilePath), Width = 48, Height = 48, Margin = new Thickness(0, 0, 0, 5) };
            TextBlock textBlock = new TextBlock { Text = Path.GetFileNameWithoutExtension(currentFilePath), Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, FontSize = 11, TextWrapping = TextWrapping.Wrap };

            ContextMenu rightClickMenu = new ContextMenu();
            rightClickMenu.Opened += ContextMenu_Opened_ApplyBlur;

            // FIXED: Hooked the Submenu detector into the individual icon menus!
            rightClickMenu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(Submenu_Opened_ApplyBlur));

            MenuItem renameItem = new MenuItem { Header = "Rename File" }; MenuItem removeItem = new MenuItem { Header = "Remove from Fence" };
            removeItem.Click += (s, args) => { _currentFiles.Remove(currentFilePath); ApplySorting(); SaveFenceState(); };

            Action triggerRename = () => {
                textBlock.Visibility = Visibility.Collapsed;
                TextBox renameBox = new TextBox
                {
                    Text = textBlock.Text,
                    Width = 72,
                    Margin = new Thickness(-5, 0, -5, 0),
                    Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
                itemContainer.Children.Add(renameBox);
                renameBox.Focus();
                renameBox.SelectAll();

                renameBox.KeyDown += (s2, e2) => {
                    if (e2.Key == Key.Enter)
                    {
                        try
                        {
                            string? dir = Path.GetDirectoryName(currentFilePath);
                            string ext = Path.GetExtension(currentFilePath);
                            if (dir != null)
                            {
                                string newPath = Path.Combine(dir, renameBox.Text + ext);
                                if (currentFilePath != newPath && !File.Exists(newPath))
                                {
                                    File.Move(currentFilePath, newPath);
                                    _currentFiles.Remove(currentFilePath);
                                    _currentFiles.Add(newPath);
                                    ApplySorting();
                                    SaveFenceState();
                                }
                                else
                                {
                                    itemContainer.Children.Remove(renameBox);
                                    textBlock.Visibility = Visibility.Visible;
                                }
                            }
                        }
                        catch
                        {
                            MessageBox.Show("Could not rename file.", "Error");
                            itemContainer.Children.Remove(renameBox);
                            textBlock.Visibility = Visibility.Visible;
                        }
                    }
                    else if (e2.Key == Key.Escape)
                    {
                        itemContainer.Children.Remove(renameBox);
                        textBlock.Visibility = Visibility.Visible;
                    }
                };

                renameBox.LostFocus += (s2, e2) => {
                    if (itemContainer.Children.Contains(renameBox))
                    {
                        itemContainer.Children.Remove(renameBox);
                        textBlock.Visibility = Visibility.Visible;
                    }
                };
            };

            renameItem.Click += (s, args) => triggerRename();
            rightClickMenu.Items.Add(renameItem); rightClickMenu.Items.Add(removeItem); itemContainer.ContextMenu = rightClickMenu; rightClickMenu.Opened += (s, args) => _isContextMenuOpen = true; rightClickMenu.Closed += (s, args) => HandleMenuClosed();

            textBlock.MouseLeftButtonDown += (s, args) => {
                if (args.ClickCount == 2) return;
                args.Handled = true;
                triggerRename();
            };

            System.Windows.Point? dragStartPoint = null;
            itemContainer.PreviewMouseLeftButtonDown += (s, args) => { dragStartPoint = args.GetPosition(null); bool isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl); if (isCtrlPressed) { if (_selectedItems.Contains(itemContainer)) { itemContainer.Background = System.Windows.Media.Brushes.Transparent; _selectedItems.Remove(itemContainer); } else { itemContainer.Background = _highlightBrush; _selectedItems.Add(itemContainer); } } else if (!_selectedItems.Contains(itemContainer)) { ClearSelection(); itemContainer.Background = _highlightBrush; _selectedItems.Add(itemContainer); } };
            itemContainer.PreviewMouseMove += (s, args) => { if (args.LeftButton == MouseButtonState.Pressed && dragStartPoint.HasValue) { System.Windows.Point currentPoint = args.GetPosition(null); if (Math.Abs(currentPoint.X - dragStartPoint.Value.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(currentPoint.Y - dragStartPoint.Value.Y) > SystemParameters.MinimumVerticalDragDistance) { DataObject dragData = new DataObject(DataFormats.FileDrop, new string[] { currentFilePath }); dragData.SetData("FenceSourceId", _fenceId); DragDropEffects result = DragDrop.DoDragDrop(itemContainer, dragData, DragDropEffects.Move | DragDropEffects.Copy); if (result == DragDropEffects.Move) { _currentFiles.Remove(currentFilePath); ApplySorting(); SaveFenceState(); } dragStartPoint = null; } } };
            itemContainer.MouseDown += (s, args) => { if (args.ClickCount == 2 && args.ChangedButton == MouseButton.Left) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = currentFilePath, UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show($"Could not open file.\nError: {ex.Message}", "Launch Error"); } } };
            itemContainer.Children.Add(iconImage); itemContainer.Children.Add(textBlock); IconPanel.Children.Add(itemContainer);
        }

        private ImageSource? GetSystemIcon(string filePath) { try { using (System.Drawing.Icon? sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath)) { if (sysIcon == null) return null; return Imaging.CreateBitmapSourceFromHIcon(sysIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); } } catch { return null; } }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadFenceState();
            ApplyAcrylicBlur(_currentFenceColor, _currentOpacity);

            IntPtr myWindowHandle = new WindowInteropHelper(this).Handle; SetWindowPos(myWindowHandle, (IntPtr)HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            HwndSource? source = HwndSource.FromHwnd(myWindowHandle); if (source != null) source.AddHook(new HwndSourceHook(WndProc));
            this.PreviewKeyDown += Window_PreviewKeyDown;
            HeaderContextMenu.Opened += (s, args) => _isContextMenuOpen = true; HeaderContextMenu.Closed += (s, args) => HandleMenuClosed();
            this.PreviewMouseRightButtonDown += (s, args) => _isContextMenuOpen = true;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WINDOWPOSCHANGING)
            {
                WINDOWPOS windowPos = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS))!; windowPos.hwndInsertAfter = (IntPtr)HWND_BOTTOM;
                if ((windowPos.flags & SWP_NOMOVE) == 0 && (windowPos.flags & SWP_NOSIZE) != 0)
                {
                    int snapMargin = 20; var screen = System.Windows.Forms.Screen.FromHandle(hwnd); var workArea = screen.WorkingArea;
                    if (Math.Abs(windowPos.x - workArea.Left) < snapMargin) windowPos.x = workArea.Left;
                    if (Math.Abs(windowPos.y - workArea.Top) < snapMargin) windowPos.y = workArea.Top;
                    if (Math.Abs((windowPos.x + windowPos.cx) - workArea.Right) < snapMargin) windowPos.x = workArea.Right - windowPos.cx;
                    if (Math.Abs((windowPos.y + windowPos.cy) - workArea.Bottom) < snapMargin) windowPos.y = workArea.Bottom - windowPos.cy;
                }
                Marshal.StructureToPtr(windowPos, lParam, true);
            }
            return IntPtr.Zero;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle); var workArea = screen.WorkingArea;
                PresentationSource? source = PresentationSource.FromVisual(this); double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0; double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
                double logicalLeft = workArea.Left / dpiX; double logicalTop = workArea.Top / dpiY; double logicalRight = workArea.Right / dpiX; double logicalBottom = workArea.Bottom / dpiY;
                bool wasHandled = false;

                this.BeginAnimation(Window.LeftProperty, null); this.BeginAnimation(Window.TopProperty, null); this.BeginAnimation(Window.WidthProperty, null); this.BeginAnimation(Window.HeightProperty, null);
                if (_isRolledUp) { _isRolledUp = false; _isTemporarilyRevealed = false; this.Width = _expandedWidth; this.Height = _expandedHeight; }

                if (e.Key == Key.Up) { this.Top = logicalTop; wasHandled = true; }
                else if (e.Key == Key.Down) { this.Top = logicalBottom - this.Height; wasHandled = true; }
                else if (e.Key == Key.Left) { this.Left = logicalLeft; wasHandled = true; }
                else if (e.Key == Key.Right) { this.Left = logicalRight - this.Width; wasHandled = true; }

                if (wasHandled) { e.Handled = true; DetermineDockState(); UpdateDockOrientation(); _expandedLeft = this.Left; _expandedTop = this.Top; _expandedWidth = this.Width; _expandedHeight = this.Height; SaveFenceState(); }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { if (!_isDeleted) SaveFenceState(); base.OnClosing(e); }

        private void ClearSelection() { foreach (var item in _selectedItems) item.Background = System.Windows.Media.Brushes.Transparent; _selectedItems.Clear(); }
        private void IconPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is ScrollViewer || e.OriginalSource is WrapPanel) { ClearSelection(); _selectionStartPoint = e.GetPosition(SelectionCanvas); _isDraggingSelectionBox = true; SelectionBox.Visibility = Visibility.Visible; SelectionBox.Width = 0; SelectionBox.Height = 0; Canvas.SetLeft(SelectionBox, _selectionStartPoint.X); Canvas.SetTop(SelectionBox, _selectionStartPoint.Y); IconPanel.CaptureMouse(); } }
        private void IconPanel_MouseMove(object sender, MouseEventArgs e) { if (_isDraggingSelectionBox) { System.Windows.Point currentPoint = e.GetPosition(SelectionCanvas); double x = Math.Min(currentPoint.X, _selectionStartPoint.X); double y = Math.Min(currentPoint.Y, _selectionStartPoint.Y); double width = Math.Abs(currentPoint.X - _selectionStartPoint.X); double height = Math.Abs(currentPoint.Y - _selectionStartPoint.Y); Canvas.SetLeft(SelectionBox, x); Canvas.SetTop(SelectionBox, y); SelectionBox.Width = width; SelectionBox.Height = height; Rect selectionRect = new Rect(x, y, width, height); foreach (UIElement child in IconPanel.Children) { if (child is StackPanel item) { System.Windows.Point itemPos = item.TranslatePoint(new System.Windows.Point(0, 0), SelectionCanvas); Rect itemRect = new Rect(itemPos, new Size(item.ActualWidth, item.ActualHeight)); if (selectionRect.IntersectsWith(itemRect)) { if (!_selectedItems.Contains(item)) { item.Background = _highlightBrush; _selectedItems.Add(item); } } else if (_selectedItems.Contains(item)) { item.Background = System.Windows.Media.Brushes.Transparent; _selectedItems.Remove(item); } } } } }
        private void IconPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { if (_isDraggingSelectionBox) { _isDraggingSelectionBox = false; SelectionBox.Visibility = Visibility.Collapsed; IconPanel.ReleaseMouseCapture(); } }
    }

    public class FenceData
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsRolledUp { get; set; } = false;

        public double ExpandedHeight { get; set; } = 300;
        public double ExpandedWidth { get; set; } = 250;
        public double ExpandedLeft { get; set; } = 0;
        public double ExpandedTop { get; set; } = 0;

        public string Title { get; set; } = "Fluid Fence"; public string HexColor { get; set; } = "#000000";
        public double Opacity { get; set; } = 0.7; public string SortMethod { get; set; } = "None";
        public string AutoSortExtensions { get; set; } = "";
        public List<string> Files { get; set; } = new List<string>();
    }
}