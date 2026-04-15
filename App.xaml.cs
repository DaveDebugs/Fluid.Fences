using System;
using System.Windows;
using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Application;
using Microsoft.Win32;
using System.IO;

namespace DesktopFences
{
    public partial class App : Application
    {
        private NotifyIcon? _trayIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            CreateTrayIcon();
            RestoreSavedFences();
        }

        private void RestoreSavedFences()
        {
            string saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences", "Fences");

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
                    }
                    return;
                }
            }

            MainWindow defaultFence = new(Guid.NewGuid().ToString());
            defaultFence.Show();
        }

        private void CreateTrayIcon()
        {
            var iconUri = new Uri("pack://application:,,,/Assets/FFICONsmall.ico");
            using var iconStream = Application.GetResourceStream(iconUri)?.Stream;

            if (iconStream == null)
            {
                throw new FileNotFoundException("Could not load the embedded icon resource.");
            }

            _trayIcon = new NotifyIcon
            {
                Icon = new Icon(iconStream),
                Visible = true,
                Text = "Fluid Fences"
            };

            var trayMenu = new ContextMenuStrip();

            trayMenu.Items.Add("Create New Fence", null, (s, e) => CreateNewFence());
            trayMenu.Items.Add("Create Folder Portal...", null, (s, e) => CreateNewPortal());
            trayMenu.Items.Add("Open Settings", null, (s, e) => OpenSettings());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit Fluid Fences", null, (s, e) => ExitApp());

            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.DoubleClick += (s, e) => OpenSettings();
        }

        private void CreateNewFence()
        {
            MainWindow newFence = new(Guid.NewGuid().ToString());
            newFence.Show();
        }

        private void CreateNewPortal()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            dialog.Title = "Select a folder to mirror in the Portal Fence";

            if (dialog.ShowDialog() == true)
            {
                MainWindow portalFence = new(Guid.NewGuid().ToString());

                portalFence.Left = SystemParameters.PrimaryScreenWidth / 2 - 125;
                portalFence.Top = SystemParameters.PrimaryScreenHeight / 2 - 150;

                portalFence.MakePortal(dialog.FolderName);
                portalFence.Show();
            }
        }

        private void OpenSettings()
        {
            SettingsWindow settings = new SettingsWindow(null);
            settings.Show();
        }

        private void ExitApp()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            Application.Current.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
            }
            base.OnExit(e);
        }
    }
}