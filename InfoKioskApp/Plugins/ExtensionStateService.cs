using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InfoKioskApp.Plugins
{
    /// <summary>
    /// Сервис состояния расширений. Хранит пользовательские настройки
    /// (включено/выключено, положение в киоске, значения полей настроек)
    /// в файле data/extensions-state.json.
    ///
    /// Файл имеет вид:
    /// {
    ///   "canteen": {
    ///     "enabled": true,
    ///     "position": "after-schedule",
    ///     "settings": {
    ///       "foodBlockId": "12345",
    ///       "autoDownload": true,
    ///       ...
    ///     }
    ///   },
    ///   "example-feed": { "enabled": false, "position": "end", "settings": {} }
    /// }
    /// </summary>
    public static class ExtensionStateService
    {
        private static string StateFilePath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "data", "extensions-state.json");

        private static readonly object _lock = new();
        private static Dictionary<string, ExtensionState> _cache;

        /// <summary>
        /// Загружает состояние из файла. Если файла нет — возвращает пустой словарь.
        /// </summary>
        public static Dictionary<string, ExtensionState> Load()
        {
            lock (_lock)
            {
                if (_cache != null) return _cache;
                try
                {
                    var dir = Path.GetDirectoryName(StateFilePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    if (!File.Exists(StateFilePath))
                    {
                        _cache = new Dictionary<string, ExtensionState>();
                        return _cache;
                    }
                    var json = File.ReadAllText(StateFilePath);
                    _cache = JsonConvert.DeserializeObject<Dictionary<string, ExtensionState>>(json)
                             ?? new Dictionary<string, ExtensionState>();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ExtensionState] Load failed: {ex.Message}");
                    _cache = new Dictionary<string, ExtensionState>();
                }
                return _cache;
            }
        }

        /// <summary>
        /// Сохраняет состояние в файл.
        /// </summary>
        public static void Save(Dictionary<string, ExtensionState> state)
        {
            lock (_lock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(StateFilePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var json = JsonConvert.SerializeObject(state, Formatting.Indented);
                    File.WriteAllText(StateFilePath, json, System.Text.Encoding.UTF8);
                    _cache = state;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ExtensionState] Save failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Возвращает состояние конкретного расширения. Если в файле нет записи —
        /// возвращает состояние по умолчанию (enabled=true, position=defaultPosition).
        /// </summary>
        public static ExtensionState Get(string extensionId, string defaultPosition = "end")
        {
            var state = Load();
            if (state.TryGetValue(extensionId, out var s)) return s;
            return new ExtensionState
            {
                Enabled = true,
                Position = defaultPosition,
                Settings = new JObject()
            };
        }

        /// <summary>
        /// Обновляет состояние одного расширения и сохраняет в файл.
        /// </summary>
        public static void Set(string extensionId, ExtensionState state)
        {
            var all = Load();
            all[extensionId] = state;
            Save(all);
        }
    }

    /// <summary>
    /// Состояние одного расширения: вкл/выкл, положение, значения настроек.
    /// </summary>
    public sealed class ExtensionState
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonProperty("position")]
        public string Position { get; set; } = "end";

        /// <summary>
        /// Показывать в подменю «Дополнительно». Если null — используется
        /// значение по умолчанию из манифеста (ShowInMore).
        /// </summary>
        [JsonProperty("showInMore")]
        public bool? ShowInMore { get; set; }

        /// <summary>
        /// Блокировать во время урока. Если null — значение из манифеста (BlockDuringLesson).
        /// </summary>
        [JsonProperty("blockDuringLesson")]
        public bool? BlockDuringLesson { get; set; }

        /// <summary>
        /// Значения полей настроек. Ключи — из SettingsFields[].key манифеста.
        /// </summary>
        [JsonProperty("settings")]
        public JObject Settings { get; set; } = new JObject();
    }
}
