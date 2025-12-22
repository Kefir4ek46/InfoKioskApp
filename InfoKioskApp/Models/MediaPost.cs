namespace InfoKioskApp.Models
{
    public class MediaPost
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Category { get; set; } = "";
        public string Title { get; set; } = "";
        public string Cover { get; set; } = "";
        public string Date { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
        public string Description { get; set; } = "";

        public List<MediaImage> Images { get; set; } = [];
    }

    public class MediaImage
    {
        public string File { get; set; }
    }
}
