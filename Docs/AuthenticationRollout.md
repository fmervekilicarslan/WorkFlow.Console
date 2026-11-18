# Kullanıcı hesabı ve görev sahipliği geçişi

Bu aşamanın migration dosyaları kaynak olarak hazırlanır. Uygulama başlangıcında gerçek `workflow_db` veritabanına migration uygulanmaz. Canlıya uygulama için ayrıca kullanıcı onayı gerekir.

Hazırlanan migration: `20261118130842_AddIdentityAndTaskOwnership`. Identity tabloları, nullable `Tasks.OwnerId`, sahiplik indeksi ve `Restrict` yabancı anahtarı ekler. Önceki iki migration dosyası korunur; yeni migration eski görevlere kullanıcı atamaz ve görev sequence'ini değiştirmez.

## Veri modeli

ASP.NET Core Identity hesap, parola hash'i ve oturum altyapısını sağlar. `Project` bu aşamada yalnız ekran adıdır; proje veya ekip yetkisi oluşturulmaz. Görevlerin `OwnerId` alanı boş bırakılabilir. Mevcut görevlerin kimlikleri, kalıcı kodları, tarihleri ve diğer alanları korunur; migration rastgele kullanıcı atamaz.

Sahibi olmayan eski görevler normal kullanıcı sorgularına dahil edilmez. Yeni hesap açılması bu görevleri devralmaz. Yeni görevlerde sahip, sunucuda doğrulanan oturumdan alınır. Liste/özet/arama/sayfalama/Kanban/Takvim ve kimlikle erişilen görev işlemleri aynı kullanıcı sınırını kullanır.

## İzole doğrulama

Testler yalnız kimliği ve rolü doğrulanmış ayrı test veritabanında migration uygular. Eski şemadaki görevlerin alanları migration öncesi/sonrası karşılaştırılır. Sahipsiz kayıtların iki ayrı kullanıcıya görünmediği ve bir kullanıcının diğerinin görevini okuyup değiştiremediği doğrulanır. Canlı veritabanı yalnız salt okunur fingerprint karşılaştırması için kullanılır.

Bu geliştirmede 12 ayrı migration/veri koruma kontrolü başarılı oldu. Son tam regresyon koşusu 705/705 kontrolden geçti; Release sıfır hata ve uyarıyla derlendi. Canlı veritabanının görev/migration/sequence/sahiplik/izin fingerprint'i aynı kaldı. Bu sonuç canlıya migration uygulanması için onay yerine geçmez.

## Canlıya geçiş için ayrı kararlar

1. Bakım zamanı ve HTTPS yapılandırması belirlenmeli; doğrulanmış yedek ve geri yükleme planı hazırlanmalıdır.
2. Üretilen migration/SQL ve uygulanacak veritabanı kullanıcı tarafından incelenip açıkça onaylanmalıdır. Bu belge otomatik uygulama yetkisi değildir.
3. Migration sonrasında eski görevler sahipsiz kalır. Kime ait oldukları, kayıt kimlikleri ve hedef kullanıcı kimlikleriyle ayrıca belirlenmelidir. İlk kayıt olan kullanıcıya veya topluca varsayılan hesaba atama yapılmaz.
4. Sahiplik atama işlemi ayrıca onaylı, denetlenebilir bir veri işlemi olmalıdır; bu aşamada sahiplik aktarma ekranı veya otomatik script çalıştırılmaz.
5. Paylaşılan ortamlarda HTTPS sertifikası ve kalıcı, uygun izinli Data Protection anahtarları hazırlanmalıdır. Birden fazla uygulama örneğinde anahtar ve uygulama adı ortak yönetilmelidir.

Migration uygulanmadan yeni sürüm canlı veritabanına karşı çalıştırılmamalıdır. Yeni kolon ve Identity tablolarının eksik olması uygulama işlemlerini engeller; canlı veriye otomatik müdahale ile telafi edilmez.

## İzole ortamda manuel tarayıcı denemesi

Önce bu sürümün izole migration ve güvenlik testleri başarılı olmalıdır. Mevcut Windows kullanıcısının şifreli test bağlantısını ayrı bir PowerShell oturumunda aşağıdaki gibi kullanın; bağlantıyı veya parolayı ekrana yazdırmayın:

```powershell
$authPreviewState = Import-Clixml -LiteralPath .local-checkpoints/isolated-tests.clixml -ErrorAction Stop
if ($authPreviewState.Name -notmatch '^workflow_test_[a-f0-9]{32}$' -or
    $authPreviewState.Connection -isnot [Security.SecureString]) { throw 'Geçerli izole test yapılandırması gerekli.' }
$env:ConnectionStrings__WorkFlowDb = [Net.NetworkCredential]::new('', $authPreviewState.Connection).Password
dotnet run --urls https://localhost:5001
```

Yerel HTTPS geliştirme sertifikası hazır ve güvenilir olmalıdır. `/Account/Register` ile iki ayrı hesap açıp farklı tarayıcı profillerinde giriş yapın. Her hesabın yalnız kendi görevlerini görmesini; diğer hesaptaki görev kimliğinin Details/Edit/Delete/durum işlemlerinde erişim vermemesini; Kanban ve Takvim filtrelerini; POST çıkışından sonra korumalı sayfanın giriş istemesini kontrol edin. Ayırdığınız deneme kayıtları izole veritabanında kalır; canlı görevleri bu ekranda beklemeyin.

Deneme bitince PowerShell oturumunu kapatın. Canlıya geçiş kararı verilmeden normal User Secrets bağlantısıyla yeni sürümü başlatmayın.
