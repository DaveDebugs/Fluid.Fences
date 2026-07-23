using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopFences
{

    public static class ConfigStore
    {
        private static readonly SemaphoreSlim _gate = new(1, 1);

        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true
        };

        public static string Path => System.IO.Path.Combine(App.ConfigFolder, "global_config.json");

        public static async Task<GlobalConfig> ReadAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { return ReadUnlocked(); }
            finally { _gate.Release(); }
        }

        public static async Task UpdateAsync(Action<GlobalConfig> mutate)
        {
            ArgumentNullException.ThrowIfNull(mutate);

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var config = ReadUnlocked();
                mutate(config);
                WriteUnlocked(config);
            }
            catch (Exception ex) { App.LogError("ConfigStore.Update", ex); }
            finally { _gate.Release(); }
        }

        public static void Update(Action<GlobalConfig> mutate)
        {
            ArgumentNullException.ThrowIfNull(mutate);

            _gate.Wait();
            try
            {
                var config = ReadUnlocked();
                mutate(config);
                WriteUnlocked(config);
            }
            catch (Exception ex) { App.LogError("ConfigStore.Update", ex); }
            finally { _gate.Release(); }
        }

        private static GlobalConfig ReadUnlocked()
        {
            try
            {
                if (!File.Exists(Path)) return new GlobalConfig();
                return JsonSerializer.Deserialize<GlobalConfig>(File.ReadAllText(Path), ReadOptions)
                       ?? new GlobalConfig();
            }
            catch (Exception ex)
            {
                App.LogError("ConfigStore.Read", ex);
                return new GlobalConfig();
            }
        }

        private static void WriteUnlocked(GlobalConfig config)
        {
            string temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(config, WriteOptions));
            File.Move(temp, Path, overwrite: true);
        }
    }
}
