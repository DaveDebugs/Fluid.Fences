using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace DesktopFences
{
    public partial class App : Application
    {
        private static Mutex? _mutex = null;
        private NotifyIcon? _trayIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. SINGLE INSTANCE MUTEX: Prevent multiple versions of the app from running simultaneously
            const string appName = "FluidFencesSingleInstance_v1";
            _mutex = new Mutex(true, appName, out bool createdNew);

            if (!createdNew)
            {
                Application.Current.Shutdown();
                return;
            }

            // 2. BACKGROUND MODE: Keep the app alive in the system tray even if all fences are deleted/closed
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);

            InitializeTrayIcon();
            LoadSavedFences();
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon();
            // Automatically grabs your app's main icon file
            _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName);
            _trayIcon.Visible = true;
            _trayIcon.Text = "Fluid Fences";

            // Double-click to open Settings
            _trayIcon.DoubleClick += (s, args) => { new SettingsWindow(null).Show(); };

            // Right-click menu
            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem newFenceItem = new ToolStripMenuItem("Create New Fence");
            newFenceItem.Click += (s, args) => { new MainWindow(Guid.NewGuid().ToString()).Show(); };

            ToolStripMenuItem settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (s, args) => { new SettingsWindow(null).Show(); };

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit Fluid Fences");
            exitItem.Click += (s, args) => {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                Application.Current.Shutdown();
            };

            menu.Items.Add(newFenceItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
        }

        private void LoadSavedFences()
        {
            string configFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences");
            string globalConfigPath = Path.Combine(configFolder, "global_config.json");
            string saveDirectory = Path.Combine(configFolder, "Fences");

            // Check if this is the first time the app has ever been run
            if (!File.Exists(globalConfigPath))
            {
                Directory.CreateDirectory(configFolder);
                File.WriteAllText(globalConfigPath, "{\"FirstRunComplete\": true, \"ShowTaskbarIcon\": true}");

                // Spawn default fence and welcome screen
                MainWindow defaultFence = new MainWindow("designer");
                defaultFence.Show();
                new SettingsWindow(defaultFence, true).ShowDialog();
                return;
            }

            // Load all existing fences
            if (Directory.Exists(saveDirectory))
            {
                string[] files = Directory.GetFiles(saveDirectory, "*.json");
                if (files.Length > 0)
                {
                    foreach (string file in files)
                    {
                        new MainWindow(Path.GetFileNameWithoutExtension(file)).Show();
                    }
                    return;
                }
            }

            // If they deleted all fences but run the app, spawn an empty one so they aren't confused
            new MainWindow(Guid.NewGuid().ToString()).Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            base.OnExit(e);
        }
    }
}