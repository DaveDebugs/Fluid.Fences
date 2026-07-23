using System;
using System.Diagnostics;
using System.IO;

namespace DesktopFences.Core.Theming
{

    internal static class ThemeLog
    {
        private static readonly object _lock = new();

        private static string LogPath => Path.Combine(App.ConfigFolder, "app_error.log");

        internal static void Warn(string context, string message)
        {
            Debug.WriteLine($"[Theming] {context}: {message}");
            Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Theming_{context}: {message}");
        }

        internal static void Error(string context, Exception ex)
        {
            Debug.WriteLine($"[Theming] {context}: {ex}");
            Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Theming_{context}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }

        internal static void Info(string context, string message)
        {
            Debug.WriteLine($"[Theming] {context}: {message}");
            Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Theming_{context}: {message}");
        }

        private static void Write(string line)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(App.ConfigFolder);
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
            catch
            {

            }
        }
    }
}
