# İzole PostgreSQL testleri

Proje kökünde önce Release derlemesini ve bağlantısız kontrolleri çalıştırın:

```powershell
dotnet build Tests/WorkFlow.Stage1Checks/WorkFlow.Stage1Checks.csproj -c Release --no-restore
dotnet Tests/WorkFlow.Stage1Checks/bin/Release/net10.0/WorkFlow.Stage1Checks.dll
```

Parametresiz çalıştırma: 23 mevcut kontrol, 14 bağlantı güvenliği ve 31 takvim tarih/koruma kontrolü; toplam 68 kontrol veritabanına bağlanmaz.

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

Varsayılan test modları tarayıcı etkileşimi çalıştırmaz. Aşağıdaki isteğe bağlı Chrome modu Kanban etkileşimlerini ve responsive düzeni ayrıca doğrular.

## Kanban kontrolleri

Kanban geliştirmesi için mevcut test veritabanını ve rolünü kullanın; aşağıdaki seçenek yeni kaynak oluşturmaz ve migration hazırlama modunu çağırmaz. Şifreli yerel test bağlantısı yoksa durur.

```powershell
dotnet build Tests/WorkFlow.Stage1Checks/WorkFlow.Stage1Checks.csproj -c Release --no-restore
dotnet Tests/WorkFlow.Stage1Checks/bin/Release/net10.0/WorkFlow.Stage1Checks.dll
$env:WORKFLOW_BROWSER_CHECKS = '1'
.\Tests\Run-IsolatedChecks.ps1 -ExistingOnly
```

V1.1 birleştirme öncesi son doğrulama: **318/318 kontrol başarılı**. Dağılım: 23 mevcut bağlantısız + 14 izolasyon + 16 PostgreSQL + 152 mevcut servis/HTTP + 52 uyarlanmış Kanban + 25 yeni V1.1/sorgu + 36 Chrome tarayıcı kontrolü. Önceki 288 kontrolün kapsamı yeni görünüm sözleşmesine uyarlanarak korundu. Release derlemesi sıfır hata ve uyarıyla geçti. Tarayıcı seçeneği kapalıyken toplam 282 kontroldür. Chrome tarayıcı kontrolleri Windows'ta yerel Google Chrome gerektirir; Stage2'nin izole bağlantılı kendi sunucusunu kullanır ve yalnız o koşunun oluşturduğu test görevinin durumunu değiştirir.

HTTP kapsamı üç durumun kalıcılığını yeni PostgreSQL bağlantısı ve ayrı Liste/Pano HTML yanıtları ile; eksik/geçersiz durum ve görev kimliğini; CSRF ve bulunamayan görevi doğrular. V1.1 testleri 14 görev ve üç durumla panonun ilk sayfanın ötesindeki tüm arama/öncelik eşleşmelerini içerdiğini, eski durum/sayfa/sayfa boyutu/sıralama parametrelerinin geçerli veya bozuk değerlerinin panoyu kısıtlamadığını doğrular. Liste filtre/sayfalama/doğrulama davranışı ve yalnız aktif görünümün HTML'e yazılması korunur. Gerçek PostgreSQL üzerinde `DbCommandInterceptor`, Controller Index çağrısında panonun iki ve listenin üç sorgu çalıştırdığını, görevlerin izlenmeden okunduğunu ve panonun görev SQL'inde sayfalama olmadığını denetler. Controller'ın concurrency, kayıt ve bağlantı hatası eşlemeleri sahte servisle test edilir; bu kontroller gerçek PostgreSQL arızası oluşturmaz.

