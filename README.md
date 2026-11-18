# WorkFlow – Task Management Application

WorkFlow, görevlerin oluşturulmasını, listelenmesini ve yönetilmesini sağlayan, **C# ve ASP.NET Core MVC** kullanılarak geliştirilmiş bir görev yönetimi uygulamasıdır.

Bu proje, nesne yönelimli programlama prensiplerini ve ASP.NET Core MVC mimarisini uygulamalı olarak öğrenmek amacıyla geliştirilmektedir.

## Kullanılan Teknolojiler

- **C#**
- **.NET 10**
- **ASP.NET Core MVC**
- **Razor Views**
- **Entity Framework Core / Npgsql / PostgreSQL**
- **HTML ve CSS**
- **Git ve GitHub**

## Projenin Özellikleri

- Yeni görev oluşturma
- Mevcut görevleri listeleme
- Kalıcı Liste / Pano görünüm seçimi ve üç sütunlu Kanban panosu
- Kanban kartlarında sürükle-bırak veya durum seçimiyle güvenli, kalıcı durum güncelleme
- Görev silme
- Görev durumunu güncelleme
- Görevlere öncelik ve tarih bilgisi atama
- Form doğrulama ve hata yönetimi
- Görev detayını görüntüleme ve görev bilgilerini düzenleme
- Başlık/açıklama araması, durum ve öncelik filtreleri
- Sıralama ve sayfalama
- Kalıcı, benzersiz görev kodları (`WF-001` gibi)
- Liste, oluşturma, düzenleme, detay ve hata ekranlarında ortak responsive görünüm
- Sistem tercihinden başlayan, kullanıcı seçimini saklayan açık/koyu tema
- Pazartesi başlayan Gün / Hafta / Ay takvimi ve ayrı tarihsiz görev bölümü

Güneş/ay tema seçicisi masaüstünde üst çubuğun sağında, mobilde hamburger menüsünün yanındadır. Seçili tema mor vurguyla belirtilir; seçenekler klavyeyle kullanılabilir. Tarayıcı depolamasına erişilemiyorsa tema seçimi yalnızca açık sayfada geçerlidir. JavaScript kapalıyken mobil gezinme bağlantıları görünür kalır; form doğrulaması sunucuda çalışır.

Görevler sayfasındaki **Liste / Pano** bağlantılarıyla görünümü değiştirebilirsiniz. Pano, mevcut `Todo`, `InProgress`, `Completed` durumlarını **Yapılacak**, **Devam Ediyor**, **Tamamlandı** sütunlarında gösterir. Arama ve öncelik filtreleri iki görünümde de çalışır. Liste durum filtresi ve sayfalama kullanır; pano ise arama/öncelikle eşleşen tüm görevleri getirir. Listedeki durum, sayfa numarası ve sayfa boyutu panonun kapsamını daraltmaz. Pano görünümünde durum sekmeleri ve sayfalama kontrolleri gösterilmez; sütun sayaçları görüntülenen kartları sayar.

Görünüm URL'de `view=list` veya `view=board` ile belirlenir; açık URL seçimi önceliklidir. Tarayıcıda saklanan görünüm tercihi, görünüm belirtilmeden görev sayfasına dönüldüğünde kullanılır. Depolama kullanılamıyorsa URL ve görünüm bağlantıları çalışır. Her istekte yalnız aktif görünümün görevleri sorgulanır; pano tek bir izlenmeyen (`AsNoTracking`) görev sorgusu ve ortak özet sorgusunu kullanır. Liste mevcut sayfalı sorgusunu korur. Pano tüm eşleşmeleri yüklediğinden çok büyük görev kümelerinde kart çizim maliyeti ayrıca değerlendirilmelidir.

Kartı başka sütuna sürükleyebilir veya karttaki durum alanını kullanabilirsiniz. Güncelleme CSRF korumalı MVC endpoint'i üzerinden mevcut `TaskService` ile PostgreSQL'e kaydedilir. Başarısız istekte kart önceki durumuna döner ve Türkçe hata gösterilir. Pano etkileşimleri JavaScript gerektirir; mevcut liste formuyla durum güncelleme kullanılmaya devam edilebilir. Kanban için yeni migration gerekmez.

