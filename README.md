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

Güneş/ay tema seçicisi masaüstünde üst çubuğun sağında, mobilde hamburger menüsünün yanındadır. Seçili tema mor vurguyla belirtilir; seçenekler klavyeyle kullanılabilir. Tarayıcı depolamasına erişilemiyorsa tema seçimi yalnızca açık sayfada geçerlidir. JavaScript kapalıyken mobil gezinme bağlantıları görünür kalır; form doğrulaması sunucuda çalışır.

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
