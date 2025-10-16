using System.Globalization;
using System.Xml.Linq;
using SearchEngineService.Models;
using SearchEngineService.Transport;

namespace SearchEngineService.Providers
{
    public class XmlProviderClient : IProviderClient
    {
        public string Name => "ProviderXml";

        public Task<IReadOnlyList<NormalizedContent>> SearchAsync(string query, int page, int size, CancellationToken ct)
        {
            // Dosya adı: mocks/provider2.xml
            var doc = XDocument.Load("mocks/provider2.xml");

            // XML'de başlık "headline", video veya article (text) tipleri var.
            // stats altında views/likes veya reading_time/reactions var.
            var allItems = doc.Root!
                              .Element("items")!
                              .Elements("item")
                              .Select(x => new
                              {
                                  Id = (string)x.Element("id")!,
                                  Type = ((string?)x.Element("type"))?.ToLowerInvariant(),
                                  Headline = (string?)x.Element("headline"),
                                  Views = (int?)x.Element("stats")?.Element("views"),
                                  Likes = (int?)x.Element("stats")?.Element("likes"),
                                  ReadingTime = (int?)x.Element("stats")?.Element("reading_time"),
                                  Reactions = (int?)x.Element("stats")?.Element("reactions"),
                                  PubDateStr = (string?)x.Element("publication_date"),
                                  Categories = x.Element("categories")?.Elements("category").Select(c => (string)c).ToList()
                              })
                              .ToList();

            // Tarih: "YYYY-MM-DD" -> DateTime
            DateTime ParseDate(string? s)
                => DateTime.ParseExact(s ?? "1970-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

            var filtered = allItems
                .Where(i => (i.Headline ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
                .Skip((page - 1) * size)
                .Take(size)
                .Select(i => new NormalizedContent(
                    Provider: Name,
                    ExternalId: i.Id,
                    Type: i.Type == "video" ? ContentType.Video : ContentType.Text,  // "article" -> Text
                    Title: i.Headline ?? "",
                    Description: i.Categories is { Count: > 0 } ? string.Join(", ", i.Categories!) : null,
                    Url: $"mock://provider2/{i.Id}",
                    Views: i.Views,
                    Likes: i.Likes,
                    Reactions: i.Reactions,
                    ReadingTime: i.ReadingTime,
                    PublishedAt: ParseDate(i.PubDateStr)
                ))
                .ToList();

            return Task.FromResult<IReadOnlyList<NormalizedContent>>(filtered);
        }
    }
}