Chrome CDP kontrolleri 1440/390/320 px genişliklerde iki temayı ve yatay taşmayı; durum seçicisini, taşıma sonrası odağı, sütun sayaçlarını ve ayrı Liste yanıtındaki kalıcılığı; URL ile Liste/Pano geçişlerini, localStorage tercihini ve açık `view=list` seçiminin önceliğini; yenileme, boş pano ve eski liste filtrelerinin panoya etkisizliğini kapsar. Seçili Yapılacak filtresi, ikinci sayfa ve beş görevlik sayfa boyutuyla gerçek Liste → Pano → Liste bağlantıları tıklanır; panoda tüm görevlerin gelmesi ve listeye dönüşte üç seçeneğin korunması ayrıca doğrulanır. Sürükle-bırak testleri sentetik HTML drag/drop olayları kullanır. Sunucu 503 ve ağ hatasında geri dönüş, tarayıcıda mock fetch yanıtlarıyla doğrulanır. Fiziksel sürükleme, gerçek klavye tuşları, dokunmatik cihaz etkileşimi ve ekran görüntüsü üzerinden görsel onay bu koşunun kanıtı değildir; mobil kontroller dar masaüstü viewport ile yapılır.

Koşu sonunda canlı görevler, migration geçmişi, sequence, sahiplik ve izin fingerprint'i değişmedi. Yalnız koşunun izole geçici kayıtları temizlendi; mevcut test veritabanı/rol ve normal uygulama bağlantısı korundu. QA sunucusu ve sahip olunan Chrome süreci kapatılır; geçici tarayıcı profili kaynak silmemek için tutulur.

## Takvim V1 kontrolleri

Takvim geliştirmesi sonrası aynı komutlarla **445/445 kontrol başarılı**: 68 bağlantısız + 16 PostgreSQL + 361 Stage2 kontrolü. Önceki 318 kontrolün tamamı korunur; 31 tarih/koruma, 34 takvim servis/SQL/HTTP ve 62 takvim Chrome kontrolü eklenmiştir. Release derlemesi sıfır hata ve uyarıyla geçti. `WORKFLOW_BROWSER_CHECKS` kapalıyken 347 kontrol çalışır. `-ExistingOnly` mevcut izole veritabanını/rolü kullanır; migration veya kaynak oluşturma moduna girmez.

`CalendarDateChecks` gün/hafta/ay aralıklarını, pazartesi başlangıcını, ay dolgu günlerini, ay/yıl geçişini, artık ve normal şubat aylarını, 1900/9998 gezinme sınırlarını, İstanbul gece yarısının UTC dönüşümünü ve geçersiz servis seçeneklerinin bağlantı kurulmadan reddini doğrular.

`CalendarChecks` kendi benzersiz işaretiyle 27 geçici görev oluşturur. Aralık başlangıcından bir mikrosaniye önce, başlangıçta, bitişten bir mikrosaniye önce ve bitişteki kayıtlar `[startUtc, endUtc)` sınırını gerçek PostgreSQL üzerinde kanıtlar. Gün aralığına yalnız doğru tarihli görevler girer; 12 tarihsiz görev ayrı 10+2 sayfalarda gösterilir. Arama/durum/öncelik, gecikmiş/yaklaşan/tamamlanmış göstergeleri, Details bağlantıları, Türkçe geçersiz istek mesajları, boş sonuçlar ve yoğun gündeki +7 görev ayrıntısı doğrulanır. `DbCommandInterceptor` takvim Controller çağrısının üç sorgu çalıştırdığını, görev sorgusundaki UTC parametreleri ve sunucuda yarı açık tarih filtresini, sayfalamasız tarihli sorguyu ve `AsNoTracking` davranışını ölçer.

`CalendarBrowserChecks.ps1` gerçek Chrome CDP ile gün/hafta/ay görünümlerini iki temada 320/390/768/1440 px genişliklerde denetler. Görünür kartlarda iç/yatay taşma ve başlık okunabilirliği; native +N ayrıntısının açılması; tarihsiz sayfalama; filtreli görünüm bağlantıları; görünüm/tema kalıcılığı; açık görünümün kayıtlı tercihe önceliği; Details ve Bugün gezinmesi; boş sonuçlar ve etkin Takvim menüsü kontrol edilir. Takvim tarayıcı akışı yalnız GET istekleri yapar. Fiziksel klavye/dokunmatik cihaz ve ekran görüntüsüne dayanan görsel onay bu koşuda yapılmadı; dar ekran kontrolleri masaüstü viewport emülasyonudur.

