using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace DesktopFences
{
    public partial class App : Application
    {
        // We use the full namespace path here to avoid colliding with WPF classes
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private bool _fencesHidden = false;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // ----------------------------------------------------------------------
            // 1. SYSTEM TRAY SETUP
            // ----------------------------------------------------------------------
            _notifyIcon = new System.Windows.Forms.NotifyIcon();

            // We use the default Windows application icon for now. 
            _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            _notifyIcon.Text = "Desktop Fences";
            _notifyIcon.Visible = true;

            // Create the right-click menu for the tray icon
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Create New Fence", null, Menu_CreateFence_Click);
            contextMenu.Items.Add("Toggle Visibility", null, Menu_ToggleVisibility_Click);
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator()); // Adds a nice dividing line
            contextMenu.Items.Add("Exit Fences", null, Menu_Exit_Click);

            _notifyIcon.ContextMenuStrip = contextMenu;

            // ----------------------------------------------------------------------
            // 2. THE SPAWNER LOGIC
            // ----------------------------------------------------------------------
            string saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences", "Fences");
            Directory.CreateDirectory(saveDirectory);

            string[] fenceFiles = Directory.GetFiles(saveDirectory, "*.json");

            if (fenceFiles.Length == 0)
            {
                // First time running, or user deleted all fences. Spawn one default fence.
                new MainWindow(Guid.NewGuid().ToString()).Show();
            }
            else
            {
                // Load all existing fences
                foreach (string file in fenceFiles)
                {
                    string fenceId = Path.GetFileNameWithoutExtension(file);
                    new MainWindow(fenceId).Show();
                }
            }
        }

        // ----------------------------------------------------------------------
        // 3. TRAY MENU CLICK EVENTS
        // ----------------------------------------------------------------------
        private void Menu_CreateFence_Click(object sender, EventArgs e)
        {
            // If the user tries to create a fence while they are all hidden, unhide them first!
            if (_fencesHidden) Menu_ToggleVisibility_Click(null, null);

            new MainWindow(Guid.NewGuid().ToString()).Show();
        }

        private void Menu_ToggleVisibility_Click(object sender, EventArgs e)
        {
            _fencesHidden = !_fencesHidden;

            // Loop through every open window in the application and hide/show them
            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow fence)
                {
                    fence.Visibility = _fencesHidden ? Visibility.Hidden : Visibility.Visible;
                }
            }
        }

        private void Menu_Exit_Click(object sender, EventArgs e)
        {
            // Clean up the icon so it doesn't get stuck in the user's taskbar
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            // Gracefully close all windows to trigger their auto-saves
            foreach (Window window in Application.Current.Windows.Cast<Window>().ToList())
            {
                window.Close();
            }

            // Force the background process to completely shut down
            Application.Current.Shutdown();
        }
    }
}