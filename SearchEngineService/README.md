Aşağıdaki içeriği proje köküne README.md olarak koyabilirsin.

# SearchEngineService

JSON ve XML formatlı iki farklı sağlayıcıdan (provider) veri toplayıp normalize eden, bu içerikleri skorlama formülüne göre puanlayan ve **popülerlik** veya **alakalılık (relevance)** skoruna göre sıralayıp sunan bir arama servisi.

> **Bonus:** `wwwroot/index.html` altında tek sayfalık bir dashboard ile arama sonuçlarını görselleştirir.

---

## İçerik

- [Mimari ve Bileşenler](#mimari-ve-bileşenler)
- [Skorlama Formülü](#skorlama-formülü)
- [Kurulum ve Çalıştırma](#kurulum-ve-çalıştırma)
- [Konfigürasyon](#konfigürasyon)
- [API](#api)
- [Dashboard](#dashboard)
- [Test Stratejisi](#test-stratejisi)
- [Teknoloji Tercihleri ve Gerekçeler](#teknoloji-tercihleri-ve-gerekçeler)
- [İyileştirme Alanları](#iyileştirme-alanları)

---

## Mimari ve Bileşenler

**Katmanlar**
- **Providers**: `JsonProviderClient` ve `XmlProviderClient` mock dosyalardan veri okur (`mocks/provider1.json`, `mocks/provider2.xml`). Bu provider'lar `ResilientProviderClient` dekoratörü sayesinde **sağlayıcı bazlı rate limit, retry ve circuit breaker** politikalarıyla korunur (ayarlanabilir konfigürasyon).
- **IngestService**: Sağlayıcılardan gelen ham verileri **normalize edip** DB’ye upsert eder ve **ScoringService** ile puanlar.
- **ScoringService**: Formüle göre `Score` üretir (detay aşağıda).
- **IContentSearch** (strateji):  
  - `MySqlContentSearch`: MySQL’de `EF.Functions.Match` ile **FULLTEXT** arama ve relevance sıralaması.  
  - `InMemoryContentSearch`: Test/in-memory ortamı için `Contains` tabanlı arama + tie‑break ile sıralama.  
  - `FallbackContentSearch`: Koşula göre uygun stratejiyi seçer.
- **API**: `SearchController` uç noktası. Rate limit, cache (Redis), global exception handling (ProblemDetails) ve Serilog logları içerir.
- **Veri**: `AppDbContext` + MySQL (prod), EF Core **InMemory** (test). `Content` ve ilişik `Score` entity’leri.

**Akış**
1. İstek geldiğinde cache anahtarına bakılır; varsa 60 sn TTL ile döner.
2. Cache miss ise provider’lardan veri toplanır → normalize edilir → `IngestService` ile **upsert + score**.
3. `IContentSearch` stratejisi ile DB üzerinde filtre/sıralama/paginasyon uygulanır.
4. Sonuçlar hem response hem de cache’e yazılır.

---

## Skorlama Formülü

> **FinalSkor = (TemelPuan * İçerikTürüKatsayısı) + GüncellikPuanı + EtkileşimPuanı**

**Temel Puan**  
- Video: `views / 1000 + (likes / 100)`  
- Metin: `reading_time + (reactions / 50)`

**İçerik Türü Katsayısı**  
- Video: `1.5`  
- Metin: `1.0`

**Güncellik Puanı**  
- 1 hafta içinde: `+5`  
- 1 ay içinde: `+3`  
- 3 ay içinde: `+1`  
- Daha eski: `+0`

**Etkileşim Puanı**  
- Video: `(likes / views) * 10`  
- Metin: `(reactions / reading_time) * 5`

> Bu kurallar case dökümanındaki gereksinimlerin birebir karşılığıdır. (Bkz. “İçerik Puanlama Formülü”).  

---

## Kurulum ve Çalıştırma

### Gereksinimler
- .NET 8 SDK
- MySQL (prod) ve Redis (cache). Testler için gerekli değil.
- `dotnet` CLI

### Kurulum
```bash
# bağımlılıklar
dotnet restore

# veritabanı (prod)
# MySQL’de Title/Description için FULLTEXT indekslerini oluştur:
# ALTER TABLE Contents ADD FULLTEXT INDEX ft_title_description (Title, Description);

# çalıştırma (Swagger açık)
dotnet run --project SearchEngineService

EF Sürüm Uyarısı (isteğe bağlı düzeltme)

Projede EF Core paket sürümlerini eşitlemek için csproj’da aynı patch versiyonunu kullanın:

<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.10" />
  <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="9.0.10" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="9.0.10" />
</ItemGroup>

Konfigürasyon

appsettings.json

{
  "ConnectionStrings": { "Default": "Server=...;Database=...;User=...;Password=...;SslMode=None" },
  "Redis": { "Configuration": "localhost:6379" },
  "ProviderResilience": {
    "Default": {
      "MaxConcurrentRequests": 2,
      "RetryCount": 2,
      "RetryBaseDelayMilliseconds": 200,
      "CircuitBreakerFailures": 3,
      "CircuitBreakerDurationSeconds": 30
    },
    "Providers": {
      "ProviderJson": { "MaxConcurrentRequests": 3, "RetryCount": 3 },
      "ProviderXml": { "MaxConcurrentRequests": 1 }
    }
  }
}


Program.cs (özet)

RateLimiter: IP başına 60 istek/dk

Serilog: Console sink

HTTP Client + Polly: 429 ve geçici hatalara WaitAndRetry

Provider dayanıklılığı: `ProviderResilience` konfigürasyonuna göre provider başına eş zamanlı istek limiti, retry ve circuit breaker (`ResilientProviderClient`).

/health: DB ve Redis ping

Swagger (Development)

Static files: / → wwwroot/index.html dashboard

DI:

IScoringService, IIngestService

IProviderClient (JSON/XML + resiliency dekoratörü)

IContentSearch (Fallback → MySql/InMemory)

API
GET /api/Search

Query parametreleri

query: arama metni (zorunlu)

type: video | text | all (varsayılan: all)

sort: popularity | relevance (varsayılan: popularity)

page: 1..n (varsayılan: 1)

size: 1..50 (varsayılan: 20)

Örnek

/api/Search?query=go&type=all&sort=relevance&page=1&size=20


Response

{
  "meta": { "page": 1, "size": 20, "total": 42 },
  "results": [
    {
      "id": "GUID",
      "title": "Clean Architecture in Go",
      "type": "video|text",
      "score": 298.25,
      "score_breakdown": {
        "baseScore": 123.45,
        "typeWeight": 1.5,
        "recency": 5,
        "engagement": 2.34
      },
      "provider": "ProviderJson",
      "published_at": "2024-03-14T00:00:00Z",
      "url": "https://..."
    }
  ]
}


Hata Kodları

400: validasyon (boş query, geçersiz type/sort/size)

429: rate limit aşıldı (Retry-After başlığı gelir)

500: genel hata (ProblemDetails)

Dashboard

wwwroot/index.html tek sayfa.

Arama kutusu, tür ve sıralama seçimleri, sayfalama, kart listesi.

Kartta başlık, tür (Video/Metin), skor ve breakdown linki görüntülenir.

Relevance vs popularity seçimi anlık değiştirilebilir.

Bu kısım case dokümanındaki “Dashboard” beklentisini karşılar.

Test Stratejisi

Neden böyle test ettik?

Provider’lar gerçekte “mock” kaynaklardan okuyor (JSON/XML). Bu yüzden test ortamında gerçek I/O’ya bağlı olmadan uçtan uca akışı doğruladık.

Gerçek dünyada da dış servisler için doğrudan çağrı yapmak yerine **fake/double** provider kullanmak yaygın (rate limit/kota, deterministik sonuç ve hata senaryolarını tetikleme ihtiyaçları nedeniyle). Testlerde `FakeProviderClient` aynı sözleşmeyi kullanarak provider davranışını simüle eder; böylece iş kuralları gerçek sisteme dokunmadan doğrulanır.

MySQL FULLTEXT ifadesi (EF.Functions.Match) InMemory provider’da desteklenmediği için, IContentSearch stratejisini soyutlayıp testte InMemoryContentSearch ile aynı akışı çalıştırdık. Bu sayede sorgu → ingest → DB → sıralama → cevap zincirini kırmadan test ettik.

Testler

IngestServiceTests.Ingest_Should_Upsert_And_Score

Normalized içerikleri DB’ye upsert eder.

ScoringService’in formüle göre Score ürettiğini doğrular.

SearchControllerTests.Get_Should_Return_200_With_Results

/api/Search?query=go&type=all&sort=popularity çağrısı 200 döner ve sonuç listesi gelir.

Test DI:

DB: EF InMemory

Cache: InMemory IDistributedCache

Providers: `FakeProviderClient` (deterministik fake provider)

IContentSearch: InMemory strateji (relevance/contains + popularity tie‑break)

SearchControllerTests.Get_Should_Return_400_When_Query_Empty

Boş query ile 400 ve uygun ProblemDetails döner.

ResilientProviderClientTests.SearchAsync_Retries_OnTransientFailure

Sağlayıcı bir kez hata verdiğinde retry politikasının devreye girdiğini doğrular.

ResilientProviderClientTests.SearchAsync_OpensCircuit_AfterConfiguredFailures

Ardışık hatalar sonrasında circuit breaker'ın devreye girdiğini doğrular.

Genişletebileceğin ek testler

type=video/text filtreleri (yalnızca ilgili tiplerin gelmesi).

sort=relevance için stabil sıralama (eşit match’lerde popularity/publishedAt tie‑break).

size sınırları (0, 51 gibi değerler → 400).

Cache hit testi: aynı parametrelerle iki çağrıda ikinci yanıtın cache’den gelmesi.

Idempotent upsert (aynı içerik iki kez ingest edilince çoğalma olmaması).

Teknoloji Tercihleri ve Gerekçeler

.NET 8 + ASP.NET Core: Minimal API/Controller, güçlü DI, test/observability araçları.

EF Core + MySQL (Pomelo): FULLTEXT desteği ile relevance sıralaması.

Redis (IDistributedCache): Parametre kombinasyonuna göre 60 sn TTL ile cache.

Polly: Sağlayıcı tarafında rate limit + retry + circuit breaker politikalarını uygulayan `ResilientProviderClient` dekoratörü.

Rate Limiting: IP başına 60/dk.

Serilog: structured logging.

Swagger: hızlı dokümantasyon ve deneme.

İyileştirme Alanları

Relevance geliştirmeleri: TF/IDF, alan ağırlıkları, stop-word çıkarımı.

Veri tazeliği: Provider ingest akışını zamanlayıp güncel içerik önceliği tanımlamak.

Observability: Provider bazlı metrikler (istek sayısı, retry/circuit durumları) ve distributed tracing entegrasyonu.

Dedupe: Aynı URL veya güçlü hash ile yinelenen içerikleri önlemek.

Daha fazla test: Cache hit senaryosu, rate limiter davranışı, relevance sıralama varyasyonları.


---

## 4) PDF’e göre son kontrol (kısa rapor)

- **Arama + filtre + sıralama + sayfalama** → Var. :contentReference[oaicite:4]{index=4}  
- **Skorlama formülü** → Birebir uygulandı (Temel + Tür katsayısı + Güncellik + Etkileşim). :contentReference[oaicite:5]{index=5}  
- **2 provider (JSON/XML) + normalize + DB’de saklama** → Var. :contentReference[oaicite:6]{index=6}  
- **Cache** → `IDistributedCache` (Redis). :contentReference[oaicite:7]{index=7}  
- **Yeni provider eklemeye uygun yapı** → `IProviderClient` ile adapter deseni, evet. :contentReference[oaicite:8]{index=8}  
- **Dashboard (opsiyonel)** → Var (tek sayfa). :contentReference[oaicite:9]{index=9}  
- **İstek limiti yönetimi** → API seviyesinde IP başına 60/dk + provider bazlı rate limit / retry / circuit breaker. :contentReference[oaicite:10]{index=10}
- **Dokümantasyon** → Swagger + README (bu çıktı). :contentReference[oaicite:11]{index=11}  
- **Test** → Var; mantıklı bir strateji ile (InMemory + mock providers). :contentReference[oaicite:12]{index=12}

Genel olarak gereksinimleri **karşılıyorsun**. Relevance geliştirmeleri ve veri tazeliği stratejisi gibi ileri adımlar sonraki iterasyonlar için değerlendirilebilir.

---

## 5) EF sürüm hizalama (uyarıyı kaldırmak istersen)

Uyarı: *“EFCore.Relational 9.0.0 ile 9.0.10 arasında çakışma…”*  
Çözüm: Tüm EF Core paketlerini **aynı patch** versiyonuna sabitle:
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.10" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="9.0.10" />
  <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="9.0.10" />
</ItemGroup>


Ardından:

dotnet restore
dotnet build


Bu adım zorunlu değil, sadece uyarıyı temizler.