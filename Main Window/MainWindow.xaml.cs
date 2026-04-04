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
using System.Windows.Controls.Primitives;

namespace DesktopFences
{
    public partial class MainWindow : Window
    {
        private const int HWND_BOTTOM = 1;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int WM_WINDOWPOSCHANGING = 0x0046;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS { public IntPtr hwnd; public IntPtr hwndInsertAfter; public int x; public int y; public int cx; public int cy; public uint flags; }

        private List<string> _currentFiles = new List<string>();
        private readonly string _saveDirectory;
        private readonly string _saveFilePath;
        private readonly string _fenceId;

        private bool _isRolledUp = false;
        private double _expandedHeight = 300;
        private bool _isTemporarilyRevealed = false;
        private string _saveSortMethod = "None";

        private List<StackPanel> _selectedItems = new List<StackPanel>();
        private System.Windows.Point _selectionStartPoint;
        private bool _isDraggingSelectionBox = false;
        private SolidColorBrush _highlightBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(85, 0, 120, 215));

        public MainWindow() { InitializeComponent(); }

        public MainWindow(string fenceId) : this()
        {
            _fenceId = fenceId;
            _saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences", "Fences");
            _saveFilePath = Path.Combine(_saveDirectory, $"{_fenceId}.json");
        }

        private void AnimateWindowHeight(double targetHeight)
        {
            DoubleAnimation heightAnimation = new DoubleAnimation
            {
                To = targetHeight,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
            };
            this.BeginAnimation(Window.HeightProperty, heightAnimation);
        }

