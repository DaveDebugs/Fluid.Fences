using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopFences
{

    public sealed class RabbitEasterEgg : Window
    {
        private readonly MediaElement _media;
        private bool _closing;

        public RabbitEasterEgg()
        {
            Title = "\U0001F430";
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = Brushes.Black;
            ShowInTaskbar = false;
            Topmost = true;

            _media = new MediaElement
            {
                Width = 820,
                Height = 448,
                LoadedBehavior = MediaState.Manual,
                Stretch = Stretch.Uniform,
                IsMuted = true,
                ScrubbingEnabled = false
            };
            _media.MediaEnded += (_, _) =>
            {
                if (_closing) return;
                try { _media.Position = TimeSpan.Zero; _media.Play(); } catch { }
            };
            _media.MediaFailed += (_, _) => SafeClose();

            Content = _media;

            Loaded += (_, _) => StartPlayback();
            MouseLeftButtonDown += (_, _) => SafeClose();
            KeyDown += (_, e) => { if (e.Key == Key.Escape) SafeClose(); };
            Deactivated += (_, _) => SafeClose();
        }

        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            try { _media.Stop(); } catch { }
            try { Close(); } catch { }
        }

        private void StartPlayback()
        {
            try
            {
                string path = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Assets", "easter_rabbit.mp4");
                if (!File.Exists(path)) { SafeClose(); return; }

                _media.Source = new Uri(path, UriKind.Absolute);
                _media.Play();
            }
            catch { SafeClose(); }
        }
    }
}
