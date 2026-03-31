using System;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Drawing;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DesktopFences
{
    public partial class MainWindow : Window
    {
        // --- WIN32 API DECLARATIONS FOR BOTTOM-MOST Z-ORDER ---
        private const int HWND_BOTTOM = 1;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private bool _isRolledUp = false;
        private double _expandedHeight = 300; // Default starting height

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd; public IntPtr hwndInsertAfter; public int x; public int y; public int cx; public int cy; public uint flags;
        }

        // --- STATE MANAGEMENT VARIABLES ---
        private List<string> _currentFiles = new List<string>();
        private readonly string _saveDirectory;
        private readonly string _saveFilePath;

        public MainWindow()
        {
            InitializeComponent();

            // Set up the save path in the user's AppData folder
            _saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences");
            _saveFilePath = Path.Combine(_saveDirectory, "fenceData.json");
        }

        // ----------------------------------------------------------------------
        // 1. STATE SAVING & LOADING (AUTO-SAVE)
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
                        // Restore Window Position and Size
                        this.Left = data.Left;
                        this.Top = data.Top;
                        this.Width = data.Width;
                        this.Height = data.Height;

                        // Restore Icons
                        IconPanel.Children.Clear();
                        foreach (string file in data.Files)
                        {
                            // Only add it if the file hasn't been deleted from the computer
                            if (File.Exists(file) || Directory.Exists(file))
                            {
                                AddIconToUI(file);
                                _currentFiles.Add(file);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load fence data: {ex.Message}");
                }
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
                    IsRolledUp = _isRolledUp,           // <-- Add this
                    ExpandedHeight = _expandedHeight,   // <-- Add this
                    Files = _currentFiles
                };

                Directory.CreateDirectory(_saveDirectory);
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_saveFilePath, json);
            }
            catch (Exception ex)
            {
                // Silently fail or log if the file is temporarily locked
                System.Diagnostics.Debug.WriteLine($"Save failed: {ex.Message}");
            }
        }

        // ----------------------------------------------------------------------
        // 2. WINDOW DRAGGING
        // ----------------------------------------------------------------------
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                // NEW: Intercept double-click for the Roll-Up feature
                if (e.ClickCount == 2)
                {
                    if (_isRolledUp)
                    {
                        // Unroll it
                        this.Height = _expandedHeight;
                        _isRolledUp = false;
                    }
                    else
                    {
                        // Roll it up (35 is the height of your Header row in XAML)
                        _expandedHeight = this.Height;
                        this.Height = 35;
                        _isRolledUp = true;
                    }
                    SaveFenceState();
                    return; // Stop here so we don't accidentally trigger a drag
                }

                // If it's just a normal single click & hold, drag the window
                this.DragMove();
                SaveFenceState();
            }
        }

        // ----------------------------------------------------------------------
        // 3. DRAG AND DROP & UI GENERATION
        // ----------------------------------------------------------------------
        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (IconPanel.Children.Count == 1 && IconPanel.Children[0] is TextBlock placeholder && placeholder.Text.Contains("Drop shortcuts"))
                {
                    IconPanel.Children.Clear();
                }

                foreach (string file in files)
                {
                    // Prevent duplicates
                    if (!_currentFiles.Contains(file))
                    {
                        AddIconToUI(file);
                        _currentFiles.Add(file);
                    }
                }

                // Save the newly added files to our JSON immediately
                SaveFenceState();
            }
        }

        private void AddIconToUI(string file)
        {
            StackPanel itemContainer = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(10),
                Width = 64,
                ToolTip = file,
                Background = System.Windows.Media.Brushes.Transparent
            };

            // NEW: Create a Context Menu for removing the icon
            ContextMenu rightClickMenu = new ContextMenu();
            MenuItem removeItem = new MenuItem { Header = "Remove from Fence" };

            removeItem.Click += (s, args) =>
            {
                // 1. Remove from the visual UI
                IconPanel.Children.Remove(itemContainer);
                // 2. Remove from our internal list
                _currentFiles.Remove(file);
                // 3. Save the new state immediately
                SaveFenceState();
            };

            rightClickMenu.Items.Add(removeItem);
            itemContainer.ContextMenu = rightClickMenu; // Attach it to the icon container

            itemContainer.MouseDown += (s, args) =>
            {
                if (args.ClickCount == 2 && args.ChangedButton == MouseButton.Left)
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = file, UseShellExecute = true }); }
                    catch (Exception ex) { MessageBox.Show($"Could not open file.\nError: {ex.Message}", "Launch Error"); }
                }
            };

            System.Windows.Controls.Image iconImage = new System.Windows.Controls.Image
            {
                Source = GetSystemIcon(file),
                Width = 32,
                Height = 32,
                Margin = new Thickness(0, 0, 0, 5)
            };

            TextBlock textBlock = new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(file),
                Foreground = System.Windows.Media.Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };

            itemContainer.Children.Add(iconImage);
            itemContainer.Children.Add(textBlock);
            IconPanel.Children.Add(itemContainer);
        }

        // ----------------------------------------------------------------------
        // 4. ICON EXTRACTION HELPER
        // ----------------------------------------------------------------------
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
        // 5. WINDOW LOADED EVENT (Z-ORDER + LOAD DATA)
        // ----------------------------------------------------------------------
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Load saved data before drawing the window
            LoadFenceState();

            IntPtr myWindowHandle = new WindowInteropHelper(this).Handle;

            // Push to the absolute bottom of the screen
            SetWindowPos(myWindowHandle, (IntPtr)HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

            // Hook the message pump to keep it there
            HwndSource source = HwndSource.FromHwnd(myWindowHandle);
            source.AddHook(new HwndSourceHook(WndProc));
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WINDOWPOSCHANGING)
            {
                WINDOWPOS windowPos = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS));
                windowPos.hwndInsertAfter = (IntPtr)HWND_BOTTOM;
                Marshal.StructureToPtr(windowPos, lParam, true);
            }
            return IntPtr.Zero;
        }
    }

    // --- DATA MODEL FOR SAVING ---
    public class FenceData
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        // NEW: Save the roll-up state
        public bool IsRolledUp { get; set; } = false;
        public double ExpandedHeight { get; set; } = 300;

        public List<string> Files { get; set; } = new List<string>();
    }
}