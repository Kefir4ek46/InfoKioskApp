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
        /// <summary>
        /// Кто опубликовал новость (если новость опубликована другим редактором,
        /// а не автором). null, если новость опубликовал автор сам или администратор.
        /// </summary>
        public string? PublishedByLogin { get; set; }
        public string? PublishedByName { get; set; }
        /// <summary>
        /// true, если новость была опубликована автоматически (у автора были права
        /// на публикацию без модерации). Используется для отображения бейджа.
        /// </summary>
        public bool AutoPublished { get; set; }
    }

    public class NewsEditor
    {
        public string Name { get; set; } = "";
        public string Login { get; set; } = "";
        public string Password { get; set; } = "";
        public string? TrustedDeviceId { get; set; }
        public bool Active { get; set; } = true;
        /// <summary>
        /// Право на публикацию новостей без подтверждения администратором
        /// (значок короны в админке). Такие редакторы также могут публиковать
        /// новости других редакторов, находящиеся на модерации.
        /// </summary>
        public bool CanPublishWithoutApproval { get; set; } = false;
        /// <summary>
        /// Имя для отображения, которое редактор может менять сам.
        /// Если пусто — используется Name (задаётся администратором при создании).
        /// </summary>
        public string? DisplayName { get; set; }
    }
}
