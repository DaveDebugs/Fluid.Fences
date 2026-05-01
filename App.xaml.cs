using System;
using System.Windows;
using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Application;
using Microsoft.Win32;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DesktopFences
{
    public partial class App : Application
    {
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 9000;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const uint VK_Z = 0x5A;

        private NotifyIcon? _trayIcon;
        private bool _isZenModeActive = false;
        private string _configFolder = "";

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _configFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences");
            Directory.CreateDirectory(_configFolder);

            CreateTrayIcon();

            ComponentDispatcher.ThreadPreprocessMessage += ComponentDispatcher_ThreadPreprocessMessage;
            RegisterHotKey(IntPtr.Zero, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_Z);

            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;

            await System.Threading.Tasks.Task.Delay(1500);

            RestoreSavedFences();

            System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(RestoreLayoutSnapshot);
            });
        }

        private void ComponentDispatcher_ThreadPreprocessMessage(ref MSG msg, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg.message == WM_HOTKEY && msg.wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleZenMode();
                handled = true;
            }
        }

        private void ToggleZenMode()
        {
            _isZenModeActive = !_isZenModeActive;
            foreach (Window window in Current.Windows)
            {
                if (window is MainWindow fence)
                {
                    fence.Visibility = _isZenModeActive ? Visibility.Hidden : Visibility.Visible;
                }
            }
        }

        public void SaveCurrentLayout()
        {
            try
            {
                string snapshotPath = Path.Combine(_configFolder, "snapshot.json");
                List<SnapshotData> snapshots = [];

                foreach (Window window in Current.Windows)
                {
                    if (window is MainWindow { Visibility: Visibility.Visible } fence)
                    {
                        snapshots.Add(new SnapshotData
                        {
                            FenceId = fence.GetFenceId(),
                            Left = fence.Left,
                            Top = fence.Top,
                            Width = fence.Width,
                            Height = fence.Height,
                            IsRolledUp = fence.GetIsRolledUp()
                        });
                    }
                }
                File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshots, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public void RestoreLayoutSnapshot()
        {
            try
            {
                string snapshotPath = Path.Combine(_configFolder, "snapshot.json");
                if (!File.Exists(snapshotPath)) return;

                string json = File.ReadAllText(snapshotPath);
                var snapshots = JsonSerializer.Deserialize<List<SnapshotData>>(json);

                if (snapshots is not null)
                {
                    foreach (var snap in snapshots)
                    {
                        foreach (Window window in Current.Windows)
                        {
                            if (window is MainWindow fence && fence.GetFenceId() == snap.FenceId)
                            {
                                fence.RestoreSnapshot(snap.Left, snap.Top, snap.Width, snap.Height, snap.IsRolledUp);
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private async void RestoreSavedFences()
        {
            string saveDirectory = Path.Combine(_configFolder, "Fences");

            if (Directory.Exists(saveDirectory))
            {
                string[] savedFences = Directory.GetFiles(saveDirectory, "*.json");
                if (savedFences.Length > 0)
                {
                    foreach (string file in savedFences)
                    {
                        string fenceId = Path.GetFileNameWithoutExtension(file);
                        if (fenceId.Equals("designer", StringComparison.OrdinalIgnoreCase)) continue;

                        MainWindow restoredFence = new(fenceId);
                        restoredFence.Show();

                        await System.Threading.Tasks.Task.Delay(75);
                    }
                    return;
                }
            }

            MainWindow defaultFence = new(Guid.NewGuid().ToString());
            defaultFence.Show();
        }

        private void CreateTrayIcon()
        {
            var iconUri = new Uri("pack://application:,,,/Assets/FF 256.ico");
            using var iconStream = Application.GetResourceStream(iconUri)?.Stream ?? throw new FileNotFoundException("Could not load the embedded icon resource.");

            _trayIcon = new NotifyIcon { Icon = new Icon(iconStream), Visible = true, Text = "Fluid Fences" };

            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Toggle Zen Mode (Ctrl+Alt+Z)", null, (s, e) => ToggleZenMode());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Create New Fence", null, (s, e) => CreateNewFence());
            trayMenu.Items.Add("Create Folder Portal...", null, (s, e) => CreateNewPortal());
            trayMenu.Items.Add("Open Settings", null, (s, e) => OpenSettings());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit Fluid Fences", null, (s, e) => ExitApp());

            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.DoubleClick += (s, e) => OpenSettings();
        }

        private void CreateNewFence() { MainWindow newFence = new(Guid.NewGuid().ToString()); newFence.Show(); }

        private void CreateNewPortal()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select a folder to mirror in the Portal Fence" };
            if (dialog.ShowDialog() == true)
            {
                MainWindow portalFence = new(Guid.NewGuid().ToString())
                {
                    Left = SystemParameters.PrimaryScreenWidth / 2 - 125,
                    Top = SystemParameters.PrimaryScreenHeight / 2 - 150
                };
                portalFence.MakePortal(dialog.FolderName);
                portalFence.Show();
            }
        }

        private void OpenSettings() { SettingsWindow settings = new(null); settings.Show(); }

        private void ExitApp()
        {
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            Current.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
            _trayIcon?.Dispose();
            base.OnExit(e);
        }
    }
}