namespace InfoKioskApp.Models
{
    public class CustomSection
    {
        public string Name { get; set; }      // Название раздела (для кнопки)
        public string FolderPath { get; set; } // Путь к папке
        public string Type { get; set; }       // Тип контента: "pdf", "image" и т.д.
        public string Icon { get; set; } = "📁"; // Иконка по умолчанию
    }
}