Takvim GET/Chrome kontrollerinden önce ve sonra tüm görevlerin snapshot'ı aynı kaldı. Yalnız kendi takvim fixture kayıtları temizlenince önceki bütün kayıtlar tekrar aynıydı; dış runner da gerçek `workflow_db` görev/migration/sequence/sahiplik/izin fingerprint'inin değişmediğini doğruladı. Test veritabanı/rol, gerçek kayıtlar ve normal uygulama bağlantısı korundu.

## Takvim V1.1 kontrolleri

Birleştirme öncesi tam koşu **471/471 başarılıdır**: 68 bağlantısız + 16 PostgreSQL + 387 Stage2. Mevcut 445 kontrolün kapsamı korunarak 9 HTTP ve 17 Chrome kontrolü eklendi. Stage2 içindeki takvim Chrome grubu artık 79, mevcut Kanban Chrome grubu 36 kontroldür. Release uygulama ve test derlemesi sıfır hata ve uyarıyla geçti. Tarayıcı kontrolleri kapalıyken toplam 356 kontroldür.

Yeni HTTP kontrolleri, İstanbul/UTC gece yarısı sınırındaki görevlerin gün/hafta/ay görünümlerinin tam doğru gün hücresinde bulunduğunu ve `29.02.2028` girişinin ISO tarih ile aynı günü seçtiğini doğrular. Geçersiz artık gün, ay günü, belirsiz tarih biçimi ve yıl sınırları için tam Türkçe hata mesajı ile girilen değerin korunması denetlenir. Doğrudan HTTP istekleri tarih ayrıştırmasının JavaScript'e bağımlı olmadığını da kanıtlar.

Chrome kontrolleri iki temada 1440/768/390/320 px genişlikleri, yoğun günde ilk üç kompakt etiketi ve hücre yüksekliğini, +7 açılmasını ve sınırlı kaydırma alanını, 120 karakter başlığın ellipsis ile kısalmasını ve tam erişilebilir adını, renk dışındaki durum/öncelik simgelerini, boş günlerde tekrarlanan metin bulunmamasını, ay etiketinden Details'a geçişi, biçimli tarih formunu ve gün başlığından tüm filtrelerle Gün görünümüne geçişi doğrular. CDP `Input.dispatchKeyEvent` ile Tab gönderilerek odak açıklamasının görünmesi ayrıca kontrol edilir. Fiziksel klavye/dokunmatik cihaz ve ekran görüntüsüyle görsel onay kapsam dışındadır.

Uzun başlık kontrolünün tek başına yeniden üretimi:

```powershell
dotnet build Tests/WorkFlow.Stage1Checks/WorkFlow.Stage1Checks.csproj -c Release --no-restore
.\Tests\Run-IsolatedChecks.ps1 -ExistingOnly -CalendarFocused
```

Odaklı mod yalnız mevcut izole veritabanında on geçici görev, kendi HTTP sunucusu ve gerçek Razor/Chrome sayfası kullanır; aynı uzun başlık assertion'ını çalıştırır. Geçici görevler temizlenir ve test veritabanı snapshot'ı karşılaştırılır; dış runner canlı fingerprint'i denetler. `-CalendarFocused`, `-ExistingOnly` olmadan reddedilir. Bu odaklı kontrol 471 toplamına ayrıca eklenmez.

İlk uzun başlık hatası, Windows PowerShell 5'in BOM içermeyen UTF-8 betiğindeki Türkçe sabiti yanlış okumasından kaynaklandı: gerçek DOM `ğ` kodu 287, yanlış okunan test beklentisi 196/376 idi. JavaScript Unicode kaçışları aynı sıkı assertion'ı kodlamadan bağımsız hâle getirdi; ürün HTML/CSS değişikliği gerekmedi. Odaklı kontrol düzeltme sonrası geçti; ardından tam koşu tekrar başarıyla tamamlandı.

Bu koşuda migration veya kaynak oluşturma yapılmadı. Canlı `workflow_db` fingerprint'i ve tüm özgün test görevleri değişmedi; test veritabanı/rol korundu. Tanı ve tam koşu logları Git dışındaki `.local-checkpoints/calendar-v11-focused-*.log` ve `calendar-v11-premerge-*.log` dosyalarındadır.
