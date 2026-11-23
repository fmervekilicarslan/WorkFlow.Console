# WorkFlow.Mvc

WorkFlow, kişisel işleri planlamak ve takip etmek için geliştirilmiş bir ASP.NET Core MVC uygulamasıdır. Aynı görevleri Liste, Kanban ve Takvim üzerinden yönetebilirsiniz.

## Mevcut özellikler

| Alan | Neler yapabilirsiniz? |
| --- | --- |
| Başlangıç | Herkese açık ana sayfadan özellikleri inceleyebilir, kayıt olabilir veya giriş yapabilirsiniz. |
| Görev Listesi | Görev oluşturabilir, açabilir, düzenleyebilir ve silebilirsiniz. Kalıcı WF kodu, başlık veya açıklamayla arama yapabilir; durum, öncelik, sıralama ve sayfalama kullanabilirsiniz. |
| Kanban | **Yapılacak**, **Devam ediyor** ve **Tamamlandı** sütunlarından görev açabilir ve kart taşıyabilirsiniz. Sürükle-bırak ve durum seçicisi PostgreSQL'e kaydeder; başarısız istekte kart eski sütununa döner. |
| Takvim | Teslim tarihlerini pazartesi başlayan **Gün**, **Hafta** ve **Ay** görünümlerinde inceleyebilirsiniz. Yoğun günlerde ek görevler açılabilir. Tarihsiz görevler ayrı listelenir. |
| Profil ve ayarlar | Görünen ad ve Hakkımda alanını düzenleyebilir, profil fotoğrafı yükleyebilir, değiştirebilir veya kaldırabilirsiniz. Profilde kayıtlıysa üyelik tarihi görünür. Hesap Ayarları'nda parola ve tema değiştirilebilir. |

Arayüz Türkçedir, açık ve koyu temayı destekler ve mobil ekranlara uyarlanır. Takvim İstanbul yerel günlerini kullanır; teslim tarihleri PostgreSQL'de UTC olarak saklanır.

## Teknolojiler ve mimari

- **C# ve .NET 10** · ASP.NET Core MVC, Razor ve ASP.NET Core Identity
- **PostgreSQL 17** · Entity Framework Core ve Npgsql
- **Arayüz** · HTML, CSS, JavaScript ve WorkFlow'un Pen.dev kaynaklı bileşenleri
- **Profil fotoğrafı** · SkiaSharp ile görüntü doğrulama ve yeniden kodlama
- **Geliştirme** · JetBrains Rider, Docker, Git ve izole HTTPS/Chrome regresyonları

| Katman | Sorumluluk |
| --- | --- |
| Controllers | HTTP rotaları, yetki kontrolü ve ekran akışları |
| Views ve ViewModels | Razor arayüzü, form alanları, doğrulama ve kullanıcı geri bildirimi |
| Services | Kullanıcıya özel görev sorguları, işlem kuralları ve profil fotoğrafı işleme |
| Models, Data ve Migrations | Identity modeli, görev eşlemeleri, PostgreSQL erişimi ve sürümlenmiş şema |

### Identity ve görev erişimi

ASP.NET Core Identity parolaları hash olarak saklar ve HTTPS üzerinden güvenli oturum çerezi kullanır. Girişte deneme sınırlaması uygulanır. Durum değiştirme, profil güncelleme, fotoğraf işlemleri ve çıkış gibi yazma istekleri CSRF korumalıdır.

Görev erişimi sunucuda oturum sahibine göre belirlenir. Liste, Kanban, Takvim ve doğrudan görev bağlantıları aynı sahiplik sınırını kullanır. Eski görevlere otomatik sahip atanmaz. Fotoğraf erişimi de hesap sahibine özeldir. JPEG veya PNG yüklemeleri 2 MiB ve her boyutta 2048 piksel ile sınırlandırılır. Doğrulanan görüntü yeniden kodlanıp PostgreSQL'de saklanır.

## Yerel kurulum

