using System;
using System.Collections.Generic;

namespace InfoKioskApp.Models
{
    public class NewsPost
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string AuthorLogin { get; set; } = "";
        public string AuthorName { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Status { get; set; } = "pending"; // pending/published/rejected
        public string? VideoUrl { get; set; }
        public string? LinkUrl { get; set; }
        public string? VideoFile { get; set; }
        public int? VideoWidth { get; set; }
        public int? VideoHeight { get; set; }
        public List<string> PhotoFiles { get; set; } = new();
        public DateTime? ModeratedAt { get; set; }
        public string? RejectReason { get; set; }
    }

    public class NewsEditor
    {
        public string Name { get; set; } = "";
        public string Login { get; set; } = "";
        public string Password { get; set; } = "";
        public string? TrustedDeviceId { get; set; }
        public bool Active { get; set; } = true;
    }
}