        // ----------------------------------------------------------------------
        // 1. STATE SAVING & LOADING
        // ----------------------------------------------------------------------
        private void LoadFenceState()
        {
            if (File.Exists(_saveFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_saveFilePath);
                    FenceData data = JsonSerializer.Deserialize<FenceData>(json);

                    if (data != null)
                    {
                        this.Left = data.Left; this.Top = data.Top; this.Width = data.Width;

                        _isRolledUp = data.IsRolledUp;
                        _expandedHeight = data.ExpandedHeight;
                        TitleText.Text = data.Title;
                        _saveSortMethod = data.SortMethod;

                        // Apply the color and the saved Opacity!
                        SolidColorBrush savedBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#B2" + data.HexColor.Replace("#", ""));
                        savedBrush.Opacity = data.Opacity;
                        MainBorder.Background = savedBrush;

                        if (_isRolledUp) this.Height = 55;
                        else this.Height = data.Height;

                        IconPanel.Children.Clear();
                        foreach (string file in data.Files)
                        {
                            if (File.Exists(file) || Directory.Exists(file)) { AddIconToUI(file); _currentFiles.Add(file); }
                        }
                    }
                }
                catch { }
            }
        }

        private void SaveFenceState()
        {
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
                    Title = TitleText.Text,
                    HexColor = "#" + MainBorder.Background.ToString().Substring(3),
                    Opacity = MainBorder.Background.Opacity,
                    SortMethod = _saveSortMethod,
                    Files = _currentFiles
                };

                Directory.CreateDirectory(_saveDirectory);
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_saveFilePath, json);
            }
            catch { }
        }

        // ----------------------------------------------------------------------
        // 2. WINDOW DRAGGING, ROLL-UP, REVEAL, & RENAMING
        // ----------------------------------------------------------------------
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    ToggleRollUp();
                    return;
                }
                this.DragMove();
                SaveFenceState();
            }
        }

        private void ToggleRollUp()
        {
            if (_isRolledUp)
            {
                AnimateWindowHeight(_expandedHeight);
                _isRolledUp = false;
                _isTemporarilyRevealed = false;
            }
            else
            {
                this.BeginAnimation(Window.HeightProperty, null);
                _expandedHeight = this.Height;
                AnimateWindowHeight(55);
                _isRolledUp = true;
                _isTemporarilyRevealed = false;
            }
            SaveFenceState();
        }

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
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                FontFamily = TitleText.FontFamily,
                Padding = new Thickness(0, 1, 0, 0)
            };

            HeaderGrid.Children.Add(renameBox);
            renameBox.Focus(); renameBox.SelectAll();

            renameBox.KeyDown += (s2, e2) =>
            {
                if (e2.Key == Key.Enter)
                {
                    TitleText.Text = renameBox.Text; HeaderGrid.Children.Remove(renameBox); TitleText.Visibility = Visibility.Visible; SaveFenceState();
                }
                else if (e2.Key == Key.Escape) { HeaderGrid.Children.Remove(renameBox); TitleText.Visibility = Visibility.Visible; }
            };

            renameBox.LostFocus += (s2, e2) =>
            {
                if (HeaderGrid.Children.Contains(renameBox))
                { TitleText.Text = renameBox.Text; HeaderGrid.Children.Remove(renameBox); TitleText.Visibility = Visibility.Visible; SaveFenceState(); }
            };
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_isRolledUp && !_isTemporarilyRevealed) { AnimateWindowHeight(_expandedHeight); _isTemporarilyRevealed = true; }
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isRolledUp && _isTemporarilyRevealed) { AnimateWindowHeight(55); _isTemporarilyRevealed = false; }
        }

        private void MenuIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { e.Handled = true; HeaderContextMenu.IsOpen = true; }

        // ----------------------------------------------------------------------
        // 3. FENCE MANAGEMENT & NEW MENUS
        // ----------------------------------------------------------------------
        private void Menu_NewFence_Click(object sender, RoutedEventArgs e)
        {
            MainWindow newFence = new MainWindow(Guid.NewGuid().ToString());
            newFence.Left = this.Left + 50; newFence.Top = this.Top + 50; newFence.Show();
        }

        private void Menu_DeleteFence_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Permanently delete this fence?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                if (File.Exists(_saveFilePath)) File.Delete(_saveFilePath);
                this.Close();
            }
        }

        private void Menu_RollUp_Click(object sender, RoutedEventArgs e) { ToggleRollUp(); }

        private void Menu_Opacity_Click(object sender, RoutedEventArgs e)
        {
            MenuItem clickedItem = (MenuItem)sender;
            double newOpacity = double.Parse(clickedItem.Tag.ToString());

            SolidColorBrush newBrush = (SolidColorBrush)MainBorder.Background.Clone();
            newBrush.Opacity = newOpacity;

            MainBorder.Background = newBrush;
            SaveFenceState();
        }

        private void Menu_Sort_Click(object sender, RoutedEventArgs e)
        {
            MenuItem clickedSort = (MenuItem)sender;
            _saveSortMethod = clickedSort.Tag.ToString();
            SaveFenceState();
        }

        private void Menu_CopyColor_Click(object sender, RoutedEventArgs e)
        {
            string currentHex = "#" + MainBorder.Background.ToString().Substring(3);
            Clipboard.SetText(currentHex);
        }

        private void Menu_EditColor_Click(object sender, RoutedEventArgs e)
        {
            // Pass the current background brush to the new Color Picker so it starts on the correct color
            SolidColorBrush currentBrush = (SolidColorBrush)MainBorder.Background;

            ColorPickerWindow picker = new ColorPickerWindow(currentBrush);
            picker.Owner = this; // Centers the popup nicely over the fence

            // If the user tweaked the sliders and clicked 'Save Color'...
            if (picker.ShowDialog() == true)
            {
                // Grab the brush directly from the picker (it already has the color and opacity baked in)
                MainBorder.Background = picker.SelectedBrush;
                SaveFenceState();
            }
        }

        private void Menu_Settings_Click(object sender, RoutedEventArgs e)
        {
            string currentHex = "#" + MainBorder.Background.ToString().Substring(3);
            SettingsWindow settings = new SettingsWindow(TitleText.Text, currentHex);

            if (settings.ShowDialog() == true)
            {
                TitleText.Text = settings.SelectedTitle;
                SolidColorBrush newBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#B2" + settings.SelectedHexColor.Replace("#", ""));
                newBrush.Opacity = MainBorder.Background.Opacity;
                MainBorder.Background = newBrush;
                SaveFenceState();
            }
        }

        // ----------------------------------------------------------------------
        // 4. DRAG AND DROP & UI GENERATION
        // ----------------------------------------------------------------------
        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (IconPanel.Children.Count == 1 && IconPanel.Children[0] is TextBlock placeholder && placeholder.Text.Contains("Drop shortcuts"))
                    IconPanel.Children.Clear();

                foreach (string file in files)
                {
                    if (!_currentFiles.Contains(file)) { AddIconToUI(file); _currentFiles.Add(file); }
                }
                SaveFenceState();

                if (e.Data.GetDataPresent("FenceSourceId") && (string)e.Data.GetData("FenceSourceId") != _fenceId)
                    e.Effects = DragDropEffects.Move;
                else
                    e.Effects = DragDropEffects.None;
            }
        }

        private void AddIconToUI(string file)
        {
            string currentFilePath = file;

            StackPanel itemContainer = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(10),
                Width = 64,
                ToolTip = currentFilePath,
                Background = System.Windows.Media.Brushes.Transparent
            };

            System.Windows.Controls.Image iconImage = new System.Windows.Controls.Image { Source = GetSystemIcon(currentFilePath), Width = 32, Height = 32, Margin = new Thickness(0, 0, 0, 5) };

            TextBlock textBlock = new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(currentFilePath),
                Foreground = System.Windows.Media.Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };

            ContextMenu rightClickMenu = new ContextMenu();
            MenuItem renameItem = new MenuItem { Header = "Rename File" };
            MenuItem removeItem = new MenuItem { Header = "Remove from Fence" };

            removeItem.Click += (s, args) => { IconPanel.Children.Remove(itemContainer); _currentFiles.Remove(currentFilePath); SaveFenceState(); };

            renameItem.Click += (s, args) =>
            {
                textBlock.Visibility = Visibility.Collapsed;
                TextBox renameBox = new TextBox { Text = textBlock.Text, Width = 64, Margin = new Thickness(-5, 0, -5, 0), TextAlignment = TextAlignment.Center };
                itemContainer.Children.Add(renameBox);
                renameBox.Focus(); renameBox.SelectAll();

                renameBox.KeyDown += (s2, e2) =>
                {
                    if (e2.Key == Key.Enter)
                    {
                        try
                        {
                            string dir = Path.GetDirectoryName(currentFilePath); string ext = Path.GetExtension(currentFilePath);
                            string newPath = Path.Combine(dir, renameBox.Text + ext);

                            if (currentFilePath != newPath && !File.Exists(newPath))
                            {
                                File.Move(currentFilePath, newPath);
                                _currentFiles.Remove(currentFilePath); _currentFiles.Add(newPath);
                                currentFilePath = newPath; itemContainer.ToolTip = currentFilePath; SaveFenceState();
                            }
                            textBlock.Text = renameBox.Text;
                        }
                        catch { MessageBox.Show("Could not rename file.", "Error"); }

                        itemContainer.Children.Remove(renameBox); textBlock.Visibility = Visibility.Visible;
                    }
                    else if (e2.Key == Key.Escape) { itemContainer.Children.Remove(renameBox); textBlock.Visibility = Visibility.Visible; }
                };

                renameBox.LostFocus += (s2, e2) => { if (itemContainer.Children.Contains(renameBox)) { itemContainer.Children.Remove(renameBox); textBlock.Visibility = Visibility.Visible; } };
            };

            rightClickMenu.Items.Add(renameItem); rightClickMenu.Items.Add(removeItem);
            itemContainer.ContextMenu = rightClickMenu;

            System.Windows.Point? dragStartPoint = null;

            itemContainer.PreviewMouseLeftButtonDown += (s, args) =>
            {
                dragStartPoint = args.GetPosition(null);
                bool isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

                if (isCtrlPressed)
                {
                    if (_selectedItems.Contains(itemContainer)) { itemContainer.Background = System.Windows.Media.Brushes.Transparent; _selectedItems.Remove(itemContainer); }
                    else { itemContainer.Background = _highlightBrush; _selectedItems.Add(itemContainer); }
                }
                else if (!_selectedItems.Contains(itemContainer))
                {
                    ClearSelection(); itemContainer.Background = _highlightBrush; _selectedItems.Add(itemContainer);
                }
            };

            itemContainer.PreviewMouseMove += (s, args) =>
            {
                if (args.LeftButton == MouseButtonState.Pressed && dragStartPoint.HasValue)
                {
                    System.Windows.Point currentPoint = args.GetPosition(null);
                    if (Math.Abs(currentPoint.X - dragStartPoint.Value.X) > SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(currentPoint.Y - dragStartPoint.Value.Y) > SystemParameters.MinimumVerticalDragDistance)
                    {
                        DataObject dragData = new DataObject(DataFormats.FileDrop, new string[] { currentFilePath });
                        dragData.SetData("FenceSourceId", _fenceId);

                        DragDropEffects result = DragDrop.DoDragDrop(itemContainer, dragData, DragDropEffects.Move | DragDropEffects.Copy);
                        if (result == DragDropEffects.Move) { IconPanel.Children.Remove(itemContainer); _currentFiles.Remove(currentFilePath); SaveFenceState(); }
                        dragStartPoint = null;
                    }
                }
            };

            itemContainer.MouseDown += (s, args) =>
            {
                if (args.ClickCount == 2 && args.ChangedButton == MouseButton.Left)
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = currentFilePath, UseShellExecute = true }); }
                    catch (Exception ex) { MessageBox.Show($"Could not open file.\nError: {ex.Message}", "Launch Error"); }
                }
            };

            itemContainer.Children.Add(iconImage); itemContainer.Children.Add(textBlock);
            IconPanel.Children.Add(itemContainer);
        }

        private ImageSource GetSystemIcon(string filePath)
        {
            try
            {
                using (System.Drawing.Icon sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath))
                {
                    if (sysIcon == null) return null;
                    return Imaging.CreateBitmapSourceFromHIcon(sysIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch { return null; }
        }

        // ----------------------------------------------------------------------
        // 5. WINDOW LOADED & MAGNETIC SNAPPING
        // ----------------------------------------------------------------------
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadFenceState();
            IntPtr myWindowHandle = new WindowInteropHelper(this).Handle;
            SetWindowPos(myWindowHandle, (IntPtr)HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            HwndSource source = HwndSource.FromHwnd(myWindowHandle);
            source.AddHook(new HwndSourceHook(WndProc));
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WINDOWPOSCHANGING)
            {
                WINDOWPOS windowPos = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS));
                windowPos.hwndInsertAfter = (IntPtr)HWND_BOTTOM;

                if ((windowPos.flags & SWP_NOMOVE) == 0)
                {
                    int snapMargin = 10;
                    var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
                    var workArea = screen.WorkingArea;

                    if (Math.Abs(windowPos.x - workArea.Left) < snapMargin) windowPos.x = workArea.Left;
                    if (Math.Abs(windowPos.y - workArea.Top) < snapMargin) windowPos.y = workArea.Top;
                    if (Math.Abs((windowPos.x + windowPos.cx) - workArea.Right) < snapMargin) windowPos.x = workArea.Right - windowPos.cx;
                    if (Math.Abs((windowPos.y + windowPos.cy) - workArea.Bottom) < snapMargin) windowPos.y = workArea.Bottom - windowPos.cy;

                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window is MainWindow otherFence && otherFence != this && otherFence.Visibility == Visibility.Visible)
                        {
                            PresentationSource source = PresentationSource.FromVisual(otherFence);
                            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                            int otherLeft = (int)(otherFence.Left * dpiX); int otherTop = (int)(otherFence.Top * dpiY);
                            int otherWidth = (int)(otherFence.Width * dpiX); int otherHeight = (int)(otherFence.Height * dpiY);
                            int otherRight = otherLeft + otherWidth; int otherBottom = otherTop + otherHeight;

                            if (Math.Abs(windowPos.x - otherRight) < snapMargin) windowPos.x = otherRight;
                            if (Math.Abs((windowPos.x + windowPos.cx) - otherLeft) < snapMargin) windowPos.x = otherLeft - windowPos.cx;
                            if (Math.Abs(windowPos.y - otherBottom) < snapMargin) windowPos.y = otherBottom;
                            if (Math.Abs((windowPos.y + windowPos.cy) - otherTop) < snapMargin) windowPos.y = otherTop - windowPos.cy;
                        }
                    }
                }
                Marshal.StructureToPtr(windowPos, lParam, true);
            }
            return IntPtr.Zero;
        }

        // ----------------------------------------------------------------------
        // 6. RESIZING & RUBBER BAND SELECTION LOGIC
        // ----------------------------------------------------------------------
        private void ResizeRight_DragDelta(object sender, DragDeltaEventArgs e) { double newWidth = this.Width + e.HorizontalChange; if (newWidth > 150) this.Width = newWidth; }
        private void ResizeLeft_DragDelta(object sender, DragDeltaEventArgs e) { double newWidth = this.Width - e.HorizontalChange; if (newWidth > 150) { this.Left += e.HorizontalChange; this.Width = newWidth; } }

        private void ResizeBottom_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_isRolledUp) return;
            this.BeginAnimation(Window.HeightProperty, null);
            double newHeight = this.Height + e.VerticalChange;
            if (newHeight > 100) { this.Height = newHeight; _expandedHeight = newHeight; }
        }

        private void ResizeTop_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_isRolledUp) return;
            this.BeginAnimation(Window.HeightProperty, null);
            double newHeight = this.Height - e.VerticalChange;
            if (newHeight > 100) { this.Top += e.VerticalChange; this.Height = newHeight; _expandedHeight = newHeight; }
        }

        private void ResizeTopLeft_DragDelta(object sender, DragDeltaEventArgs e) { ResizeTop_DragDelta(sender, e); ResizeLeft_DragDelta(sender, e); }
        private void ResizeTopRight_DragDelta(object sender, DragDeltaEventArgs e) { ResizeTop_DragDelta(sender, e); ResizeRight_DragDelta(sender, e); }
        private void ResizeBottomLeft_DragDelta(object sender, DragDeltaEventArgs e) { ResizeBottom_DragDelta(sender, e); ResizeLeft_DragDelta(sender, e); }
        private void ResizeBottomRight_DragDelta(object sender, DragDeltaEventArgs e) { ResizeBottom_DragDelta(sender, e); ResizeRight_DragDelta(sender, e); }
        private void Resize_DragCompleted(object sender, DragCompletedEventArgs e) { SaveFenceState(); }

        private void ClearSelection() { foreach (var item in _selectedItems) item.Background = System.Windows.Media.Brushes.Transparent; _selectedItems.Clear(); }

        private void IconPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is ScrollViewer || e.OriginalSource is WrapPanel)
            {
                ClearSelection();
                _selectionStartPoint = e.GetPosition(SelectionCanvas);
                _isDraggingSelectionBox = true;
                SelectionBox.Visibility = Visibility.Visible; SelectionBox.Width = 0; SelectionBox.Height = 0;
                Canvas.SetLeft(SelectionBox, _selectionStartPoint.X); Canvas.SetTop(SelectionBox, _selectionStartPoint.Y);
                IconPanel.CaptureMouse();
            }
        }

        private void IconPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingSelectionBox)
            {
                System.Windows.Point currentPoint = e.GetPosition(SelectionCanvas);
                double x = Math.Min(currentPoint.X, _selectionStartPoint.X); double y = Math.Min(currentPoint.Y, _selectionStartPoint.Y);
                double width = Math.Abs(currentPoint.X - _selectionStartPoint.X); double height = Math.Abs(currentPoint.Y - _selectionStartPoint.Y);

                Canvas.SetLeft(SelectionBox, x); Canvas.SetTop(SelectionBox, y);
                SelectionBox.Width = width; SelectionBox.Height = height;

                Rect selectionRect = new Rect(x, y, width, height);

                foreach (UIElement child in IconPanel.Children)
                {
                    if (child is StackPanel item)
                    {
                        System.Windows.Point itemPos = item.TranslatePoint(new System.Windows.Point(0, 0), SelectionCanvas);
                        Rect itemRect = new Rect(itemPos, new Size(item.ActualWidth, item.ActualHeight));

                        if (selectionRect.IntersectsWith(itemRect))
                        {
                            if (!_selectedItems.Contains(item)) { item.Background = _highlightBrush; _selectedItems.Add(item); }
                        }
                        else if (_selectedItems.Contains(item)) { item.Background = System.Windows.Media.Brushes.Transparent; _selectedItems.Remove(item); }
                    }
                }
            }
        }

        private void IconPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingSelectionBox)
            {
                _isDraggingSelectionBox = false; SelectionBox.Visibility = Visibility.Collapsed; IconPanel.ReleaseMouseCapture();
            }
        }
    }

    // ----------------------------------------------------------------------
    // 7. DATA MODEL FOR SAVING 
    // ----------------------------------------------------------------------
    public class FenceData
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsRolledUp { get; set; } = false; public double ExpandedHeight { get; set; } = 300;
        public string Title { get; set; } = "My Apps"; public string HexColor { get; set; } = "#000000";
        public double Opacity { get; set; } = 0.7; public string SortMethod { get; set; } = "None";
        public List<string> Files { get; set; } = new List<string>();
    }
}