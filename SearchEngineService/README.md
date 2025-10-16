SearchEngineService

Farklı sağlayıcılardan (JSON & XML) gelen içerikleri normalize eden, skorlayan ve arama yapan .NET 8 Web API.
Üstüne tek dosyalık bir dashboard (wwwroot/index.html) ile sonuçları görsel listeleme.

Case hedefi: “provider → normalize → score → search” zincirini kurmak; kalite için FULLTEXT relevance, performans için Redis cache eklemek.

İçindekiler

Özellikler

Hızlı Başlangıç

Mimari ve Akış

Veri Modeli

Skorlama Formülü

API

Dashboard

Loglama & Cache

Geliştirme tercihleri

Ekran Görüntüleri

Troubleshooting

Yol Haritası

Özellikler

Providers: IProviderClient arayüzü + JsonProviderClient, XmlProviderClient (mock dosyalar).

Normalize → Ingest → Score: idempotent upsert + ScoringService ile final ve breakdown skorları.

Arama:

query (opsiyonel) → varsa FULLTEXT WHERE

type: all|video|text

sort: popularity (final skor) veya relevance (FULLTEXT alaka)

sayfalama: page, size

Relevance: MySQL FULLTEXT (MATCH(Title, Description) AGAINST(...)).

Popularity: case formülüne göre FinalPopularityScore.

Cache: IDistributedCache ile 60 sn sorgu bazlı Redis cache.

Swagger: /swagger

Dashboard: / (wwwroot/index.html)

Serilog: console + request logging.

Hızlı Başlangıç

Gereksinimler: .NET 8 SDK, Docker Desktop.

1) Konteynerlar

docker compose up -d
# MySQL (weg-mysql), Redis (weg-redis) ayağa kalkar


2) İlk kurulum

dotnet tool install --global dotnet-ef
dotnet ef database update   # migration'lar uygulanır, indeksler oluşur (FULLTEXT dahil)


3) Çalıştır

dotnet run
# Swagger:   https://localhost:7220/swagger
# Dashboard: https://localhost:7220/


İlk aramada ingest + skor hesaplanır; aynı parametreyle ikinci arama cache’ten gelir (Serilog’da CACHE_HIT görürsün).

Mimari ve Akış
Controllers
  └── SearchController      # GET /api/Search
Providers
  ├── IProviderClient       # arayüz
  ├── JsonProviderClient    # provider1.json -> Normalize
  └── XmlProviderClient     # provider2.xml -> Normalize
Services
  ├── IngestService         # Upsert (Contents + ContentScores) + score hesaplama
  └── ScoringService        # Formüller
Data
  └── AppDbContext          # EF Core; FullText index migration
Models
  └── Content, ContentScore
Transport
  └── NormalizedContent     # provider -> ingest DTO
wwwroot
  └── index.html            # dashboard (tek dosya)


Akış
/api/Search:

Cache Key üret (query/type/sort/page/size) → 60 sn kontrol.

Cache miss → provider’lardan getir, normalize et → IngestService ile upsert + skorlama.

DB’de query varsa FULLTEXT WHERE uygula.

sort=relevance ise relevance skoru ile sırala; popularity ise FinalPopularityScore ile.

page/size ile sayfalayıp döndür → cache’e yaz (60 sn).

Veri Modeli

Contents

Id (Guid PK)

Provider, ExternalId (UNIQUE çift)

Type: video|text

Title, Description, Url

Views, Likes (video için)
ReadingTime, Reactions (text için)

PublishedAt, CreatedAt, UpdatedAt

Index: Type

FULLTEXT: Title, Description

ContentScores (1–1)

ContentId (FK & PK)

BaseScore, TypeWeight, RecencyScore, EngagementScore

FinalPopularityScore

Skorlama Formülü

Video

base = views/1000 + likes/100

typeWeight = 1.5

engagement = (likes / max(views,1)) * 10

Text

base = readingTime + reactions/50

typeWeight = 1.0

engagement = (reactions / max(readingTime,1)) * 5

Recency

<7 gün: +5, <30 gün: +3, <90 gün: +1, aksi: 0

Final

FinalPopularityScore = (base * typeWeight) + recency + engagement


Breakdown skorları ContentScores tablosunda saklanır ve API yanıtına dahil edilir.

API
GET /api/Search
  ?query=go
  &type=all|video|text
  &sort=popularity|relevance
  &page=1
  &size=20


200

{
  "meta": { "page": 1, "size": 20, "total": 5 },
  "results": [
    {
      "id": "GUID",
      "title": "Advanced Go Concurrency Patterns",
      "type": "video",
      "score": 69.84,
      "score_breakdown": { "baseScore": 46, "typeWeight": 1.5, "recency": 0, "engagement": 0.84 },
      "provider": "ProviderJson",
      "published_at": "2024-03-14T00:00:00Z",
      "url": "mock://provider1/v2"
    }
  ]
}


Hata durumları

400 – boş query (istenirse) / geçersiz parametre

500 – beklenmeyen hata (global handler ile problem+json)

Dashboard

Adres: / (wwwroot/index.html)

Ne yapar?: Formdan parametre alır, API’yi fetch ile çağırır, kart grid’inde sonuçları gösterir.

Özellikler: pagination, score breakdown, provider ve yayın tarihi bilgisi, link.

Loglama & Cache

Serilog: Console’da request logları ve özel satırlar; örn. CACHE_HIT {Key} / CACHE_MISS {Key}.

Redis Cache: IDistributedCache ile 60 sn TTL. Key formatı:

search:{query}:{type}:{sort}:{page}:{size}


FULLTEXT: Migration ile Contents(Title,Description) üzerine FULLTEXT index. Relevance modunda EF.Functions.Match(...).

Geliştirme tercihleri

GUID PK: dağınık sistemlerde çakışmasız anahtar. (Alternatif: UUIDv7/BINARY(16))

Upsert: UNIQUE (Provider, ExternalId) ile idempotent ingest.

Sıralama tutarlılığı: query varsa her iki modda da FULLTEXT WHERE uygulanır; fark sadece ORDER BY.

Performans: AsNoTracking, projection, cache.

Genişleyebilirlik: Yeni provider → yeni sınıf + DI kaydı.

Ekran Görüntüleri

dosyaları docs/ klasörüne koyup README içinde referansla.

Önerilen kareler:

Swagger – arama endpoint’i (parametreler + response bölümü açık)
docs/swagger-search.png

Dashboard – sonuçlar (senin attığın kılavuz görsel çok iyi)
docs/dashboard.png

Docker – MySQL & Redis container’ları up (Docker Desktop ekranı)
docs/docker.png (opsiyonel)

DBeaver – Contents & ContentScores tabloları ve FULLTEXT index
docs/schema.png (opsiyonel)

Serilog – CACHE_HIT/CACHE_MISS log çıktısı
docs/cache-log.png (opsiyonel)