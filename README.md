SearchEngineService

JSON ve XML tabanlı sağlayıcılardan içerik toplayıp normalize eden, popülerlik ve alakalılık skorlarına göre sıralayan arama servisi; tek sayfalık dashboard ile sonuçları görselleştirir.

🔥 Öne Çıkanlar

Çoklu sağlayıcı adaptörleri: ProviderJson ve ProviderXml mock dosyalardan veri okuyup ortak NormalizedContent modeline dönüştürür.

Normalize → upsert → skorla zinciri: IngestService, provider verilerini idempotent şekilde MySQL’e yazar ve aynı anda ScoringService ile puanlar.

Çift arama stratejisi: MySQL FULLTEXT araması veya in-memory fallback, relevans ve popülerlik sıralamalarını tie-breaker’larla yönetir.

Dayanıklılık ve performans: 60/dk rate limit, Redis cache (60 sn TTL), Polly tabanlı yeniden deneme ve global hata yakalama hazır gelir.

UI + API entegrasyonu: Dashboard, arama parametrelerini yönetir, rate limit/hata mesajlarını kullanıcıya gösterir.

📚 İçindekiler

Hızlı Başlangıç

Mimari ve İş Akışı

Veri modeli ve depolama

Sağlayıcı adaptörleri

Ingest ve skor üretimi

Arama stratejisi

API hattı

Altyapı kesitleri

Skorlama Modeli

API Referansı

Dashboard

Testler

Teknoloji Seçimleri

🚀 Hızlı Başlangıç
1) Gereksinimler

.NET 8 SDK ve dotnet CLI.

MySQL 8.0+ ve Redis 7 (yerel veya container).

Geliştirme için isteğe bağlı olarak Docker Compose.

2) Bağımlılık servislerini başlat
docker compose up -d


YAML dosyası MySQL’i FULLTEXT parametreleriyle, Redis’i de varsayılan portta ayağa kaldırır.

3) Konfigürasyonu yap

SearchEngineService/appsettings.Development.json içinde:

ConnectionStrings.Default değerini kendi MySQL bağlantınıza göre düzenleyin.

Redis.Configuration değerini Redis örneğinize göre güncelleyin.

4) Veritabanını hazırla

MySQL’de migration’ları uygulayın (örn.):

dotnet ef database update --project SearchEngineService --startup-project SearchEngineService


Migration’lar içerik ve skor tablolarını, benzersiz indeksleri ve FULLTEXT indeksini oluşturur.

5) Derle ve çalıştır
dotnet restore
dotnet build
dotnet run --project SearchEngineService


Çalışan servis, rate limiter ve Serilog loglama ile birlikte Swagger UI’ı (Development ortamında) açar; statik dosyalar kök URL’den dashboard’u sunar.

🧩 Mimari ve İş Akışı
Veri modeli ve depolama

Content ve ContentScore entiteleri, sağlayıcı + external id üzerinden benzersiz olacak şekilde tasarlanmıştır; skor bire-bir ilişki ile saklanır. Migration’lar MySQL şeması ve indeksleri tanımlar.

Sağlayıcı adaptörleri

ProviderJson: mocks/provider1.json dosyasını okuyup başlık eşleşmesine göre filtreler, eksik açıklamaları tag’lerle tamamlar.

ProviderXml: mocks/provider2.xml dosyasındaki <item> kayıtlarını tür, kategori ve yayın tarihine göre normalize eder.
Her adaptör NormalizedContent kaydı döndürür.

Ingest ve skor üretimi

IngestService, aynı sağlayıcı ve external id için kayıt varsa günceller, yoksa ekler; ardından ScoringService ile popülerlik skorunu hesaplayıp kaydeder.

Arama stratejisi

MySQLContentSearch: FULLTEXT MATCH ... AGAINST ile relevans skoru üretir, eşitliklerde popülerlik ve yayın tarihini tie-breaker olarak kullanır.

FallbackContentSearch: Test ve in-memory senaryolar için Contains tabanlı relevans + popülerlik tie-breaker uygular.
Çalışma zamanı, ortam durumuna göre uygun stratejiyi dependency injection ile seçer.

API hattı

GET /api/Search uç noktası:

İstek parametrelerini doğrular (query, type, sort, size).

60 sn TTL’li Redis cache’e bakar.

Sağlayıcılardan veriyi toplar ve ingest eder.

Seçilen arama stratejisiyle filtre/paginasyon uygular.

Yanıtı döndürüp cache’e yazar.

Altyapı kesitleri

Rate limiting: IP başına dakikada 60 istek, 429 durumunda Retry-After başlığı ile ProblemDetails döner.

Polly retry: HTTP sağlayıcı çağrılarına transient hata ve 429 durumlarında bekle-yeniden dene politikası uygular.

Health check: /health uç noktası MySQL ve Redis’i doğrular; rate limit dışı bırakılmıştır.

Serilog & global exception handler: Yapılandırılmış loglama ve 500 hatalarında RFC 7807 uyumlu ProblemDetails üretimi.

🧮 Skorlama Modeli

Formül:

FinalPopularityScore = (BaseScore × TypeWeight) + RecencyScore + EngagementScore

Bileşen	Video İçerik	Metin İçerik
BaseScore	views/1000 + likes/100	reading_time + reactions/50
TypeWeight	1.5	1.0
RecencyScore	0-5 (yayın tarihine göre)	0-5 (aynı kural)
EngagementScore	(likes/views)×10	(reactions/reading_time)×5

Skorlar yuvarlanarak ContentScore tablosunda saklanır.

📘 API Referansı
GET /api/Search

Parametreler

Parametre	Tip	Varsayılan	Açıklama
query	string	–	Zorunlu arama terimi.
type	all | video | text	all	
sort	popularity | relevance	popularity	
page	int	1	1’den başlar.
size	int	20	1-50 arası sayfa boyutu.

Yanıt gövdesi meta bilgisi, skor kırılımı ve provider detayları içerir.

Hata Kodları

400: Parametre validasyon hataları.

429: Rate limit aşıldı (Retry-After 60 sn).

500: Global exception handler ProblemDetails döner.

🖥️ Dashboard

wwwroot/index.html, sorgu alanı, tür/sıralama seçimleri ve sayfalama kontrolleriyle API’ye istek atan hafif bir dashboard sunar.

wwwroot/js/app.js rate limit (429), validasyon ve genel hata durumlarını kullanıcıya açıklayıp sonuç kartlarını skor kırılımlarıyla oluşturur.

✅ Testler

Çözümde xUnit tabanlı entegrasyon ve servis testleri bulunur; tümünü çalıştırmak için:

dotnet test SearchEngineSolution.sln


IngestServiceTests: Upsert ve skor hesaplamasının tek kayıt üzerinde idempotent çalıştığını doğrular.

SearchControllerTests: Boş sorgu için 400, geçerli sorgu için 200 ve sonuç gövdesini kontrol eder.

CustomWebAppFactory: Test ortamında EF InMemory, bellek içi cache ve fake provider kullanarak gerçek bağımlılıkları izole eder.

🧰 Teknoloji Seçimleri

ASP.NET Core 8, controller tabanlı API ve Serilog entegrasyonu.

EF Core + Pomelo MySQL sağlayıcısı, FULLTEXT desteği ve InMemory test veritabanı.

StackExchange Redis cache & distributed cache API’si.

Polly tabanlı HTTP retry politikaları.

Swagger UI (Development) ve statik dosya pipeline’ı.
