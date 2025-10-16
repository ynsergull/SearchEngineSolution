using Microsoft.EntityFrameworkCore;
using SearchEngineService.Data;
using SearchEngineService.Models;
using SearchEngineService.Transport;

namespace SearchEngineService.Services
{
    public interface IIngestService
    {
        Task<List<Content>> IngestAsync(IReadOnlyList<NormalizedContent> items, CancellationToken ct);
    }

    public class IngestService : IIngestService
    {
        private readonly AppDbContext _db;
        private readonly IScoringService _scoring;

        public IngestService(AppDbContext db, IScoringService scoring)
        {
            _db = db; _scoring = scoring;
        }

        public async Task<List<Content>> IngestAsync(IReadOnlyList<NormalizedContent> items, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var result = new List<Content>();

            foreach (var n in items)
            {
                var entity = await _db.Contents.Include(c => c.Score)
                    .FirstOrDefaultAsync(c => c.Provider == n.Provider && c.ExternalId == n.ExternalId, ct);

                if (entity == null)
                {
                    entity = new Content
                    {
                        Provider = n.Provider,
                        ExternalId = n.ExternalId,
                        Type = n.Type,
                        Title = n.Title,
                        Description = n.Description,
                        Url = n.Url,
                        Views = n.Views,
                        Likes = n.Likes,
                        Reactions = n.Reactions,
                        ReadingTime = n.ReadingTime,
                        PublishedAt = n.PublishedAt
                    };
                    _db.Contents.Add(entity);
                }
                else
                {
                    entity.Type = n.Type; entity.Title = n.Title; entity.Description = n.Description;
                    entity.Url = n.Url; entity.Views = n.Views; entity.Likes = n.Likes;
                    entity.Reactions = n.Reactions; entity.ReadingTime = n.ReadingTime;
                    entity.PublishedAt = n.PublishedAt; entity.UpdatedAt = now;
                }

                entity.Score = _scoring.Compute(entity, now);
                result.Add(entity);
            }

            await _db.SaveChangesAsync(ct);
            return result;
        }
    }
}