Liste, Pano ve Takvim araç çubukları ortak kontrol ölçüleri ve daha sıkı boşluklar kullanır. Kanban kartları görev kodunu, başlığı, önceliği ve teslim tarihini koruyan kompakt düzendedir; başlık görev detayını açar. Durum seçicisi daha dar, klavyeyle kullanılabilir ve dokunma için en az 44 piksel yüksekliğindedir. Masaüstünde her sütunun görev alanı bağımsız kaydırılırken başlığı görünür kalır; bu alanlara Tab ile odaklanabilirsiniz. Mobilde sütunlar alt alta ve sayfayla birlikte kayar. Açık/koyu tema, filtre adresleri ve görünüm tercihleri korunur.

UI Refinements V2 ile Kanban kartlarının kodu ve önceliği üstte, başlığı ortada, teslim tarihi ve küçük durum seçicisi ortak alt satırdadır. Alan daraldığında alt satır taşmadan sarılır. Native seçici ikincil görsel ağırlık taşır; dokunma alanı ve klavye odağı korunur. Sütun kaydırma çubukları açık/koyu temaya uyum sağlar.

## Görev Takvimi V1.2

Menüdeki **Takvim** bağlantısı `/Calendar` sayfasını açar. Varsayılan görünüm **Ay**dır; **Gün / Hafta / Ay** bağlantıları, **Önceki / Bugün / Sonraki** kontrolleriyle tarih aralığını değiştirebilirsiniz. Görünüm tercihi tarayıcıda saklanır; URL'deki açık `view=day|week|month` seçimi önceliklidir. Arama, durum ve öncelik filtreleri tarih ve görünüm bağlamını korur.

Haftalar pazartesi başlar. Ay ızgarasındaki yoğun günlerde ilk üç görev gösterilir; kalanlar klavyeyle de açılabilen **+N görev** genişletme alanındadır. Görev kartları mevcut detay sayfasına gider. Teslim tarihi olmayan görevler ayrı bölümde, sayfalı olarak gösterilir; takvim günlerine yerleştirilmez. Bugün ve tamamlanmamış gecikmiş/yaklaşan görevler vurgulanır; tamamlananlar gecikmiş sayılmaz.

Ay görünümünde Pzt–Paz hafta başlıkları yedi sütunla hizalanır; gün hücrelerinde yalnız gün numarası gösterilir. Dar ekranda hafta düzeni korunur ve yalnız takvim alanı yatay kaydırılır. Önceki/sonraki ayın günleri daha soluk, bugünün numarası mor yuvarlak alanla belirtilir.

V2 düzeninde ay dışı hücreler daha hafif arka plan, çerçeve ve tarih renkleriyle geri planda kalır; gün bağlantıları ve bu günlerdeki görevler kullanılmaya devam eder. Hücre boşlukları azaltılır; görevler ve erişilebilir açıklamaları soluklaştırılmaz.

Görevler küçük öncelik işareti, görev kodu ve tek satırlık başlıkla gösterilir; uzun başlıklar üç noktayla kısalır. Tam başlık, öncelik ve durum klavye odağında, erişilebilir adda ve tooltip'te bulunur. Dört öncelik hafif renk tonlarıyla ayrılır; durum yalnız renkle anlatılmaz. Yoğun günlerin **+N görev** alanı kaydırılabilir; boş günlerde yalnız gün numarası gösterilir. Gün numarası ilgili Gün görünümünü, görev etiketi Details sayfasını açar. Hafta ve Gün görünümleri ayrıntılı kartları korur.

Takvim tarih alanı **GG.AA.YYYY** biçimindedir (örneğin `18.11.2026`). Native tarih alanının tarayıcı diline bağlı gösterimi yerine açıklamalı metin alanı ve sunucuda kesin biçim doğrulaması kullanılır; JavaScript kapalıyken de çalışır. Mevcut `yyyy-MM-dd` URL bağlantıları desteklenmeye devam eder. Tarih, arama, durum ve öncelik alanları dar ekranlara uyarlanan kompakt araç çubuğundadır.

