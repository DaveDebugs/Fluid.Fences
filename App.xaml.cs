using System;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace DesktopFences
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? _trayIcon;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            _trayIcon = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Visible = true,
                Text = "Fluid Fences"
            };

            _trayIcon.ContextMenuStrip = new ContextMenuStrip();
            _trayIcon.ContextMenuStrip.Items.Add("Clean Up Desktop (Auto-Sort)", null, OnCleanDesktopClicked);
            _trayIcon.ContextMenuStrip.Items.Add("-");
            _trayIcon.ContextMenuStrip.Items.Add("Create New Fence", null, OnNewFenceClicked);
            _trayIcon.ContextMenuStrip.Items.Add("Settings", null, OnSettingsClicked);
            _trayIcon.ContextMenuStrip.Items.Add("-");
            _trayIcon.ContextMenuStrip.Items.Add("Exit Fluid Fences", null, OnExitClicked);

            string saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences", "Fences");
            bool loadedFences = false;

            if (Directory.Exists(saveDirectory))
            {
                string[] fenceFiles = Directory.GetFiles(saveDirectory, "*.json");
                if (fenceFiles.Length > 0)
                {
                    foreach (string file in fenceFiles)
                    {
                        string fenceId = Path.GetFileNameWithoutExtension(file);
                        MainWindow fence = new MainWindow(fenceId);
                        fence.Show();
                    }
                    loadedFences = true;
                }
            }

            if (!loadedFences)
            {
                MainWindow defaultFence = new MainWindow("designer");
                defaultFence.Show();
            }
        }

        // ======================================================================
        // THE AUTO-SORTING ENGINE
        // ======================================================================
        private void OnCleanDesktopClicked(object? sender, EventArgs e)
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string[] desktopFiles = Directory.GetFiles(desktopPath);
            int movedCount = 0;

            foreach (string filePath in desktopFiles)
            {
                // Never touch system configuration files
                if (Path.GetFileName(filePath).ToLower() == "desktop.ini") continue;

                string extension = Path.GetExtension(filePath).ToLower();

                // Check every open fence to see if it wants this file type
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is MainWindow fence && !string.IsNullOrWhiteSpace(fence.AutoSortExtensions))
                    {
                        string[] rules = fence.AutoSortExtensions.ToLower().Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        bool isMatch = false;

                        foreach (string rule in rules)
                        {
                            if (extension == rule || extension == "." + rule) { isMatch = true; break; }
                        }

                        if (isMatch)
                        {
                            try
                            {
                                // To truly clear the desktop, we move the files into a dedicated storage folder
                                string storageDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Fluid Fences Storage", fence.FenceTitle);
                                Directory.CreateDirectory(storageDir);

                                string newFilePath = Path.Combine(storageDir, Path.GetFileName(filePath));

                                if (!File.Exists(newFilePath))
                                {
                                    File.Move(filePath, newFilePath);
                                    fence.AddFileFromAutoSort(newFilePath);
                                    movedCount++;
                                }
                            }
                            catch { /* Ignore files that are currently open/locked by Windows */ }
                            break; // Once a file is sorted into one fence, stop checking the other fences
                        }
                    }
                }
            }

            if (movedCount > 0)
            {
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is MainWindow fence) fence.DashboardSaveAndRefresh();
                }
                System.Windows.MessageBox.Show($"Successfully swept up and sorted {movedCount} files from your desktop!", "Desktop Cleaned", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnNewFenceClicked(object? sender, EventArgs e) { MainWindow fence = new MainWindow(Guid.NewGuid().ToString()); fence.Show(); }

        private void OnSettingsClicked(object? sender, EventArgs e)
        {
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window is MainWindow main) { SettingsWindow settings = new SettingsWindow(main); settings.Show(); return; }
            }
        }

        private void OnExitClicked(object? sender, EventArgs e)
        {
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window is MainWindow main) main.DashboardSaveAndRefresh();
            }
            _trayIcon?.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e) { _trayIcon?.Dispose(); base.OnExit(e); }
    }
}