namespace InfoKioskApp.Models
{
    public class ScheduleFile
    {
        public string Name { get; set; }        // Название для кнопки
        public string FilePath { get; set; }    // Путь к файлу
        public string Type { get; set; }        // excel / pdf / image
    }
}