1. Bu depoyu klonlayın. Rider'da depo içindeki **WorkFlow.Mvc.csproj** dosyasını açın ve .NET 10 SDK'yı seçin.
2. PostgreSQL 17'yi yerel olarak çalıştırın. Docker ile yeni kurulumda, Git dışındaki **.local-checkpoints/postgres.env** dosyasına **POSTGRES_DB**, **POSTGRES_USER** ve **POSTGRES_PASSWORD** değerlerini ekleyin. Geliştirme örneğinde veritabanı **workflow_db**, rol **workflow_user** ve yerel port **5433**. Yeni konteyneri bir kez oluşturun:

   ~~~powershell
   docker run -d --name workflow-postgres --env-file .local-checkpoints/postgres.env --publish 127.0.0.1:5433:5432 --mount source=workflow-pgdata,target=/var/lib/postgresql/data postgres:17
   ~~~

   Mevcut **workflow-postgres** konteyneriniz ve veritabanınız varsa bunları kullanın.
3. **ConnectionStrings:WorkFlowDb** değerini projenin **User Secrets** yapılandırmasına ekleyin. PostgreSQL host, port, veritabanı, rol ve kendi parolanızı kullanın. Diğer çalışma ortamlarında bağlantıyı **ConnectionStrings__WorkFlowDb** üzerinden güvenli biçimde sağlayın.
4. Geliştirme sertifikasını hazırlayın:

   ~~~powershell
   dotnet dev-certs https --trust
   ~~~

5. Şema için aşağıdaki migration bölümünü uygulayın. Rider'da **WorkFlow.Mvc** için **.NET Project** Run Configuration seçin ve **ASPNETCORE_ENVIRONMENT=Development** ayarını kullanın. **Run** sonrasında terminalde bildirilen adresi açın. Varsayılan adres **https://localhost:5003**.

### EF Core migration kullanımı

Uygulama açılışta şema değişikliği yapmaz. Proje ile uyumlu **dotnet-ef 10.0.14** aracını kullanın. Hedef veritabanını doğrulayın ve SQL'i önce gözden geçirin:

~~~powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef migrations list --project WorkFlow.Mvc.csproj
dotnet ef migrations script --idempotent --project WorkFlow.Mvc.csproj
~~~

Yeni ve boş bir yerel veritabanını hazırlamak için **dotnet ef database update --project WorkFlow.Mvc.csproj** komutunu kullanabilirsiniz. Mevcut veri içeren bir veritabanında önce tam yedek alın, geri yüklemeyi ayrı bir kopyada doğrulayın ve migration'ı o kopyada deneyin. Identity şemasını eklemek ve eski görevleri hesaplara bağlamak ayrı işlemlerdir. [Geçiş planı](Docs/AuthenticationRollout.md) ayrıntıları açıklar.

## Test ve sunum

~~~powershell
dotnet build -c Release
dotnet build Tests/WorkFlow.Stage1Checks/WorkFlow.Stage1Checks.csproj -c Release
dotnet Tests/WorkFlow.Stage1Checks/bin/Release/net10.0/WorkFlow.Stage1Checks.dll
~~~

Son komut veritabanı gerektirmeyen kontrolleri çalıştırır. PostgreSQL, HTTPS ve Chrome regresyonları için [izole test yönergelerini](Tests/README.md) kullanın. Yazma testleri ayrı bir test veritabanı ve kısıtlı rol gerektirir. Canlı veya normal geliştirme veritabanı yazma testi hedefi olarak kullanılmaz.

Geliştirme verilerinden ayrı bir sunum hazırlamak için [izole HTTPS demo rehberine](Docs/PresentationDemo.md) bakın. Demo veritabanının şeması çalıştırılan sürümle eşleşmelidir. Parolaları, bağlantı dizelerini, sertifikaları, yedekleri ve yerel kontrol dosyalarını Git dışında tutun.

## Sonraki geliştirme başlıkları

- Birden çok proje, ekip üyeliği ve görev atama
- Görev yorumları ve değişiklik geçmişi
- E-posta doğrulama, parola sıfırlama ve çok faktörlü kimlik doğrulama

Bu başlıklar mevcut kişisel görev alanında henüz kullanılabilir değildir.
