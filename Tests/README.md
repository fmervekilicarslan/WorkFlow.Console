# İzole PostgreSQL testleri

Proje kökünde önce Release derlemesini ve bağlantısız kontrolleri çalıştırın:

```powershell
dotnet build Tests/WorkFlow.Stage1Checks/WorkFlow.Stage1Checks.csproj -c Release --no-restore
dotnet Tests/WorkFlow.Stage1Checks/bin/Release/net10.0/WorkFlow.Stage1Checks.dll
```

Parametresiz çalıştırma: 23 mevcut kontrol ve 14 bağlantı güvenliği kontrolü; veritabanına bağlanmaz.

## Bu yerel ortamda yazma testleri

Windows ve Docker Desktop için, mevcut `workflow-postgres` konteynerinin yalnızca `127.0.0.1:5433` portundan erişilebilir olması gerekir. Betik mevcut `workflow_user` yönetici rolünü yalnızca yeni test kaynaklarını oluşturmak ve salt okunur canlı denetimler için kullanır. Mevcut hesaplara yetki eklemez, sunucu ayarlarını değiştirmez.

```powershell
.\Tests\Run-IsolatedChecks.ps1
```

İlk çalıştırma benzersiz `workflow_test_<32 hex>` veritabanı ve aynı adlı LOGIN rolünü oluşturur. Rolün superuser, CREATEDB, CREATEROLE, REPLICATION, BYPASSRLS ve INHERIT yetkileri veya başka rol üyeliği yoktur. Rol yalnızca kendi test veritabanının sahibidir. PUBLIC erişimi yalnızca yeni test veritabanında kaldırılır. Canlı tablolara/sequence'e erişim veya canlı şema/veritabanı oluşturma yetkisi tespit edilirse betik mevcut izinleri değiştirmeden durur.

Bağlantı parolası rastgele üretilir. Bağlantı yalnızca test alt süreçlerine `WORKFLOW_TEST_CONNECTION` ortam değişkeniyle aktarılır. Yeniden çalıştırmak için `.local-checkpoints/isolated-tests.clixml` içinde Windows DPAPI ile şifreli tutulur; bu dosya Git dışındadır ve aynı Windows kullanıcısı/makinesi tarafından açılabilir. Bu dosyayı, bağlantıyı veya parolayı paylaşmayın. Normal uygulama bağlantısı ve User Secrets değiştirilmez.

Her yazma testi modu bağlantının açık host, port, veritabanı, kullanıcı ve parola içerdiğini; hedefin benzersiz yerel test adı olduğunu; bağlı veritabanı/kullanıcı kimliğini ve kısıtlı rol sahipliğini doğrular. `workflow_db`, eksik bağlantı ve belirsiz hedefler reddedilir. Testler User Secrets'a geri dönmez. Migration uygulaması yalnızca bu denetimden sonra yapılır.

Betik sırasıyla `--prepare-test-database`, `--database` ve `--stage2` modlarını çalıştırır. Son iki grup 16 PostgreSQL ve 152 servis/HTTP kontrolü içerir. HTTP kapsamı İngilizce dil isteğinde boş/boşluklu başlığın tam Türkçe alan ve özet mesajını, erişilebilirlik bağlantılarını ve 120/121 karakter sınırını doğrular. Create ve Edit için ayrıca sayısal/metinsel geçersiz enumlar, ayrıştırılamayan/geçersiz gün içeren tarihler ve yıl sınırını aşan tarihler kontrol edilir. Başarısız gönderimde tam Türkçe alan/özet mesajı, girilen başlık/açıklama/tarih ve veritabanındaki bütün kayıtların korunması doğrulanır. Toplam 37 bağlantısız kontrolle birlikte 205 kontrol vardır. Bu kontroller yeni test kayıtlarını temizler; test veritabanını ve rolünü silmez. Herhangi bir kaynak silme işlemi için ayrıca kullanıcı onayı gerekir.

Canlı `workflow_db` görevleri, migration geçmişi, sequence durumu ve public şema/nesne sahipliği/izinleri READ ONLY transaction içinde özetlenip önce/sonra karşılaştırılır. Eşzamanlı kullanıcı işlemi de fark yaratabilir; fark varsa incelemeden devam etmeyin. Bu karşılaştırma yedek veya tam sunucu denetimi değildir.

Bu testler tarayıcı etkileşimi çalıştırmaz. Mobil menü, modal klavye davranışı, ekran genişlikleri ve yerleşik tarayıcı doğrulama mesajları için ayrıca tarayıcı kontrolleri gerekir.

## Kanban kontrolleri

Kanban geliştirmesi için mevcut test veritabanını ve rolünü kullanın; aşağıdaki seçenek yeni kaynak oluşturmaz ve migration hazırlama modunu çağırmaz. Şifreli yerel test bağlantısı yoksa durur.

```powershell
dotnet build Tests/WorkFlow.Stage1Checks/WorkFlow.Stage1Checks.csproj -c Release --no-restore
dotnet Tests/WorkFlow.Stage1Checks/bin/Release/net10.0/WorkFlow.Stage1Checks.dll
$env:WORKFLOW_BROWSER_CHECKS = '1'
.\Tests\Run-IsolatedChecks.ps1 -ExistingOnly
```

Son doğrulama: **288/288 kontrol başarılı**. Dağılım: 23 mevcut bağlantısız + 14 izolasyon + 16 PostgreSQL + 152 mevcut servis/HTTP + 52 Kanban + 31 Chrome tarayıcı kontrolü. Release derlemesi sıfır hata ve uyarıyla geçti. Tarayıcı seçeneği kapalıyken toplam 257 kontroldür. Chrome tarayıcı kontrolleri Windows'ta yerel Google Chrome gerektirir; Stage2'nin izole bağlantılı kendi sunucusunu kullanır ve yalnız o koşunun oluşturduğu test görevinin durumunu değiştirir.

Yeni HTTP kapsamı üç durumun kalıcılığını yeni PostgreSQL bağlantısı ve yenilenen HTML ile; liste/pano kimlik kümelerini filtreli ve sayfalı istek üzerinde; eksik/geçersiz durum ve görev kimliğini; CSRF ve bulunamayan görevi doğrular. Controller'ın concurrency, kayıt ve bağlantı hatası eşlemeleri sahte servisle test edilir; bu kontroller gerçek PostgreSQL arızası oluşturmaz.

Chrome CDP kontrolleri 1440/390/320 px genişliklerde iki temayı ve yatay taşmayı; durum seçicisini, taşıma sonrası odağı, sütun sayaçlarını ve liste senkronunu; Liste/Pano ve tema kalıcılığını; yenileme, boş pano ve filtre davranışını kapsar. Sürükle-bırak testleri sentetik HTML drag/drop olayları kullanır. Sunucu 503 ve ağ hatasında geri dönüş, tarayıcıda mock fetch yanıtlarıyla doğrulanır. Fiziksel sürükleme, gerçek klavye tuşları, dokunmatik cihaz etkileşimi ve ekran görüntüsü üzerinden görsel onay bu koşunun kanıtı değildir; mobil kontroller dar masaüstü viewport ile yapılır.

Koşu sonunda canlı görevler, migration geçmişi, sequence, sahiplik ve izin fingerprint'i değişmedi. Yalnız koşunun izole geçici kayıtları temizlendi; mevcut test veritabanı/rol ve normal uygulama bağlantısı korundu. QA sunucusu ve sahip olunan Chrome süreci kapatılır; geçici tarayıcı profili kaynak silmemek için tutulur.
