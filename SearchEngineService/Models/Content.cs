namespace SearchEngineService.Models
{
    public enum ContentType { Video = 1, Text = 2 }

    public class Content
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Provider { get; set; } = default!;
        public string ExternalId { get; set; } = default!;
        public ContentType Type { get; set; }
        public string Title { get; set; } = default!;
        public string? Description { get; set; }
        public string Url { get; set; } = default!;
        public int? Views { get; set; }
        public int? Likes { get; set; }
        public int? Reactions { get; set; }
        public int? ReadingTime { get; set; }
        public DateTime PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ContentScore Score { get; set; } = null!;
    }

    public class ContentScore
    {
        public Guid ContentId { get; set; }
        public double BaseScore { get; set; }
        public double TypeWeight { get; set; }
        public double RecencyScore { get; set; }
        public double EngagementScore { get; set; }
        public double FinalPopularityScore { get; set; }
        public Content Content { get; set; } = null!;
    }
}
