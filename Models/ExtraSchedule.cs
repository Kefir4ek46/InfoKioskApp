namespace InfoKioskApp.Models
{
    public class ExtraSchedule
    {
        public string Name { get; set; }         // Название (отображается в списке)
        public string Path { get; set; }         // Путь к файлу (Excel, PDF, изображение)
        public string Type { get; set; }         // Тип файла: "excel", "pdf", "image" и т.д.
    }
}
