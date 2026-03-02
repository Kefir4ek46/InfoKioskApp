using System;

namespace InfoKioskApp.Models
{
    public class HonorPerson
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FullName { get; set; } = "";
        public string Description { get; set; } = "";
        public string PhotoFile { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
