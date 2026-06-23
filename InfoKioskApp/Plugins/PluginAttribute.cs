using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace InfoKioskApp.Plugins
{
    /// <summary>
    /// Маркер плагина. Указывается над классом, реализующим <see cref="IPlugin"/>.
    /// PluginManager при загрузке ищет все типы с этим атрибутом и регистрирует
    /// их как обработчики сообщений plugin.&lt;Id&gt;.* в WebMessageBridge.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class PluginAttribute : System.Attribute
    {
        public string Id { get; }
        public string Name { get; set; }
        public string Icon { get; set; }
        public string Description { get; set; }
        public int Order { get; set; } = 100;
        public bool ShowInSidebar { get; set; } = true;

        public PluginAttribute(string id)
        {
            Id = id ?? "";
            Name = id ?? "";
            Icon = "🔌";
        }
    }
}
