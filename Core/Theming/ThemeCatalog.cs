using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DesktopFences.Core.Theming
{

    public static class ThemeCatalog
    {
        private const string BuiltinResourcePrefix = "DesktopFences.Themes.Builtin.";
        public const string ThemePackageExtension = ".fftheme";

        public static string? LastImportProblem { get; private set; }

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private static readonly ObservableCollection<ThemeDefinition> _themes = new();

        public static ReadOnlyObservableCollection<ThemeDefinition> Themes { get; } = new(_themes);

        public static string UserThemeFolder
        {
            get
            {
                string path = Path.Combine(App.ConfigFolder, "Themes");
                try { Directory.CreateDirectory(path); } catch { }
                return path;
            }
        }

        public static void Reload()
        {
            var loaded = new List<ThemeDefinition>();

            foreach (var theme in LoadBuiltins())
                loaded.Add(theme);

            foreach (var theme in LoadUserThemes())
            {
                int existing = loaded.FindIndex(t =>
                    string.Equals(t.Id, theme.Id, StringComparison.OrdinalIgnoreCase));

                if (existing >= 0) loaded[existing] = theme;
                else loaded.Add(theme);
            }

            _themes.Clear();
            foreach (var theme in loaded
                         .OrderByDescending(t => t.IsBuiltIn)
                         .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                _themes.Add(theme);
            }

            if (_themes.Count == 0)
            {
                var fallback = ThemeDefinition.Fallback;
                fallback.IsBuiltIn = true;
                _themes.Add(fallback);
            }
        }

        private static IEnumerable<ThemeDefinition> LoadBuiltins()
        {
            var asm = Assembly.GetExecutingAssembly();

            foreach (string name in asm.GetManifestResourceNames())
            {
                if (!name.StartsWith(BuiltinResourcePrefix, StringComparison.Ordinal)) continue;
                if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

                ThemeDefinition? theme = null;
                try
                {
                    using Stream? stream = asm.GetManifestResourceStream(name);
                    if (stream is null) continue;

                    theme = JsonSerializer.Deserialize<ThemeDefinition>(stream, ReadOptions);
                    if (theme is null) continue;

                    theme.IsBuiltIn = true;
                    theme.SourcePath = null;

                    if (string.IsNullOrWhiteSpace(theme.Id))
                        theme.Id = Slugify(theme.Name);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ThemeCatalog] Built-in '{name}' failed to load: {ex.Message}");
                    continue;
                }

                yield return theme;
            }
        }

        private static IEnumerable<ThemeDefinition> LoadUserThemes()
        {
            string folder = UserThemeFolder;
            if (!Directory.Exists(folder)) yield break;

            string[] files;
            try { files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThemeCatalog] Cannot enumerate user themes: {ex.Message}");
                yield break;
            }

            foreach (string file in files)
            {
                ThemeDefinition? theme = TryLoadFile(file);
                if (theme is not null) yield return theme;
            }
        }

        public static ThemeDefinition? TryLoadFile(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                var theme = JsonSerializer.Deserialize<ThemeDefinition>(json, ReadOptions);
                if (theme is null) return null;

                theme.IsBuiltIn = false;
                theme.SourcePath = path;

                if (string.IsNullOrWhiteSpace(theme.Id))
                    theme.Id = Slugify(Path.GetFileNameWithoutExtension(path));
                if (string.IsNullOrWhiteSpace(theme.Name))
                    theme.Name = Path.GetFileNameWithoutExtension(path);

                return theme;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThemeCatalog] User theme '{path}' failed to load: {ex.Message}");
                return null;
            }
        }

        public static ThemeDefinition Get(string? idOrName)
        {
            if (_themes.Count == 0) Reload();

            if (!string.IsNullOrWhiteSpace(idOrName))
            {
                var byId = _themes.FirstOrDefault(t =>
                    string.Equals(t.Id, idOrName, StringComparison.OrdinalIgnoreCase));
                if (byId is not null) return byId;

                var byName = _themes.FirstOrDefault(t =>
                    string.Equals(t.Name, idOrName, StringComparison.OrdinalIgnoreCase));
                if (byName is not null) return byName;
            }

            return Default;
        }

        public static ThemeDefinition Default =>
            _themes.FirstOrDefault(t => t.Id == "fluid-glass")
            ?? _themes.FirstOrDefault()
            ?? ThemeDefinition.Fallback;

        public static bool Exists(string id) =>
            _themes.Any(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

        public static async Task<ThemeDefinition> SaveAsync(ThemeDefinition theme, string? newName = null,
                                                            string? overwriteId = null)
        {
            var (copy, path, json) = PrepareForSave(theme, newName, overwriteId);

            string temp = path + ".tmp";

            await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);

            Reload();
            return Get(copy.Id);
        }

        public static ThemeDefinition Save(ThemeDefinition theme, string? newName = null,
                                           string? overwriteId = null)
        {
            var (copy, path, json) = PrepareForSave(theme, newName, overwriteId);

            string temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);

            Reload();
            return Get(copy.Id);
        }

        private static (ThemeDefinition Copy, string Path, string Json) PrepareForSave(
            ThemeDefinition theme, string? newName, string? overwriteId = null)
        {
            ArgumentNullException.ThrowIfNull(theme);

            var copy = Clone(theme);

            if (!string.IsNullOrWhiteSpace(newName)) copy.Name = newName!;

            if (!string.IsNullOrWhiteSpace(overwriteId))
            {

                copy.Id = overwriteId!;
                copy.IsBuiltIn = false;
                string fixedPath = Path.Combine(UserThemeFolder, copy.Id + ".json");
                copy.SourcePath = fixedPath;
                return (copy, fixedPath, JsonSerializer.Serialize(copy, WriteOptions));
            }

            if (theme.IsBuiltIn)
            {
                copy.Id = UniqueId(Slugify(copy.Name));
                if (string.Equals(copy.Name, theme.Name, StringComparison.Ordinal))
                    copy.Name = $"{theme.Name} (Custom)";
            }
            else if (string.IsNullOrWhiteSpace(copy.Id))
            {
                copy.Id = UniqueId(Slugify(copy.Name));
            }

            copy.IsBuiltIn = false;

            string path = Path.Combine(UserThemeFolder, copy.Id + ".json");
            copy.SourcePath = path;

            return (copy, path, JsonSerializer.Serialize(copy, WriteOptions));
        }

        public static bool Delete(ThemeDefinition theme)
        {
            if (theme is null || theme.IsBuiltIn) return false;
            if (string.IsNullOrWhiteSpace(theme.SourcePath)) return false;

            try
            {
                if (File.Exists(theme.SourcePath)) File.Delete(theme.SourcePath);
                Reload();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThemeCatalog] Delete failed: {ex.Message}");
                return false;
            }
        }

        public static async Task ExportAsync(ThemeDefinition theme, string destinationPath, string? mediaPath = null)
        {
            ArgumentNullException.ThrowIfNull(theme);

            string staging = Path.Combine(Path.GetTempPath(), "ff_theme_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);

            try
            {
                var payload = Clone(theme);
                payload.SourcePath = null;

                await File.WriteAllTextAsync(
                    Path.Combine(staging, "theme.json"),
                    JsonSerializer.Serialize(payload, WriteOptions));

                if (!string.IsNullOrWhiteSpace(mediaPath) && File.Exists(mediaPath))
                {
                    string mediaDir = Path.Combine(staging, "media");
                    Directory.CreateDirectory(mediaDir);
                    File.Copy(mediaPath, Path.Combine(mediaDir, Path.GetFileName(mediaPath)), overwrite: true);
                }

                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                ZipFile.CreateFromDirectory(staging, destinationPath, CompressionLevel.Optimal, false);
            }
            finally
            {
                try { Directory.Delete(staging, true); } catch { }
            }
        }

        public static async Task<ThemeDefinition?> ImportAsync(string sourcePath)
        {
            if (!File.Exists(sourcePath)) return null;

            if (Path.GetExtension(sourcePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                var direct = TryLoadFile(sourcePath);
                if (direct is null) return null;
                direct.Id = UniqueId(string.IsNullOrWhiteSpace(direct.Id) ? Slugify(direct.Name) : direct.Id);
                return await SaveAsync(direct);
            }

            string staging = Path.Combine(Path.GetTempPath(), "ff_import_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(staging);
                ExtractSafely(sourcePath, staging);

                string manifest = Path.Combine(staging, "theme.json");
                if (!File.Exists(manifest))
                {
                    manifest = Directory.GetFiles(staging, "*.json", SearchOption.AllDirectories).FirstOrDefault() ?? "";
                    if (!File.Exists(manifest)) return null;
                }

                var theme = TryLoadFile(manifest);
                if (theme is null) return null;

                string mediaDir = Path.Combine(staging, "media");
                if (Directory.Exists(mediaDir))
                {
                    string target = Path.Combine(UserThemeFolder, "media");
                    Directory.CreateDirectory(target);
                    foreach (string file in Directory.GetFiles(mediaDir))
                    {
                        try { File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true); }
                        catch { }
                    }
                }

                theme.Id = UniqueId(string.IsNullOrWhiteSpace(theme.Id) ? Slugify(theme.Name) : theme.Id);
                return await SaveAsync(theme);
            }
            catch (InvalidDataException ex)
            {

                ThemeLog.Warn("Import", $"Refused '{Path.GetFileName(sourcePath)}': {ex.Message}");
                LastImportProblem = ex.Message;
                return null;
            }
            catch (Exception ex)
            {
                ThemeLog.Error("Import", ex);
                LastImportProblem = null;
                return null;
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }
        }

        private const int    MaxArchiveEntries      = 64;
        private const long   MaxTotalUncompressed   = 64L * 1024 * 1024;
        private const long   MaxSingleEntryBytes    = 48L * 1024 * 1024;
        private const double MaxCompressionRatio    = 120.0;

        private static void ExtractSafely(string archivePath, string destination)
        {
            string root = Path.GetFullPath(destination);
            if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;

            using ZipArchive archive = ZipFile.OpenRead(archivePath);

            if (archive.Entries.Count > MaxArchiveEntries)
                throw new InvalidDataException(
                    $"Theme package has {archive.Entries.Count} entries; the limit is {MaxArchiveEntries}.");

            long declaredTotal = 0;
            foreach (ZipArchiveEntry e in archive.Entries) declaredTotal += e.Length;

            if (declaredTotal > MaxTotalUncompressed)
                throw new InvalidDataException(
                    $"Theme package expands to {declaredTotal / (1024 * 1024)} MB; the limit is {MaxTotalUncompressed / (1024 * 1024)} MB.");

            long written = 0;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                string target = Path.GetFullPath(Path.Combine(root, entry.FullName));

                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Theme package tried to write outside its folder ('{entry.FullName}').");

                if (entry.Length > MaxSingleEntryBytes)
                    throw new InvalidDataException(
                        $"'{entry.FullName}' is {entry.Length / (1024 * 1024)} MB; the limit is {MaxSingleEntryBytes / (1024 * 1024)} MB.");

                if (entry.CompressedLength > 0 &&
                    (double)entry.Length / entry.CompressedLength > MaxCompressionRatio)
                    throw new InvalidDataException(
                        $"'{entry.FullName}' is compressed {entry.Length / entry.CompressedLength}x, which is not plausible for theme content.");

                written += entry.Length;
                if (written > MaxTotalUncompressed)
                    throw new InvalidDataException("Theme package exceeded its size budget while extracting.");

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        public static ThemeDefinition Clone(ThemeDefinition source)
        {
            string json = JsonSerializer.Serialize(source, WriteOptions);
            var clone = JsonSerializer.Deserialize<ThemeDefinition>(json, ReadOptions)!;
            clone.IsBuiltIn = source.IsBuiltIn;
            clone.SourcePath = source.SourcePath;
            return clone;
        }

        public static string Slugify(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "theme";
            string slug = Regex.Replace(input.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            return string.IsNullOrEmpty(slug) ? "theme" : slug;
        }

        private static string UniqueId(string baseId)
        {
            if (!Exists(baseId) && !File.Exists(Path.Combine(UserThemeFolder, baseId + ".json")))
                return baseId;

            for (int i = 2; i < 1000; i++)
            {
                string candidate = $"{baseId}-{i}";
                if (!Exists(candidate) && !File.Exists(Path.Combine(UserThemeFolder, candidate + ".json")))
                    return candidate;
            }
            return $"{baseId}-{Guid.NewGuid():N}";
        }
    }
}
