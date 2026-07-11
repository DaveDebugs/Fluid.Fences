#pragma warning disable CA1001

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using Application = System.Windows.Application;

namespace DesktopFences
{
    public partial class App : Application
    {
        private const int HOTKEY_ID = 9000;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_Z = 0x5A;

        private NotifyIcon? _trayIcon;
        private bool _isZenModeActive;

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public static string ConfigFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopFences");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);
                if (key?.GetValue("FluidFences") != null)
                {
                    using RegistryKey? serializeKey = Registry.CurrentUser.CreateSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Serialize");
                    serializeKey?.SetValue("StartupDelayInMS", 0, RegistryValueKind.DWord);
                }
            }
            catch { }

            string oldFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences");
            if (Directory.Exists(oldFolder))
            {
                try
                {
                    void MoveDirectory(string source, string target)
                    {
                        if (!Directory.Exists(target)) Directory.CreateDirectory(target);
                        foreach (string file in Directory.GetFiles(source))
                        {
                            string destFile = Path.Combine(target, Path.GetFileName(file));
                            try { if (!File.Exists(destFile)) File.Move(file, destFile); } catch (Exception ex) { LogError("MoveDirectory File", ex); }
                        }
                        foreach (string dir in Directory.GetDirectories(source))
                        {
                            try { MoveDirectory(dir, Path.Combine(target, Path.GetFileName(dir))); } catch (Exception ex) { LogError("MoveDirectory Dir", ex); }
                        }
                    }
                    MoveDirectory(oldFolder, ConfigFolder);
                    try { Directory.Delete(oldFolder, true); } catch { }
                }
                catch { }
            }

            Directory.CreateDirectory(ConfigFolder);
            Directory.CreateDirectory(Path.Combine(ConfigFolder, "Fences"));

            CreateTrayIcon();

            ComponentDispatcher.ThreadPreprocessMessage += ComponentDispatcher_ThreadPreprocessMessage;

            bool hotkeyRegistered = NativeMethods.RegisterHotKey(IntPtr.Zero, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_Z);
            if (!hotkeyRegistered)
            {
                bool fallbackRegistered = NativeMethods.RegisterHotKey(IntPtr.Zero, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_Z);
                if (fallbackRegistered) _trayIcon?.ShowBalloonTip(3000, "Hotkey Changed", "Ctrl+Alt+Z is in use by another app. Zen Mode is using Ctrl+Shift+Z instead.", ToolTipIcon.Info);
            }

            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;

            RestoreSavedFences();

            Updater.CleanupOldUpdates();
            _ = Updater.CheckAndApplyUpdateAsync(silentCheck: true);
        }

        private static readonly object _logLock = new();
        private static void LogError(string context, Exception ex)
        {
            try
            {
                lock (_logLock)
                {
                    string logPath = Path.Combine(ConfigFolder, "app_error.log");
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex.Message}\n{ex.StackTrace}\n");
                }
            }
            catch { }
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            _ = System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    foreach (Window window in Current.Windows)
                    {
                        if (window is MainWindow { IsLoaded: true } fence) fence.ClampToScreen();
                    }
                });
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

        public async void SaveCurrentLayout()
        {
            try
            {
                string snapshotPath = Path.Combine(ConfigFolder, "snapshot.json");
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
                            IsRolledUp = fence.GetIsRolledUp(),
                            ExpandedLeft = fence.ExpandedLeft,
                            ExpandedTop = fence.ExpandedTop,
                            ExpandedWidth = fence.ExpandedWidth,
                            ExpandedHeight = fence.ExpandedHeight
                        });
                    }
                }

                string tempPath = snapshotPath + ".tmp";
                await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(snapshots, _jsonOptions));
                File.Move(tempPath, snapshotPath, overwrite: true);
            }
            catch (Exception ex) { LogError("SaveCurrentLayout", ex); }
        }

        public async void RestoreLayoutSnapshot()
        {
            try
            {
                string snapshotPath = Path.Combine(ConfigFolder, "snapshot.json");
                if (!File.Exists(snapshotPath)) return;

                string json = await File.ReadAllTextAsync(snapshotPath);
                var snapshots = JsonSerializer.Deserialize<List<SnapshotData>>(json);

                if (snapshots is not null)
                {
                    foreach (var snap in snapshots)
                    {
                        foreach (Window window in Current.Windows)
                        {
                            if (window is MainWindow fence && fence.GetFenceId() == snap.FenceId)
                            {
                                fence.RestoreSnapshot(snap);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { LogError("RestoreLayoutSnapshot", ex); }
        }

        private async void RestoreSavedFences()
        {
            try
            {
                string saveDirectory = Path.Combine(ConfigFolder, "Fences");

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

                            await System.Threading.Tasks.Task.Delay(50);
                        }
                        return;
                    }
                }
            }
            catch (Exception ex) { LogError("RestoreSavedFences", ex); }

            MainWindow defaultFence = new(Guid.NewGuid().ToString())
            {
                Left = SystemParameters.PrimaryScreenWidth - 350,
                Top = 100,
                Width = 250,
                Height = 300
            };
            defaultFence.FenceTitle = "My First Fence";
            defaultFence.Show();
        }

        private void CreateTrayIcon()
        {
            var iconUri = new Uri("pack://application:,,,/Assets/FF 256.ico");
            using var iconStream = Application.GetResourceStream(iconUri)?.Stream ?? throw new FileNotFoundException("Could not load the embedded icon resource.");

            _trayIcon = new NotifyIcon { Icon = new Icon(iconStream), Visible = true, Text = "Fluid Fences" };

            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Toggle Zen Mode", null, (s, e) => ToggleZenMode());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Create New Fence", null, (s, e) => CreateNewFence());
            trayMenu.Items.Add("Create Folder Portal...", null, (s, e) => CreateNewPortal());
            trayMenu.Items.Add("Check for Updates", null, (s, e) => _ = Updater.CheckAndApplyUpdateAsync(silentCheck: false));
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
            NativeMethods.UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
            _trayIcon?.Dispose();
            base.OnExit(e);
        }
    }
}