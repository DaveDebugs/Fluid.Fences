using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopFences.Core.Theming
{

    public enum WallpaperSource
    {
        None,
        StaticFile,
        DesktopLayer,
        ScreenCapture
    }

    public sealed record WallpaperSample(byte[] Bgra, int Width, int Height, WallpaperSource Source);

    public static class WallpaperSampler
    {
        private const int SPI_GETDESKWALLPAPER = 0x0073;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SystemParametersInfoW")]
        private static extern bool SystemParametersInfo(int uAction, int uParam, [Out] char[] lpvParam, int fuWinIni);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindWindowW")]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindWindowExW")]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr dest, int dx, int dy, int w, int h,
                                          IntPtr src, int sx, int sy, uint rop);

        private const uint SRCCOPY = 0x00CC0020;

        public static WallpaperSample? Capture(bool allowScreenCapture = false, uint? monitorIndex = null, Rect? region = null)
        {
            var fromFile = TryStaticFile(monitorIndex);
            if (fromFile is not null) return fromFile;

            var fromDesktop = TryDesktopLayer();
            if (fromDesktop is not null) return fromDesktop;

            if (allowScreenCapture)
            {
                var fromScreen = TryScreenCapture(region);
                if (fromScreen is not null) return fromScreen;
            }

            return null;
        }

        public static WallpaperSample? CaptureMany(int frames = 4, int delayMs = 320,
                                                   bool allowScreenCapture = false,
                                                   uint? monitorIndex = null,
                                                   Rect? region = null)
        {
            frames = Math.Clamp(frames, 1, 12);

            var pooled = new List<byte>();
            int width = 0, height = 0;
            WallpaperSource source = WallpaperSource.None;

            for (int i = 0; i < frames; i++)
            {
                var frame = Capture(allowScreenCapture, monitorIndex, region);
                if (frame is null) continue;

                if (frame.Source == WallpaperSource.StaticFile) return frame;

                if (width == 0) { width = frame.Width; height = frame.Height; source = frame.Source; }
                if (frame.Width != width || frame.Height != height) continue;

                pooled.AddRange(frame.Bgra);

                if (i < frames - 1) System.Threading.Thread.Sleep(delayMs);
            }

            if (pooled.Count == 0 || width == 0) return null;

            int pooledHeight = pooled.Count / 4 / width;
            ThemeLog.Info("WallpaperSampler",
                $"pooled {pooled.Count / 4 / (width * height)} frame(s) from {source}");

            return new WallpaperSample(pooled.ToArray(), width, pooledHeight, source);
        }

        private static WallpaperSample? TryScreenCapture(Rect? region = null)
        {
            IntPtr screenDc = IntPtr.Zero, memoryDc = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                screenDc = GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero) return null;

                int srcX, srcY, srcW, srcH;
                if (region is Rect r && r.Width >= 1 && r.Height >= 1)
                {
                    srcX = (int)r.X; srcY = (int)r.Y;
                    srcW = (int)r.Width; srcH = (int)r.Height;
                }
                else
                {
                    srcX = 0; srcY = 0;
                    srcW = (int)SystemParameters.PrimaryScreenWidth;
                    srcH = (int)SystemParameters.PrimaryScreenHeight;
                }
                if (srcW <= 0 || srcH <= 0) return null;

                int captureW = Math.Min(srcW, 640);
                int captureH = Math.Max(1, (int)(srcH * (captureW / (double)srcW)));

                memoryDc = CreateCompatibleDC(screenDc);
                bitmap = CreateCompatibleBitmap(screenDc, captureW, captureH);
                previous = SelectObject(memoryDc, bitmap);

                if (!StretchBlt(memoryDc, 0, 0, captureW, captureH,
                                screenDc, srcX, srcY, srcW, srcH, SRCCOPY))
                    return null;

                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                converted.Freeze();

                int stride = converted.PixelWidth * 4;
                var pixels = new byte[converted.PixelHeight * stride];
                converted.CopyPixels(pixels, stride, 0);

                if (IsEffectivelyBlank(pixels)) return null;

                return new WallpaperSample(pixels, converted.PixelWidth, converted.PixelHeight,
                                           WallpaperSource.ScreenCapture);
            }
            catch (Exception ex)
            {
                ThemeLog.Warn("WallpaperSampler", $"Screen capture route failed: {ex.Message}");
                return null;
            }
            finally
            {

                if (previous != IntPtr.Zero) _ = SelectObject(memoryDc, previous);
                if (bitmap != IntPtr.Zero) _ = DeleteObject(bitmap);
                if (memoryDc != IntPtr.Zero) _ = DeleteDC(memoryDc);
                if (screenDc != IntPtr.Zero) _ = ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
                                              IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);

        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID,
                              [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID);

            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetMonitorDevicePathAt(uint monitorIndex);

            uint GetMonitorDevicePathCount();

        }

        [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
        private class DesktopWallpaperClass { }

        public static string? GetWallpaperPathForMonitor(uint monitorIndex)
        {
            object? com = null;
            try
            {
                com = new DesktopWallpaperClass();
                if (com is not IDesktopWallpaper wallpaper) return null;

                uint count = wallpaper.GetMonitorDevicePathCount();
                if (count == 0 || monitorIndex >= count) return null;

                string monitorId = wallpaper.GetMonitorDevicePathAt(monitorIndex);
                if (string.IsNullOrWhiteSpace(monitorId)) return null;

                string path = wallpaper.GetWallpaper(monitorId);
                return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : path;
            }
            catch (Exception ex)
            {
                ThemeLog.Warn("WallpaperSampler", $"Per-monitor wallpaper unavailable: {ex.Message}");
                return null;
            }
            finally
            {
                if (com is not null && Marshal.IsComObject(com))
                {
                    try { Marshal.ReleaseComObject(com); } catch { }
                }
            }
        }

        public static uint GetMonitorCount()
        {
            object? com = null;
            try
            {
                com = new DesktopWallpaperClass();
                return com is IDesktopWallpaper w ? w.GetMonitorDevicePathCount() : 0;
            }
            catch { return 0; }
            finally
            {
                if (com is not null && Marshal.IsComObject(com))
                {
                    try { Marshal.ReleaseComObject(com); } catch { }
                }
            }
        }

        private static WallpaperSample? TryStaticFile(uint? monitorIndex = null)
        {
            try
            {
                string? path = null;

                if (monitorIndex is uint index) path = GetWallpaperPathForMonitor(index);

                if (string.IsNullOrWhiteSpace(path))
                {
                    var buffer = new char[520];
                    if (!SystemParametersInfo(SPI_GETDESKWALLPAPER, buffer.Length, buffer, 0))
                        return null;

                    int end = Array.IndexOf(buffer, '\0');
                    path = new string(buffer, 0, end < 0 ? buffer.Length : end);
                }

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);

                bitmap.DecodePixelWidth = 320;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                converted.Freeze();

                int stride = converted.PixelWidth * 4;
                var pixels = new byte[converted.PixelHeight * stride];
                converted.CopyPixels(pixels, stride, 0);

                return new WallpaperSample(pixels, converted.PixelWidth, converted.PixelHeight,
                                           WallpaperSource.StaticFile);
            }
            catch (Exception ex)
            {
                ThemeLog.Warn("WallpaperSampler", $"Static file route failed: {ex.Message}");
                return null;
            }
        }

        private static WallpaperSample? TryDesktopLayer()
        {
            IntPtr source = FindWallpaperWindow();
            IntPtr hdcSource = IntPtr.Zero, hdcMemory = IntPtr.Zero, hBitmap = IntPtr.Zero, oldObject = IntPtr.Zero;

            try
            {
                if (!GetWindowRect(source, out RECT rect)) return null;

                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width <= 0 || height <= 0) return null;

                int captureW = Math.Min(width, 640);
                int captureH = Math.Max(1, (int)(height * (captureW / (double)width)));

                hdcSource = GetDC(source);
                if (hdcSource == IntPtr.Zero) return null;

                hdcMemory = CreateCompatibleDC(hdcSource);
                hBitmap = CreateCompatibleBitmap(hdcSource, captureW, captureH);
                oldObject = SelectObject(hdcMemory, hBitmap);

                if (!BitBlt(hdcMemory, 0, 0, captureW, captureH, hdcSource, 0, 0, SRCCOPY))
                    return null;

                var bmpSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                var converted = new FormatConvertedBitmap(bmpSource, PixelFormats.Bgra32, null, 0);
                converted.Freeze();

                int stride = converted.PixelWidth * 4;
                var pixels = new byte[converted.PixelHeight * stride];
                converted.CopyPixels(pixels, stride, 0);

                if (IsEffectivelyBlank(pixels))
                {
                    ThemeLog.Warn("WallpaperSampler", "Desktop layer blitted blank; live renderer likely bypasses GDI.");
                    return null;
                }

                return new WallpaperSample(pixels, converted.PixelWidth, converted.PixelHeight,
                                           WallpaperSource.DesktopLayer);
            }
            catch (Exception ex)
            {
                ThemeLog.Warn("WallpaperSampler", $"Desktop layer route failed: {ex.Message}");
                return null;
            }
            finally
            {
                if (oldObject != IntPtr.Zero) _ = SelectObject(hdcMemory, oldObject);
                if (hBitmap != IntPtr.Zero) _ = DeleteObject(hBitmap);
                if (hdcMemory != IntPtr.Zero) _ = DeleteDC(hdcMemory);
                if (hdcSource != IntPtr.Zero && source != IntPtr.Zero) _ = ReleaseDC(source, hdcSource);
            }
        }

        private static IntPtr FindWallpaperWindow()
        {
            try
            {
                IntPtr progman = FindWindow("Progman", null);
                if (progman != IntPtr.Zero)
                {

                    _ = SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

                    IntPtr worker = IntPtr.Zero;
                    IntPtr candidate = IntPtr.Zero;
                    while ((candidate = FindWindowEx(IntPtr.Zero, candidate, "WorkerW", null)) != IntPtr.Zero)
                    {
                        if (FindWindowEx(candidate, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                            worker = candidate;
                    }
                    if (worker != IntPtr.Zero) return worker;

                    return progman;
                }
            }
            catch (Exception ex) { ThemeLog.Warn("WallpaperSampler", $"WorkerW lookup failed: {ex.Message}"); }

            return GetDesktopWindow();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                                                        uint flags, uint timeout, out IntPtr result);

        private static bool IsEffectivelyBlank(byte[] bgra)
        {
            if (bgra.Length < 64) return true;

            byte minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;
            for (int i = 0; i + 3 < bgra.Length; i += 4 * 37)
            {
                byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                if (r < minR) minR = r; if (r > maxR) maxR = r;
                if (g < minG) minG = g; if (g > maxG) maxG = g;
                if (b < minB) minB = b; if (b > maxB) maxB = b;
            }
            return (maxR - minR) < 6 && (maxG - minG) < 6 && (maxB - minB) < 6;
        }

        public static Color? AverageColor(bool allowScreenCapture = false)
        {
            var sample = Capture(allowScreenCapture);
            if (sample is null) return null;

            double r = 0, g = 0, b = 0;
            int n = 0;
            for (int i = 0; i + 3 < sample.Bgra.Length; i += 4)
            {
                if (sample.Bgra[i + 3] < 8) continue;
                r += ColorSpace.ToLinear(sample.Bgra[i + 2] / 255.0);
                g += ColorSpace.ToLinear(sample.Bgra[i + 1] / 255.0);
                b += ColorSpace.ToLinear(sample.Bgra[i] / 255.0);
                n++;
            }
            if (n == 0) return null;

            return Color.FromRgb(
                (byte)Math.Round(Math.Clamp(ColorSpace.ToSrgb(r / n) * 255, 0, 255)),
                (byte)Math.Round(Math.Clamp(ColorSpace.ToSrgb(g / n) * 255, 0, 255)),
                (byte)Math.Round(Math.Clamp(ColorSpace.ToSrgb(b / n) * 255, 0, 255)));
        }
    }
}
