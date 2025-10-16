using SearchEngineService.Models;

namespace SearchEngineService.Services
{
    public interface IScoringService
    {
        ContentScore Compute(Content c, DateTime nowUtc);
    }

    public class ScoringService : IScoringService
    {
        public ContentScore Compute(Content c, DateTime nowUtc)
        {
            double baseScore = 0, typeWeight = 1.0, recency = 0, engagement = 0;

            if (c.Type == ContentType.Video)
            {
                var views = Math.Max(0, c.Views ?? 0);
                var likes = Math.Max(0, c.Likes ?? 0);
                baseScore = (views / 1000.0) + (likes / 100.0);
                typeWeight = 1.5;
                engagement = views == 0 ? 0 : ((likes / Math.Max(1.0, views)) * 10.0);
            }
            else
            {
                var rt = Math.Max(0, c.ReadingTime ?? 0);
                var reactions = Math.Max(0, c.Reactions ?? 0);
                baseScore = rt + (reactions / 50.0);
                typeWeight = 1.0;
                engagement = rt == 0 ? 0 : ((reactions / Math.Max(1.0, rt)) * 5.0);
            }

            var ageDays = (nowUtc - c.PublishedAt).TotalDays;
            recency = ageDays <= 7 ? 5 : ageDays <= 30 ? 3 : ageDays <= 90 ? 1 : 0;

            var final = (baseScore * typeWeight) + recency + engagement;

            return new ContentScore
            {
                ContentId = c.Id,
                BaseScore = Math.Round(baseScore, 3),
                TypeWeight = typeWeight,
                RecencyScore = recency,
                EngagementScore = Math.Round(engagement, 3),
                FinalPopularityScore = Math.Round(final, 4)
            };
        }
    }
}
