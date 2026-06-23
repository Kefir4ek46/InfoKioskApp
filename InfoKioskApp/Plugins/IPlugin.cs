using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace InfoKioskApp.Plugins
{
    /// <summary>
    /// Контекст, который PluginManager передаёт каждому плагину при инициализации.
    /// Содержит только то, что плагину действительно нужно — никаких ссылок на
    /// внутренние сервисы киоска, только HttpClient, конфиг и логирование.
    /// </summary>
    public sealed class PluginContext
    {
        /// <summary>
        /// Глобальный HttpClient с настроенными таймаутами. Плагины ДОЛЖНЫ
        /// использовать его для внешних HTTP-запросов, чтобы киоск мог
        /// корректно завершаться (HttpClient.Dispose() закроет все соединения).
        /// </summary>
        public HttpClient HttpClient { get; set; }

        /// <summary>
        /// Корневая директория плагина (обычно plugins/&lt;id&gt;/).
        /// Здесь плагин может хранить свой кэш, логи, временные файлы.
        /// </summary>
        public string PluginDirectory { get; set; }

        /// <summary>
        /// Конфигурация плагина из plugins/&lt;id&gt;/config.json.
        /// Если файла нет — пустой объект.
        /// </summary>
        public JObject Config { get; set; }

        /// <summary>
        /// Логирование в общий лог киоска (Console + файл).
        /// </summary>
        public System.Action<string> Log { get; set; }

        /// <summary>
        /// Логирование ошибок.
        /// </summary>
        public System.Action<string> LogError { get; set; }
    }

    /// <summary>
    /// Интерфейс, который должен реализовать каждый C#-плагин.
    /// </summary>
    public interface IPlugin
    {
        /// <summary>Уникальный идентификатор плагина (например, "telegram-feed").</summary>
        string Id { get; }

        /// <summary>Человекочитаемое имя для сайдбара.</summary>
        string Name { get; }

        /// <summary>Emoji-иконка для сайдбара.</summary>
        string Icon { get; }

        /// <summary>
        /// Вызывается один раз при загрузке плагина. Здесь плагин может
        /// открыть соединения с внешними сервисами, прочитать конфиг и т.д.
        /// </summary>
        Task InitAsync(PluginContext ctx);

        /// <summary>
        /// Обработчик вызова из JS. method — суффикс после "plugin.&lt;Id&gt;."
        /// (например, "list", "search", "subscribe"). args — JSON-объект
        /// с параметрами. Возвращаемый объект сериализуется в JSON и уходит в JS.
        /// </summary>
        Task<object> HandleCall(string method, JObject args);

        /// <summary>
        /// Вызывается при завершении работы киоска. Плагин должен закрыть
        /// все свои соединения и освободить ресурсы.
        /// </summary>
        Task DisposeAsync();
    }
}
