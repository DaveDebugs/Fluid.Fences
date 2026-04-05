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

        private DockState _dockState = DockState.Floating;
        private bool _isTemporarilyRevealed = false;
        private string _saveSortMethod = "None";
        private bool _isContextMenuOpen = false;

        private List<StackPanel> _selectedItems = new List<StackPanel>();
        private System.Windows.Point _selectionStartPoint;
        private bool _isDraggingSelectionBox = false;
        private SolidColorBrush _highlightBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(85, 0, 120, 215));

        public string FenceTitle { get => TitleText.Text; set => TitleText.Text = value; }
        public string FenceSortMethod { get => _saveSortMethod; set => _saveSortMethod = value; }
        public void DashboardSaveAndRefresh() { ApplySorting(); SaveFenceState(); }
        public void DashboardDelete() { if (File.Exists(_saveFilePath)) File.Delete(_saveFilePath); this.Close(); }

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

        // ----------------------------------------------------------------------
        // 1. STATE SAVING, LOADING, & SORTING ALGORITHM
        // ----------------------------------------------------------------------
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
                        this.Left = data.Left; this.Top = data.Top;
                        _isRolledUp = data.IsRolledUp;
                        _expandedHeight = data.ExpandedHeight;
                        _expandedWidth = data.Width;
                        _expandedLeft = data.Left;
                        _expandedTop = data.Top;
                        TitleText.Text = data.Title;
                        _saveSortMethod = data.SortMethod;

                        SolidColorBrush savedBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#B2" + data.HexColor.Replace("#", ""))!;
                        savedBrush.Opacity = data.Opacity;
                        MainBorder.Background = savedBrush;

                        if (_isRolledUp)
                        {
                            if (data.Width <= 65) { this.Width = 55; this.Height = data.Height; }
                            else { this.Height = 55; this.Width = data.Width; }
                        }
                        else { this.Height = data.Height; this.Width = data.Width; }

                        _currentFiles.Clear();
                        foreach (string file in data.Files) { if (File.Exists(file) || Directory.Exists(file)) { _currentFiles.Add(file); } }

                        DetermineDockState();
                        UpdateDockOrientation();
                        ApplySorting();
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
                File.WriteAllText(_saveFilePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private void ApplySorting()
        {
            if (_saveSortMethod != "None" && _saveSortMethod != "DateAdd")
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

        private long GetFileSize(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }

        private void RefreshIconUI()
        {
            IconPanel.Children.Clear(); ClearSelection();
            if (_currentFiles.Count == 0)
            {
                IconPanel.Children.Add(new TextBlock { Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#88FFFFFF")!, Text = "Drop shortcuts here..." });
                return;
            }
            foreach (string file in _currentFiles) { AddIconToUI(file); }
        }

        // ----------------------------------------------------------------------
        // 2. DIRECTIONAL SIDE DOCKING, MARGINS, & ROLL-OUT ANIMATIONS
        // ----------------------------------------------------------------------
        private void DetermineDockState()
        {
            var workArea = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
            double dpiX = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            int physLeft = (int)(this.Left * dpiX);
            int physTop = (int)(this.Top * dpiY);
            int physRight = (int)((this.Left + this.Width) * dpiX);
            int physBottom = (int)((this.Top + this.Height) * dpiY);

            int thresh = 25; // Pixel distance to qualify as "docked"

            if (Math.Abs(physLeft - workArea.Left) < thresh) _dockState = DockState.Left;
            else if (Math.Abs(physRight - workArea.Right) < thresh) _dockState = DockState.Right;
            else if (Math.Abs(physTop - workArea.Top) < thresh) _dockState = DockState.Top;
            else if (Math.Abs(physBottom - workArea.Bottom) < thresh) _dockState = DockState.Bottom;
            else _dockState = DockState.Floating;
        }

        private void UpdateDockOrientation()
        {
            // NEW: Eliminate the Drop Shadow margin on whatever side is touching the screen to remove the gap!
            if (_dockState == DockState.Left) MainBorder.Margin = new Thickness(0, 10, 10, 10);
            else if (_dockState == DockState.Right) MainBorder.Margin = new Thickness(10, 10, 0, 10);
            else if (_dockState == DockState.Top) MainBorder.Margin = new Thickness(10, 0, 10, 10);
            else if (_dockState == DockState.Bottom) MainBorder.Margin = new Thickness(10, 10, 10, 0);
            else MainBorder.Margin = new Thickness(10); // Floating gets shadow on all sides

            // Move the Header Grid to the correct side and rotate
            if (_dockState == DockState.Left)
            {
                HeaderRowTop.Height = new GridLength(0); HeaderColLeft.Width = new GridLength(35); HeaderColRight.Width = new GridLength(0);
                Grid.SetRow(HeaderBorder, 0); Grid.SetRowSpan(HeaderBorder, 2); Grid.SetColumn(HeaderBorder, 0); Grid.SetColumnSpan(HeaderBorder, 1);
                HeaderGrid.LayoutTransform = new RotateTransform(-90);
            }
            else if (_dockState == DockState.Right)
            {
                HeaderRowTop.Height = new GridLength(0); HeaderColLeft.Width = new GridLength(0); HeaderColRight.Width = new GridLength(35);
                Grid.SetRow(HeaderBorder, 0); Grid.SetRowSpan(HeaderBorder, 2); Grid.SetColumn(HeaderBorder, 2); Grid.SetColumnSpan(HeaderBorder, 1);
                HeaderGrid.LayoutTransform = new RotateTransform(90);
            }
            else // Top, Bottom, or Floating (Header always sits on top)
            {
                HeaderRowTop.Height = new GridLength(35); HeaderColLeft.Width = new GridLength(0); HeaderColRight.Width = new GridLength(0);
                Grid.SetRow(HeaderBorder, 0); Grid.SetRowSpan(HeaderBorder, 1); Grid.SetColumn(HeaderBorder, 0); Grid.SetColumnSpan(HeaderBorder, 3);
                HeaderGrid.LayoutTransform = Transform.Identity;
            }
        }

        private void AnimateRollUp()
        {
            double d = 250; QuarticEase ease = new QuarticEase { EasingMode = EasingMode.EaseInOut };

            if (_dockState == DockState.Left)
            {
                this.BeginAnimation(Window.WidthProperty, new DoubleAnimation { To = 55, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
            }
            else if (_dockState == DockState.Right)
            {
                this.BeginAnimation(Window.WidthProperty, new DoubleAnimation { To = 55, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
                this.BeginAnimation(Window.LeftProperty, new DoubleAnimation { To = _expandedLeft + (_expandedWidth - 55), Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
            }
            else if (_dockState == DockState.Bottom)
            {
                this.BeginAnimation(Window.HeightProperty, new DoubleAnimation { To = 55, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
                this.BeginAnimation(Window.TopProperty, new DoubleAnimation { To = _expandedTop + (_expandedHeight - 55), Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
            }
            else
            {
                this.BeginAnimation(Window.HeightProperty, new DoubleAnimation { To = 55, Duration = TimeSpan.FromMilliseconds(d), EasingFunction = ease });
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

                // Clear any active roll-up animations to free the window for dragging
                this.BeginAnimation(Window.LeftProperty, null);
                this.BeginAnimation(Window.TopProperty, null);
                this.BeginAnimation(Window.WidthProperty, null);
                this.BeginAnimation(Window.HeightProperty, null);

                if (_isRolledUp || _isTemporarilyRevealed)
                {
                    _isRolledUp = false; _isTemporarilyRevealed = false;
                    // Restore full size while dragging, but let the user keep their mouse exactly where they clicked!
                    this.Width = _expandedWidth; this.Height = _expandedHeight;
                    if (_dockState == DockState.Right) this.Left = this.Left - (_expandedWidth - 55);
                    if (_dockState == DockState.Bottom) this.Top = this.Top - (_expandedHeight - 55);
                }

                // Treat as floating during the drag so the visual shadow restores fully
                _dockState = DockState.Floating; UpdateDockOrientation();

                this.DragMove();

                // The exact millisecond the user drops the fence, re-evaluate its edge!
                DetermineDockState(); UpdateDockOrientation();

                _expandedLeft = this.Left; _expandedTop = this.Top; _expandedWidth = this.Width; _expandedHeight = this.Height;
                SaveFenceState();
            }
        }

        private void ToggleRollUp()
        {
            if (_isRolledUp)
            {
                AnimateReveal();
                _isRolledUp = false; _isTemporarilyRevealed = false;
            }
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

        private void TitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) return;
            e.Handled = true; TitleText.Visibility = Visibility.Collapsed;
            TextBox renameBox = new TextBox { Text = TitleText.Text, Width = 140, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, FontFamily = TitleText.FontFamily, Padding = new Thickness(0, 1, 0, 0) };
            HeaderGrid.Children.Add(renameBox); renameBox.Focus(); renameBox.SelectAll();
            renameBox.KeyDown += (s2, e2) => { if (e2.Key == Key.Enter) { TitleText.Text = renameBox.Text; HeaderGrid.Children.Remove(renameBox); TitleText.Visibility = Visibility.Visible; SaveFenceState(); } else if (e2.Key == Key.Escape) { HeaderGrid.Children.Remove(renameBox); TitleText.Visibility = Visibility.Visible; } };
            renameBox.LostFocus += (s2, e2) => { if (HeaderGrid.Children.Contains(renameBox)) { TitleText.Text = renameBox.Text; HeaderGrid.Children.Remove(renameBox); TitleText.Visibility = Visibility.Visible; SaveFenceState(); } };
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e) { if (_isRolledUp && !_isTemporarilyRevealed) { AnimateReveal(); _isTemporarilyRevealed = true; } }
        private void Window_MouseLeave(object sender, MouseEventArgs e) { if (_isContextMenuOpen || _isDraggingSelectionBox) return; if (_isRolledUp && _isTemporarilyRevealed) { AnimateRollUp(); _isTemporarilyRevealed = false; } }
        private void HandleMenuClosed() { _isContextMenuOpen = false; if (!this.IsMouseOver && _isRolledUp && _isTemporarilyRevealed && !_isDraggingSelectionBox) { AnimateRollUp(); _isTemporarilyRevealed = false; } }
        private void MenuIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { e.Handled = true; HeaderContextMenu.IsOpen = true; }

        private void Menu_NewFence_Click(object sender, RoutedEventArgs e) { MainWindow newFence = new MainWindow(Guid.NewGuid().ToString()); newFence.Left = this.Left + 50; newFence.Top = this.Top + 50; newFence.Show(); }
        private void Menu_DeleteFence_Click(object sender, RoutedEventArgs e) { if (MessageBox.Show("Permanently delete this fence?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) { if (File.Exists(_saveFilePath)) File.Delete(_saveFilePath); this.Close(); } }
        private void Menu_RollUp_Click(object sender, RoutedEventArgs e) { ToggleRollUp(); }
        private void Menu_Opacity_Click(object sender, RoutedEventArgs e) { if (sender is not MenuItem clickedItem || clickedItem.Tag == null) return; if (double.TryParse(clickedItem.Tag.ToString(), out double newOpacity)) { SolidColorBrush newBrush = (SolidColorBrush)MainBorder.Background.Clone(); newBrush.Opacity = newOpacity; MainBorder.Background = newBrush; SaveFenceState(); } }
        private void Menu_Sort_Click(object sender, RoutedEventArgs e) { if (sender is not MenuItem clickedSort || clickedSort.Tag == null) return; _saveSortMethod = clickedSort.Tag.ToString() ?? "None"; ApplySorting(); SaveFenceState(); }
        private void Menu_CopyColor_Click(object sender, RoutedEventArgs e) { Clipboard.SetText("#" + MainBorder.Background.ToString().Substring(3)); }
        private void Menu_EditColor_Click(object sender, RoutedEventArgs e) { SolidColorBrush currentBrush = (SolidColorBrush)MainBorder.Background; ColorPickerWindow picker = new ColorPickerWindow(currentBrush); picker.Owner = this; if (picker.ShowDialog() == true && picker.SelectedBrush != null) { MainBorder.Background = picker.SelectedBrush; SaveFenceState(); } }
        private void Menu_Settings_Click(object sender, RoutedEventArgs e) { SettingsWindow settings = new SettingsWindow(this); settings.Owner = this; settings.ShowDialog(); }

        // ----------------------------------------------------------------------
        // 3. DEBUG KEYBOARD SNAPPING 
        // ----------------------------------------------------------------------
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
                var workArea = screen.WorkingArea;

                PresentationSource? source = PresentationSource.FromVisual(this);
                double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                double logicalLeft = workArea.Left / dpiX; double logicalTop = workArea.Top / dpiY;
                double logicalRight = workArea.Right / dpiX; double logicalBottom = workArea.Bottom / dpiY;

                bool wasHandled = false;

                this.BeginAnimation(Window.LeftProperty, null); this.BeginAnimation(Window.TopProperty, null);
                this.BeginAnimation(Window.WidthProperty, null); this.BeginAnimation(Window.HeightProperty, null);

                if (_isRolledUp) { _isRolledUp = false; _isTemporarilyRevealed = false; this.Width = _expandedWidth; this.Height = _expandedHeight; }

                // Snap perfectly to the logical edge bounds. The Margin removal will handle the visual gap.
                if (e.Key == Key.Up) { this.Top = logicalTop; wasHandled = true; }
                else if (e.Key == Key.Down) { this.Top = logicalBottom - this.Height; wasHandled = true; }
                else if (e.Key == Key.Left) { this.Left = logicalLeft; wasHandled = true; }
                else if (e.Key == Key.Right) { this.Left = logicalRight - this.Width; wasHandled = true; }

                if (wasHandled)
                {
                    e.Handled = true;
                    DetermineDockState(); UpdateDockOrientation();
                    _expandedLeft = this.Left; _expandedTop = this.Top; _expandedWidth = this.Width; _expandedHeight = this.Height;
                    SaveFenceState();
                }
            }
        }

        // ----------------------------------------------------------------------
        // 4. DRAG AND DROP & UI GENERATION
        // ----------------------------------------------------------------------
        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null) { foreach (string file in files) { if (!_currentFiles.Contains(file)) { _currentFiles.Add(file); } } ApplySorting(); SaveFenceState(); }
                string? sourceId = e.Data.GetData("FenceSourceId") as string;
                if (!string.IsNullOrEmpty(sourceId) && sourceId != _fenceId) e.Effects = DragDropEffects.Move; else e.Effects = DragDropEffects.None;
            }
        }

        private void AddIconToUI(string file)
        {
            string currentFilePath = file;
            StackPanel itemContainer = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(10), Width = 64, ToolTip = currentFilePath, Background = System.Windows.Media.Brushes.Transparent };
            System.Windows.Controls.Image iconImage = new System.Windows.Controls.Image { Source = GetSystemIcon(currentFilePath), Width = 32, Height = 32, Margin = new Thickness(0, 0, 0, 5) };
            TextBlock textBlock = new TextBlock { Text = Path.GetFileNameWithoutExtension(currentFilePath), Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, FontSize = 11, TextWrapping = TextWrapping.Wrap };

            ContextMenu rightClickMenu = new ContextMenu(); MenuItem renameItem = new MenuItem { Header = "Rename File" }; MenuItem removeItem = new MenuItem { Header = "Remove from Fence" };
            removeItem.Click += (s, args) => { _currentFiles.Remove(currentFilePath); ApplySorting(); SaveFenceState(); };
            renameItem.Click += (s, args) =>
            {
                textBlock.Visibility = Visibility.Collapsed; TextBox renameBox = new TextBox { Text = textBlock.Text, Width = 64, Margin = new Thickness(-5, 0, -5, 0), TextAlignment = TextAlignment.Center };
                itemContainer.Children.Add(renameBox); renameBox.Focus(); renameBox.SelectAll();
                renameBox.KeyDown += (s2, e2) => { if (e2.Key == Key.Enter) { try { string? dir = Path.GetDirectoryName(currentFilePath); string ext = Path.GetExtension(currentFilePath); if (dir != null) { string newPath = Path.Combine(dir, renameBox.Text + ext); if (currentFilePath != newPath && !File.Exists(newPath)) { File.Move(currentFilePath, newPath); _currentFiles.Remove(currentFilePath); _currentFiles.Add(newPath); ApplySorting(); SaveFenceState(); } else { itemContainer.Children.Remove(renameBox); textBlock.Visibility = Visibility.Visible; } } } catch { MessageBox.Show("Could not rename file.", "Error"); itemContainer.Children.Remove(renameBox); textBlock.Visibility = Visibility.Visible; } } else if (e2.Key == Key.Escape) { itemContainer.Children.Remove(renameBox); textBlock.Visibility = Visibility.Visible; } };
                renameBox.LostFocus += (s2, e2) => { if (itemContainer.Children.Contains(renameBox)) { itemContainer.Children.Remove(renameBox); textBlock.Visibility = Visibility.Visible; } };
            };

            rightClickMenu.Items.Add(renameItem); rightClickMenu.Items.Add(removeItem); itemContainer.ContextMenu = rightClickMenu;
            rightClickMenu.Opened += (s, args) => _isContextMenuOpen = true; rightClickMenu.Closed += (s, args) => HandleMenuClosed();

            System.Windows.Point? dragStartPoint = null;
            itemContainer.PreviewMouseLeftButtonDown += (s, args) => { dragStartPoint = args.GetPosition(null); bool isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl); if (isCtrlPressed) { if (_selectedItems.Contains(itemContainer)) { itemContainer.Background = System.Windows.Media.Brushes.Transparent; _selectedItems.Remove(itemContainer); } else { itemContainer.Background = _highlightBrush; _selectedItems.Add(itemContainer); } } else if (!_selectedItems.Contains(itemContainer)) { ClearSelection(); itemContainer.Background = _highlightBrush; _selectedItems.Add(itemContainer); } };
            itemContainer.PreviewMouseMove += (s, args) => { if (args.LeftButton == MouseButtonState.Pressed && dragStartPoint.HasValue) { System.Windows.Point currentPoint = args.GetPosition(null); if (Math.Abs(currentPoint.X - dragStartPoint.Value.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(currentPoint.Y - dragStartPoint.Value.Y) > SystemParameters.MinimumVerticalDragDistance) { DataObject dragData = new DataObject(DataFormats.FileDrop, new string[] { currentFilePath }); dragData.SetData("FenceSourceId", _fenceId); DragDropEffects result = DragDrop.DoDragDrop(itemContainer, dragData, DragDropEffects.Move | DragDropEffects.Copy); if (result == DragDropEffects.Move) { _currentFiles.Remove(currentFilePath); ApplySorting(); SaveFenceState(); } dragStartPoint = null; } } };
            itemContainer.MouseDown += (s, args) => { if (args.ClickCount == 2 && args.ChangedButton == MouseButton.Left) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = currentFilePath, UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show($"Could not open file.\nError: {ex.Message}", "Launch Error"); } } };

            itemContainer.Children.Add(iconImage); itemContainer.Children.Add(textBlock); IconPanel.Children.Add(itemContainer);
        }

        private ImageSource? GetSystemIcon(string filePath) { try { using (System.Drawing.Icon? sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath)) { if (sysIcon == null) return null; return Imaging.CreateBitmapSourceFromHIcon(sysIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); } } catch { return null; } }

        // ----------------------------------------------------------------------
        // 5. WINDOW LOADED & DPI-PERFECT MAGNETIC SNAPPING
        // ----------------------------------------------------------------------
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadFenceState();
            IntPtr myWindowHandle = new WindowInteropHelper(this).Handle; SetWindowPos(myWindowHandle, (IntPtr)HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            HwndSource? source = HwndSource.FromHwnd(myWindowHandle);
            if (source != null) source.AddHook(new HwndSourceHook(WndProc));

            this.PreviewKeyDown += Window_PreviewKeyDown;
            HeaderContextMenu.Opened += (s, args) => _isContextMenuOpen = true;
            HeaderContextMenu.Closed += (s, args) => HandleMenuClosed();
            this.PreviewMouseRightButtonDown += (s, args) => _isContextMenuOpen = true;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WINDOWPOSCHANGING)
            {
                WINDOWPOS windowPos = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS))!; windowPos.hwndInsertAfter = (IntPtr)HWND_BOTTOM;

                if ((windowPos.flags & SWP_NOMOVE) == 0 && (windowPos.flags & SWP_NOSIZE) != 0)
                {
                    int snapMargin = 20; // Increased snapping strength
                    var screen = System.Windows.Forms.Screen.FromHandle(hwnd); var workArea = screen.WorkingArea;

                    // We map the physical edge of the WPF window DIRECTLY to the edge of the monitor. 
                    // Our UpdateDockOrientation() will dynamically remove the 10px shadow margin from the touching edge so it's perfectly flush.
                    if (Math.Abs(windowPos.x - workArea.Left) < snapMargin) windowPos.x = workArea.Left;
                    if (Math.Abs(windowPos.y - workArea.Top) < snapMargin) windowPos.y = workArea.Top;
                    if (Math.Abs((windowPos.x + windowPos.cx) - workArea.Right) < snapMargin) windowPos.x = workArea.Right - windowPos.cx;
                    if (Math.Abs((windowPos.y + windowPos.cy) - workArea.Bottom) < snapMargin) windowPos.y = workArea.Bottom - windowPos.cy;
                }
                Marshal.StructureToPtr(windowPos, lParam, true);
            }
            return IntPtr.Zero;
        }

        // ----------------------------------------------------------------------
        // 6. RESIZING & SELECTION LOGIC
        // ----------------------------------------------------------------------
        private void Resize_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (_isRolledUp) return;
            this.Height = this.ActualHeight; this.Width = this.ActualWidth;
            this.BeginAnimation(Window.HeightProperty, null); this.BeginAnimation(Window.WidthProperty, null);
            this.BeginAnimation(Window.LeftProperty, null); this.BeginAnimation(Window.TopProperty, null);
        }

        private void ResizeRight_DragDelta(object sender, DragDeltaEventArgs e) { double newWidth = this.Width + e.HorizontalChange; if (newWidth > 150) this.Width = newWidth; }
        private void ResizeLeft_DragDelta(object sender, DragDeltaEventArgs e) { double newWidth = this.Width - e.HorizontalChange; if (newWidth > 150) { this.Left += e.HorizontalChange; this.Width = newWidth; } }
        private void ResizeBottom_DragDelta(object sender, DragDeltaEventArgs e) { if (_isRolledUp) return; double newHeight = this.Height + e.VerticalChange; if (newHeight > 100) { this.Height = newHeight; _expandedHeight = newHeight; } }
        private void ResizeTop_DragDelta(object sender, DragDeltaEventArgs e) { if (_isRolledUp) return; double newHeight = this.Height - e.VerticalChange; if (newHeight > 100) { this.Top += e.VerticalChange; this.Height = newHeight; _expandedHeight = newHeight; } }
        private void ResizeTopLeft_DragDelta(object sender, DragDeltaEventArgs e) { ResizeTop_DragDelta(sender, e); ResizeLeft_DragDelta(sender, e); }
        private void ResizeTopRight_DragDelta(object sender, DragDeltaEventArgs e) { ResizeTop_DragDelta(sender, e); ResizeRight_DragDelta(sender, e); }
        private void ResizeBottomLeft_DragDelta(object sender, DragDeltaEventArgs e) { ResizeBottom_DragDelta(sender, e); ResizeLeft_DragDelta(sender, e); }
        private void ResizeBottomRight_DragDelta(object sender, DragDeltaEventArgs e) { ResizeBottom_DragDelta(sender, e); ResizeRight_DragDelta(sender, e); }
        private void Resize_DragCompleted(object sender, DragCompletedEventArgs e) { DetermineDockState(); UpdateDockOrientation(); _expandedLeft = this.Left; _expandedTop = this.Top; _expandedWidth = this.Width; _expandedHeight = this.Height; SaveFenceState(); }

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
        public bool IsRolledUp { get; set; } = false; public double ExpandedHeight { get; set; } = 300;
        public string Title { get; set; } = "My Apps"; public string HexColor { get; set; } = "#000000";
        public double Opacity { get; set; } = 0.7; public string SortMethod { get; set; } = "None";
        public List<string> Files { get; set; } = new List<string>();
    }
}