Tarih aralıkları İstanbul yerel günlerinden mevcut `TaskDates` dönüşümleriyle UTC'ye çevrilir. Veritabanı sorgusu başlangıç dahil, bitiş hariç aralık kullanır; kayıtların UTC teslim tarihleri doğru yerel güne gruplanır. Seçili tarihler mevcut form sınırlarıyla uyumlu olarak 1900–9998 yılları arasındadır. Takvim V1 yalnız okur; takvimden sürükleyerek teslim tarihi değiştirme içermez ve yeni migration gerektirmez.

## Proje Mimarisi

Uygulama, ASP.NET Core MVC mimarisine uygun olarak katmanlara ayrılmıştır.

| Katman | Açıklama |
|---|---|
| Models | Görev ve proje verilerini temsil eder. |
| Views | Kullanıcının etkileşim kurduğu web sayfalarını oluşturur. |
| Controllers | HTTP isteklerini karşılar ve uygulama akışını yönetir. |
| Services | Görev yönetimiyle ilgili iş kurallarını uygular. |
| Interfaces | Servislerin uyması gereken sözleşmeleri tanımlar. |
| ViewModels | Form verilerini ve doğrulama kurallarını yönetir. |
| Enums | Görev önceliği ve durum değerlerini tanımlar. |
| Exceptions | Uygulamaya özgü hata durumlarını temsil eder. |

## Kurulum ve Çalıştırma

Projeyi çalıştırabilmek için bilgisayarınızda **.NET 10 SDK** ve erişilebilir bir **PostgreSQL** veritabanı olmalıdır. Mevcut çalışma düzeninde MVC ve Console, `RiderProjects` altında kardeş klasörlerdir.

1. Repository'yi bilgisayarınıza klonlayın:

   ```bash
   git clone https://github.com/fmervekilicarslan/WorkFlow.Console.git
   ```

2. Proje klasörüne geçin:

   ```bash
   cd WorkFlow.Console
   ```

3. MVC uygulamasını başlatın:

   ```bash
   dotnet run --project WorkFlow.Mvc.csproj
   ```

4. Terminalde belirtilen yerel adresi tarayıcıda açın.

Çalıştırmadan önce `ConnectionStrings:WorkFlowDb` bağlantısı User Secrets veya `ConnectionStrings__WorkFlowDb` ortam değişkeni üzerinden tanımlanmalıdır. Parola ve bağlantı bilgilerini kaynak dosyalarına veya Git'e eklemeyin. `dotnet run` migration uygulamaz; mevcut veritabanında şema değişikliği yapmadan önce yedek ve açık kullanıcı onayı gerekir.

## Veri Saklama

Görevler, `WorkFlowDbContext` üzerinden PostgreSQL `Tasks` tablosunda saklanır; uygulama kapatıldığında korunur. `TaskService` görev işlemlerini ve sorguları yürütür. `Project` sınıfı şu anda yalnızca arayüzdeki proje adını taşır.

Görevlerin `Guid` kimliği korunur. Kullanıcıya gösterilen `WF-001` biçimindeki kodlar veritabanı sequence'i üzerinden üretilir; silinen numaralar yeniden kullanılmaz.

## Doğrulama

Release derlemesi için `dotnet build -c Release` kullanılır. Bağlantısız kontroller ve canlı veriden ayrı PostgreSQL/HTTP testleri için [test yönergelerini](Tests/README.md) izleyin. Test bağlantıları uygulamanın bağlantısından ayrıdır; canlı `workflow_db` yazma testlerinde kullanılmaz. Mobil görünüm, tema, gezinme ve klavye etkileşimleri ayrıca tarayıcıda kontrol edilir.

## Geliştirme Durumu

WorkFlow, yazılım geliştirme stajı kapsamında üzerinde çalışılmaya devam edilen bir öğrenme ve uygulama projesidir.

Projenin geliştirilmesi sırasında MVC mimarisi, Dependency Injection, servis katmanı, HTTP istekleri ve Git sürüm kontrolü gibi konular üzerinde çalışılmaktadır.
