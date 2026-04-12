Fluid Fences is a high-performance, open-source desktop organization utility built with .NET 8 and WPF. It allows you to create customizable "fences" on your desktop to house shortcuts, files, and folders, keeping your workspace clean and efficient.

This project was born out of a desire for a desktop organizer that felt truly "smooth" and native to Windows. It served as a primary learning project for coding, utilizing AI as a teaching tool while overcoming the limitations of AI in replicating complex, low-level Windows interactions.

🚀 Key Features
Native Windows Integration: Uses direct Win32 API hooks to provide a seamless experience, including native Acrylic blur effects and rounded corners.

Intelligent Docking & Physics: Features a custom physics engine with hysteresis-based "corner stickiness," allowing fences to snap perfectly to screen edges without accidentally flipping orientation.

Roll-up Mode: Double-click a fence header to collapse it into a space-saving bar. Hover to temporarily reveal contents, or double-click again to lock it open.

High-Performance Icon Engine: A 4-tier asynchronous extraction system ensures the UI never freezes while loading icons:

Native Loader: Fast loading for standard image formats.

Shell API: Extracts thumbnails and video frames.

Binary Extractor: Rips jumbo 256x256 icons directly from .exe and .dll files.

Legacy Fallback: Reliable 32x32 extraction for unknown file types.

Advanced Customization: * Full HSL/RGB color control with transparency.

Eyedropper Tool: Pick any color directly from your monitor to match your wallpaper perfectly.

Wallpaper Engine Compatible: Configured as a "Tool Window" so it doesn't pause animated wallpapers.

System Tray Management: Runs silently in the tray to manage multiple fences and global settings.

Auto-Organize: Automatically pull files from your desktop into specific fences based on file extensions.

🛠 Technical Highlights
Fluid Fences utilizes several advanced concepts that make it a great reference for developers:

Single Instance Mutex: Ensures only one instance of the application runs at a time.

Win32 Interop: Extensive use of user32.dll, dwmapi.dll, shell32.dll, and gdi32.dll to achieve functionality outside standard WPF limits.

Asynchronous UI: Heavy file system and Shell operations are offloaded to background threads to keep the user interface responsive.

JSON Persistence: All fence states, positions, and customizations are saved via System.Text.Json in the user's AppData folder.

📥 Installation
Download the latest release from the Releases page.

Run the installer and follow the prompts.

On first run, a "Designer" fence will open to help you get started.

💡 How to Use
Create New Fence: Right-click the system tray icon or an existing fence header.

Add Files: Drag and drop files directly from Windows Explorer into a fence.

Select Multiple: Click and drag in an empty area of a fence to use the blue selection box.

Settings: Access global settings like "Start with Windows" through the tray icon or fence menu.

❤️ Support the Project
If you find this tool useful and want to help keep the internet on, consider a donation:

BuyMeACoffee: @Davedebugs

Venmo: @Davedebugs

📜 License
This project is licensed under the GNU General Public License v3.0. See the LICENSE file for details.

© 2026 [Davedebugs -- David Daniel]