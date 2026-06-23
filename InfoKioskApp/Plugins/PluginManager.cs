using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InfoKioskApp.Plugins
{
    /// <summary>
    /// Менеджер плагинов. Отвечает за:
    ///   - обнаружение C#-плагинов (классов с [Plugin(...)]) в текущей сборке;
    ///   - обнаружение JS-плагинов (папок plugins/&lt;id&gt;/ с plugin.json);
    ///   - инициализацию C#-плагинов с PluginContext;
    ///   - регистрацию обработчиков в WebMessageBridge (plugin.&lt;id&gt;.*);
    ///   - формирование списка JS-плагинов, который отдаётся киоску для
    ///     динамического добавления кнопок в сайдбар.
    ///
    /// Текущая реализация — фундамент. Сами плагины (Telegram, библиотека и т.д.)
    /// добавляются отдельными классами в Plugins/ или сборками рядом с exe.
    /// </summary>
    public static class PluginManager
    {
        private static readonly Dictionary<string, IPlugin> _plugins = new();
        private static readonly Dictionary<string, PluginContext> _contexts = new();
        private static readonly List<JsPluginManifest> _jsPlugins = new();
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        public static IReadOnlyList<IPlugin> Plugins => _plugins.Values.ToList();
        public static IReadOnlyList<JsPluginManifest> JsPlugins => _jsPlugins;

        /// <summary>
        /// Главная точка входа. Сканирует сборки на C#-плагины и папку plugins/
        /// на JS-плагины. Инициализирует C#-плагины, регистрирует их в bridge.
        ///
        /// Вызывается из WebMessageBridge.AttachWebView (или App startup).
        /// </summary>
        public static async Task LoadAllAsync(string pluginsRoot)
        {
            if (!Directory.Exists(pluginsRoot))
            {
                try { Directory.CreateDirectory(pluginsRoot); } catch { }
            }

            // 1) C#-плагины — ищем в текущей сборке все типы с [Plugin(...)].
            try
            {
                var pluginTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => SafeGetTypes(a))
                    .Where(t => t.GetCustomAttribute<PluginAttribute>() != null
                                && typeof(IPlugin).IsAssignableFrom(t)
                                && !t.IsAbstract && !t.IsInterface);

                foreach (var type in pluginTypes)
                {
                    try
                    {
                        var attr = type.GetCustomAttribute<PluginAttribute>();
                        if (string.IsNullOrEmpty(attr?.Id)) continue;
                        if (_plugins.ContainsKey(attr.Id))
                        {
                            Console.WriteLine($"[PluginManager] duplicate plugin id: {attr.Id}, skipping");
                            continue;
                        }

                        var plugin = (IPlugin)Activator.CreateInstance(type);
                        var ctx = BuildContext(attr.Id, pluginsRoot);
                        await plugin.InitAsync(ctx);
                        _plugins[attr.Id] = plugin;
                        _contexts[attr.Id] = ctx;
                        Console.WriteLine($"[PluginManager] loaded C# plugin: {attr.Id} ({attr.Name})");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PluginManager] failed to init plugin {type.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PluginManager] C# plugin scan failed: {ex.Message}");
            }

            // 2) JS-плагины — ищем plugins/<id>/plugin.json.
            try
            {
                if (Directory.Exists(pluginsRoot))
                {
                    foreach (var dir in Directory.GetDirectories(pluginsRoot))
                    {
                        var manifestPath = Path.Combine(dir, "plugin.json");
                        if (!File.Exists(manifestPath)) continue;
                        try
                        {
                            var json = File.ReadAllText(manifestPath);
                            var manifest = JsonConvert.DeserializeObject<JsPluginManifest>(json);
                            if (manifest == null || string.IsNullOrEmpty(manifest.Id)) continue;
                            manifest.Directory = dir;
                            _jsPlugins.Add(manifest);
                            Console.WriteLine($"[PluginManager] discovered JS plugin: {manifest.Id} ({manifest.Name})");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[PluginManager] bad plugin.json in {dir}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PluginManager] JS plugin scan failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Горячая перезагрузка JS-плагинов без перезапуска киоска.
        /// Заново сканирует папку plugins/ на наличие plugin.json,
        /// заменяет _jsPlugins новым списком. C#-плагины не трогает
        /// (они загружаются один раз при старте).
        ///
        /// Возвращает true, если список изменился (появились/исчезли/изменились плагины).
        /// </summary>
        public static bool ReloadJsPlugins(string pluginsRoot)
        {
            var fresh = new List<JsPluginManifest>();
            try
            {
                if (!Directory.Exists(pluginsRoot))
                {
                    Directory.CreateDirectory(pluginsRoot);
                }
                foreach (var dir in Directory.GetDirectories(pluginsRoot))
                {
                    var manifestPath = Path.Combine(dir, "plugin.json");
                    if (!File.Exists(manifestPath)) continue;
                    try
                    {
                        var json = File.ReadAllText(manifestPath);
                        var manifest = JsonConvert.DeserializeObject<JsPluginManifest>(json);
                        if (manifest == null || string.IsNullOrEmpty(manifest.Id)) continue;
                        manifest.Directory = dir;
                        fresh.Add(manifest);
                        Console.WriteLine($"[PluginManager] reloaded JS plugin: {manifest.Id} ({manifest.Name})");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PluginManager] bad plugin.json in {dir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PluginManager] reload scan failed: {ex.Message}");
            }

            // Сравниваем с текущим списком (по id + дате изменения plugin.json).
            bool changed = false;
            if (fresh.Count != _jsPlugins.Count)
            {
                changed = true;
            }
            else
            {
                for (int i = 0; i < fresh.Count; i++)
                {
                    var f = fresh[i];
                    var old = _jsPlugins.FirstOrDefault(p => p.Id == f.Id);
                    if (old == null
                        || old.Name != f.Name
                        || old.Icon != f.Icon
                        || old.Version != f.Version
                        || old.DefaultPosition != f.DefaultPosition
                        || old.ShowInSidebar != f.ShowInSidebar
                        || old.ShowInMore != f.ShowInMore
                        || old.BlockDuringLesson != f.BlockDuringLesson
                        || old.Order != f.Order
                        || (old.SettingsFields?.Count ?? 0) != (f.SettingsFields?.Count ?? 0))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            // Заменяем список в любом случае — даже если не изменился,
            // это безопасно (расширения получают свежие манифесты).
            _jsPlugins.Clear();
            _jsPlugins.AddRange(fresh);
            return changed;
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch { return Array.Empty<Type>(); }
        }

        private static PluginContext BuildContext(string pluginId, string pluginsRoot)
        {
            var dir = Path.Combine(pluginsRoot, pluginId);
            Directory.CreateDirectory(dir);
            var configPath = Path.Combine(dir, "config.json");
            JObject config = new();
            try
            {
                if (File.Exists(configPath))
                    config = JObject.Parse(File.ReadAllText(configPath));
            }
            catch { }

            return new PluginContext
            {
                HttpClient = _httpClient,
                PluginDirectory = dir,
                Config = config,
                Log = (msg) => Console.WriteLine($"[Plugin:{pluginId}] {msg}"),
                LogError = (msg) => Console.WriteLine($"[Plugin:{pluginId}] ERROR: {msg}")
            };
        }

        /// <summary>
        /// Маршрутизация вызова из JS. Если message имеет вид
        /// "plugin.&lt;id&gt;.&lt;method&gt;" — найти плагин и вызвать HandleCall.
        /// Возвращает null, если плагин не найден (bridge вернёт ошибку в JS).
        /// </summary>
        public static async Task<object> TryHandleCall(string fullMethod, JObject args)
        {
            if (string.IsNullOrEmpty(fullMethod) || !fullMethod.StartsWith("plugin."))
                return null;

            var parts = fullMethod.Split(new[] { '.' }, 3);
            if (parts.Length < 3) return null;

            string id = parts[1];
            string method = parts[2];

            if (!_plugins.TryGetValue(id, out var plugin))
                return new { error = $"Plugin '{id}' not found" };

            try
            {
                return await plugin.HandleCall(method, args ?? new JObject());
            }
            catch (Exception ex)
            {
                _contexts.TryGetValue(id, out var ctx);
                ctx?.LogError?.Invoke($"HandleCall({method}) failed: {ex.Message}");
                return new { error = ex.Message };
            }
        }

        /// <summary>
        /// Список JS-плагинов для передачи в киоск. Киоск добавит кнопки в
        /// сайдбар и подгрузит view.js/view.css при клике.
        /// Учитывает состояние (enabled) и положение (position) из ExtensionStateService.
        /// </summary>
        public static object GetJsPluginDescriptors()
        {
            return _jsPlugins.Select(p =>
            {
                var state = ExtensionStateService.Get(p.Id, p.DefaultPosition ?? "end");
                return new
                {
                    id = p.Id,
                    name = p.Name,
                    icon = p.Icon ?? "🔌",
                    order = p.Order,
                    showInSidebar = p.ShowInSidebar,
                    showInMore = state.ShowInMore ?? p.ShowInMore,
                    blockDuringLesson = state.BlockDuringLesson ?? p.BlockDuringLesson,
                    needsBackend = p.NeedsBackend,
                    enabled = state.Enabled,
                    position = state.Position,
                    settings = state.Settings,
                    viewJsUrl = $"/plugins/{p.Id}/view.js",
                    viewCssUrl = File.Exists(Path.Combine(p.Directory ?? "", "view.css"))
                        ? $"/plugins/{p.Id}/view.css"
                        : null
                };
            }).ToList();
        }

        /// <summary>
        /// Полный список расширений для админки: манифест + состояние + пути.
        /// </summary>
        public static object GetAdminDescriptors()
        {
            return _jsPlugins.Select(p =>
            {
                var state = ExtensionStateService.Get(p.Id, p.DefaultPosition ?? "end");
                return new
                {
                    id = p.Id,
                    name = p.Name,
                    icon = p.Icon ?? "🔌",
                    version = p.Version ?? "1.0.0",
                    author = p.Author ?? "",
                    description = p.Description ?? "",
                    order = p.Order,
                    defaultPosition = p.DefaultPosition ?? "end",
                    showInSidebar = p.ShowInSidebar,
                    showInMore = state.ShowInMore ?? p.ShowInMore,
                    blockDuringLesson = state.BlockDuringLesson ?? p.BlockDuringLesson,
                    needsBackend = p.NeedsBackend,
                    category = p.Category ?? "utility",
                    hasSettings = p.HasSettings,
                    settingsFields = p.SettingsFields ?? new List<ExtensionSettingField>(),
                    // Состояние.
                    enabled = state.Enabled,
                    position = state.Position,
                    settings = state.Settings ?? new JObject(),
                    // Пути.
                    directory = p.Directory ?? "",
                    hasViewJs = File.Exists(Path.Combine(p.Directory ?? "", "view.js")),
                    hasViewCss = File.Exists(Path.Combine(p.Directory ?? "", "view.css"))
                };
            }).ToList();
        }

        /// <summary>
        /// Обновляет состояние расширения (enabled/position/settings/showInMore/blockDuringLesson)
        /// и сохраняет в файл.
        /// </summary>
        public static void UpdateState(string extensionId, bool? enabled, string position,
            JObject settings, bool? showInMore = null, bool? blockDuringLesson = null)
        {
            var current = ExtensionStateService.Get(extensionId);
            if (enabled.HasValue) current.Enabled = enabled.Value;
            if (!string.IsNullOrEmpty(position)) current.Position = position;
            if (settings != null) current.Settings = settings;
            if (showInMore.HasValue) current.ShowInMore = showInMore.Value;
            if (blockDuringLesson.HasValue) current.BlockDuringLesson = blockDuringLesson.Value;
            ExtensionStateService.Set(extensionId, current);
        }

        /// <summary>
        /// Возвращает состояние расширения (для бэкенда-логики плагина).
        /// </summary>
        public static ExtensionState GetState(string extensionId)
        {
            return ExtensionStateService.Get(extensionId);
        }

        /// <summary>
        /// Завершение работы всех плагинов. Вызывается из MainWindow.Window_Closing.
        /// </summary>
        public static async Task ShutdownAsync()
        {
            foreach (var p in _plugins.Values)
            {
                try { await p.DisposeAsync(); }
                catch (Exception ex) { Console.WriteLine($"[PluginManager] Dispose failed for {p.Id}: {ex.Message}"); }
            }
            _plugins.Clear();
            _contexts.Clear();
        }
    }

    /// <summary>
    /// Манифест JS-плагина. Десериализуется из plugins/&lt;id&gt;/plugin.json.
    /// </summary>
    public sealed class JsPluginManifest
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("icon")]
        public string Icon { get; set; } = "🔌";

        [JsonProperty("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("order")]
        public int Order { get; set; } = 100;

        /// <summary>
        /// Положение расширения в сайдбаре киоска.
        ///   "start" — в начале (перед расписанием)
        ///   "after-schedule" — сразу после расписания
        ///   "after-news" — после новостей
        ///   "after-calendar" — после календаря
        ///   "end" — в конце (по умолчанию)
        /// </summary>
        [JsonProperty("defaultPosition")]
        public string DefaultPosition { get; set; } = "end";

        [JsonProperty("showInSidebar")]
        public bool ShowInSidebar { get; set; } = true;

        /// <summary>
        /// Показывать ли расширение в подменю «Дополнительно»
        /// (открывается отдельной кнопкой в сайдбаре). Для развлечений
        /// и редко используемых расширений.
        /// </summary>
        [JsonProperty("showInMore")]
        public bool ShowInMore { get; set; } = false;

        /// <summary>
        /// Блокировать ли расширение во время урока (когда идёт активный урок
        /// по расписанию звонков). Помогает отвлечь учеников от игр.
        /// </summary>
        [JsonProperty("blockDuringLesson")]
        public bool BlockDuringLesson { get; set; } = false;

        [JsonProperty("needsBackend")]
        public bool NeedsBackend { get; set; } = false;

        /// <summary>
        /// Категория расширения. Влияет на отображение в админке
        /// (иконка категории) и доступность функций (например, кнопка
        /// «Сбросить рекорды» только для category="game").
        /// Значения: "game", "utility", "school", "interactive", "info".
        /// </summary>
        [JsonProperty("category")]
        public string Category { get; set; } = "utility";

        [JsonProperty("hasSettings")]
        public bool HasSettings { get; set; } = false;

        /// <summary>
        /// Список полей настроек для админки. Каждое поле описывает
        /// один input (text/number/checkbox) с ключом, подписью, типом,
        /// значением по умолчанию и т.д.
        /// </summary>
        [JsonProperty("settingsFields")]
        public List<ExtensionSettingField> SettingsFields { get; set; } = new();

        [JsonIgnore]
        public string Directory { get; set; }
    }

    /// <summary>
    /// Одно поле настроек расширения. Описывается в plugin.json,
    /// рендерится админкой автоматически (без ручного HTML).
    /// </summary>
    public sealed class ExtensionSettingField
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        /// <summary>"text" | "number" | "checkbox" | "textarea" | "select"</summary>
        [JsonProperty("type")]
        public string Type { get; set; } = "text";

        [JsonProperty("placeholder")]
        public string Placeholder { get; set; }

        [JsonProperty("default")]
        public JToken Default { get; set; }

        [JsonProperty("required")]
        public bool Required { get; set; }

        [JsonProperty("min")]
        public JToken Min { get; set; }

        [JsonProperty("max")]
        public JToken Max { get; set; }

        /// <summary>Для type="select" — список вариантов {value, label}.</summary>
        [JsonProperty("options")]
        public List<ExtensionSettingOption> Options { get; set; }

        [JsonProperty("help")]
        public string Help { get; set; }
    }

    public sealed class ExtensionSettingOption
    {
        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }
    }
}
