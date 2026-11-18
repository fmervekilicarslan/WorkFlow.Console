# İzole PostgreSQL testleri

Proje kökünde önce Release derlemesini ve bağlantısız kontrolleri çalıştırın:

```powershell
dotnet restore Tests/WorkFlow.Stage1Checks/WorkFlow.Stage1Checks.csproj
dotnet build Tests/WorkFlow.Stage1Checks/WorkFlow.Stage1Checks.csproj -c Release --no-restore
dotnet Tests/WorkFlow.Stage1Checks/bin/Release/net10.0/WorkFlow.Stage1Checks.dll
```

Identity bağımlılıkları eklendiğinde test projesinin transitive paket grafiği de yenilenmelidir. Bu nedenle yeni checkout veya paket değişikliğinden sonra `--no-restore` derlemesinden önce yukarıdaki restore adımını çalıştırın.

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

## UI Refinements V1 kontrolleri

Son tam koşu **526/526 başarılıdır**: 68 bağlantısız + 16 PostgreSQL + 442 Stage2. Mevcut 471 kontrolün tamamı korunur; 55 Chrome regresyonu eklenmiştir. Kanban/Liste Chrome grubu 85, Takvim Chrome grubu 85 kontroldür. Release uygulama ve test derlemesi sıfır hata ve uyarıyla geçti. Tarayıcı seçeneği kapalıyken toplam 356 kontrol değişmez. Çalıştırma komutları yukarıdaki Release, bağlantısız ve `WORKFLOW_BROWSER_CHECKS=1` ile `Run-IsolatedChecks.ps1 -ExistingOnly` komutlarıdır.

Yeni kapsam:

- İki temada 1440/768/390/320 px: başlık ve native durum seçicisinin kart içinde kalması, 44 px dokunma hedefi ve erişilebilir adı; araç çubuğu alanlarının okunabilir boyutları.
- Masaüstünde gerçek çok görevli sütunda sınırlı yükseklik ve bağımsız kaydırma; başlığın, komşu sütunların ve sayfanın yerinde kalması. Odaklanabilir, adlandırılmış kaydırma bölgesine CDP ArrowDown gönderilerek görünür odak ve gerçek kaydırma doğrulanır. Mobilde iç kaydırma yerine sayfa akışı denetlenir.
- 120 karakter başlığın tam metni ve Details bağlantısı; native select üzerinde CDP Home/ArrowDown/Tab ile gerçek tarayıcı klavye akışı; başlık bağlantısına CDP touchStart/touchEnd ile Details gezinmesi.
- Bekleyen istekte select'in devre dışı kalması, `aria-busy` ve drag koruması; bozuk JSON yanıtında eski sütun/sıra/select/odak, etkileşim ve sayaçların geri alınması. Mevcut başarılı kalıcılık, 503/ağ hatası, drag/drop, CSRF, filtre ve görünüm tercihi testleri korunur.
- Liste ve Takvimde iki temada 1440/390/320 px gerçek form gönderimi: alan yükseklikleri, yatay taşma ve Liste durum/öncelik/sayfa boyutu/sıralama ile Takvim tarih/arama değerlerinin korunması.

Dokunma testi Chrome CDP emülasyonudur; fiziksel telefon/tablet testi değildir. HTML drag/drop testi sentetik olaylarla devam eder. Fiziksel sürükleme ve ekran görüntüsüyle görsel onay bu koşunun kanıtı değildir.

Testler tek seri QA işi olarak mevcut izole veritabanında çalıştı; migration veya kaynak oluşturma yapılmadı. Takvim GET snapshot'ı, fixture temizliği sonrası tüm özgün görevler, kalıcı kodlar ve migration geçmişi aynı kaldı. Dış runner canlı `workflow_db` görev/migration/sequence/sahiplik/izin fingerprint'inin değişmediğini doğruladı. Test veritabanı/rol ve normal uygulama korundu; yalnız QA'nın sunucusu/Chrome süreci kapatıldı. Git dışı son loglar: `.local-checkpoints/ui-refinements-v1-final-{build,offline,isolated}.log`.

Takvim açıklama metni sadeleştirmesi sonrası tam koşu **529/529 başarılıdır**: 68 bağlantısız + 16 PostgreSQL + 445 Stage2; Kanban/Liste Chrome 85, Takvim Chrome 88. Önceki 526 kontrol korunur; tarih alanının erişilebilir biçim adı yeni `aria-label` sözleşmesine uyarlanır. Üç yeni gerçek DOM kontrolü ay açıklamasının/tarih yardım metninin kaldırıldığını, boş alanda `GG.AA.YYYY` placeholder'ının göründüğünü ve CDP `Input.insertText` ile tarih yazılınca gizlendiğini doğrular. Release sıfır hata/uyarı; tüm özgün görevler, migration geçmişi ve canlı fingerprint aynı kaldı. Mevcut fiziksel cihaz/görsel onay sınırları geçerlidir. Loglar Git dışında `.local-checkpoints/calendar-ui-copy-{build,offline,isolated}.log` dosyalarındadır.

## Takvim V1.2 kontrolleri

Tam koşu **559/559 başarılıdır**: 68 bağlantısız + 16 PostgreSQL + 475 Stage2; Takvim Chrome 118, Kanban/Liste Chrome 85. Önceki 529 kontrolün kapsamı korunarak 30 Chrome regresyonu eklendi. Release sıfır hata ve uyarıyla geçti. İzinli komut beklemesi nedeniyle bu koşuyu tek seri QA işi adına ana ajan yürüttü; QA ajanı logları ayrıca denetledi. İkinci DB yazma işi çalıştırılmadı.

İki temada 1440/768/390/320 px ölçümleri yedi Türkçe hafta başlığının sırasını, her tarihin pazartesi başlangıçlı doğru sütunuyla hizasını, yedi sütunun dar ekranda da korunmasını ve yalnız takvim bölgesinde yatay kaydırmayı doğrular. Sayfa taşması, hücre içi etiket okunabilirliği, sayısal gün bağlantıları ve tam tarih adları, ay dışı günlerin görsel ayrımı, dört farklı öncelik noktası ve simge kalabalığının kaldırılması denetlenir. Önceki ay kartı taşma kontrolü yeni iç kaydırma sözleşmesine uyarlanır; Gün/Hafta taşma kontrolleri korunur.

Chrome `Accessibility.getPartialAXTree`, görev bağlantısının gerçek erişilebilirlik ağacında tam başlık/durum/öncelik adıyla bulunduğunu doğrular. Bugünün sayısı iki temada da daire, `aria-current=date` ve en az 4,5:1 metin kontrastına sahiptir; CDP ArrowRight takvim bölgesini sayfayı taşımadan kaydırır ve gün bağlantısının görünür klavye odağı korunur. Bu kanıt fiziksel ekran okuyucuyla dinleme, fiziksel cihaz veya ekran görüntüsüyle görsel onay değildir.

İstanbul/UTC yarı açık aralık ve mikrosaniye sınırları, her üç görünümde doğru gün hücresi, artık yıl/ay/yıl sınırları, Details/tam başlık/+N, filtreler, tercihler ve tarih alanı regresyonları geçti. GET snapshot'ı, fixture temizliği sonrası tüm özgün görevler/kodlar ve migration geçmişi aynı kaldı; canlı `workflow_db` görev/migration/sequence/sahiplik/izin fingerprint'i değişmedi. Mevcut izole veritabanı/rol korundu; migration/provisioning yapılmadı. Git dışı loglar: `.local-checkpoints/calendar-v12-{build,offline,isolated}.log`.

## UI Refinements V2 kontrolleri

Son tam koşu **577/577 başarılıdır**: 68 bağlantısız + 16 PostgreSQL + 493 Stage2. Önceki 559 kontrol korunur; 18 Chrome regresyonu eklenmiştir. Kanban/Liste Chrome grubu 93, Takvim Chrome grubu 128 kontroldür. Release uygulama ve test derlemesi sıfır hata ve uyarıyla geçti.

Yeni kontroller iki temada 1440/768/390/320 px genişliklerde kompakt footer yerleşimini, native durum seçicisinin 44 px dokunma alanını, başlık alanının korunmasını, ay dışı tarihlerin en az 4,5:1 kontrastını, görevlerin soluklaştırılmamasını ve boş hücre yüksekliğini doğrular. Ay dışı güne tıklayarak doğru Gün görünümüne ve o gündeki görevden Details sayfasına erişim ayrıca kontrol edilir. Mevcut durum kalıcılığı, CSRF, drag/drop, hata geri alma, klavye, dokunma emülasyonu, sütun kaydırması, UTC sınırları, tarih alanı, +N ve erişilebilir ad kontrolleri geçti.

UI/UX ajanı ürün düzenlemelerini, QA ajanı testleri hazırladı. Ana ajan tek seri test koşusunu yürütüp logları denetledi; QA ajanı sonraki oturumda logları ayrıca inceleyerek 577 sayımını, sıfır başarısızlığı ve veri koruma kanıtlarını doğruladı. Fiziksel cihaz ve ekran görüntüsüyle görsel onay yapılmadı. Yalnız mevcut izole veritabanı ExistingOnly ile kullanıldı; migration/provisioning yapılmadı. Özgün görevler/kodlar, migration geçmişi ve canlı workflow_db salt okunur fingerprint'i değişmedi. Loglar Git dışında .local-checkpoints/ui-refinements-v2-{build,offline,isolated}.log dosyalarındadır.

## Identity ve kullanıcı sahipliği kontrolleri

Son tam koşu **705/705 başarılıdır**: 68 bağlantısız + 16 PostgreSQL + 621 Stage2. Önceki 577 kontrol, gerçek kullanıcı sahipliği ve kimliği doğrulanmış HTTPS oturumuyla uyarlanarak korundu; 102 Identity HTTP/güvenlik ve 26 Identity Chrome kontrolü eklendi. Kanban/Liste Chrome 93, Takvim Chrome 128 kontrol olarak devam eder. Release uygulama ve test derlemesi sıfır hata/uyarıyla geçti; son logda başarısız kontrol yoktur. Tarayıcı seçeneği kapalıyken toplam 458 kontroldür.

Ayrıca ilk açık izinli izole migration koşusunda **12/12 koruma kontrolü** geçti. Bu ayrı kanıt, 705 kontrolün parçası değildir; tek bir 717 kontrollük koşu yapılmış gibi raporlanmamalıdır. `20261118130842_AddIdentityAndTaskOwnership` migration'ından önce EF'nin yeni OwnerId sütununa erişmesi beklenmeden Npgsql ile eski alanların snapshot'ı alındı. Tüm eski görev alanları, Guid/kod/tarihler ve sequence migration boyunca aynı kaldı. Yalnız koşunun eklediği legacy fixture sahipsiz kaldı; iki gerçek kullanıcının Liste/Pano/Takvim ve doğrudan Details isteklerinden görünmedi. Fixture temizliğinden sonra bütün özgün görev alanları yeniden aynıydı.

Güncel hazırlık ve normal tekrar komutları:

```powershell
dotnet restore Tests/WorkFlow.Stage1Checks/WorkFlow.Stage1Checks.csproj
dotnet build Tests/WorkFlow.Stage1Checks/WorkFlow.Stage1Checks.csproj -c Release --no-restore
dotnet Tests/WorkFlow.Stage1Checks/bin/Release/net10.0/WorkFlow.Stage1Checks.dll
$env:WORKFLOW_BROWSER_CHECKS = '1'
.\Tests\Run-IsolatedChecks.ps1 -ExistingOnly
```

Yeni migration için kullanıcı açıkça yetki verdiyse, yalnız hazır izole test veritabanında ilk koşu şu komutla yapılabilir:

```powershell
.\Tests\Run-IsolatedChecks.ps1 -ExistingOnly -ApplyAuthorizedTestMigrations
```

Bu bayrak `-ExistingOnly` olmadan veya odaklı takvim moduyla reddedilir. Mevcut DPAPI test bağlantısı, bağlı test veritabanı/rol kimliği ve canlı nesnelere yetkisizlik denetlendikten sonra prepare modu çalışır. Normal `-ExistingOnly` migration uygulamaz. Canlı `workflow_db` için migration, veri yazma veya otomatik sahip atama yapılmadı; test veritabanı/rol oluşturulmadı veya silinmedi. Eski bölümlerdeki kaynak oluşturma komutları tarihsel altyapıyı anlatır; bu çalışmada yalnız hazır izole kaynaklar kullanıldı.

Kapsam:

- Anonim Liste/Pano/Takvim/CRUD ve çıkış istekleri aynı origin'deki giriş sayfasına yönlendirilir; mutlak HTTPS yönlendirmesinde de şema/host/port ve `/Account/Login` yolu doğrulanır. Eksik kullanıcı bağlamında servis hiçbir görevi, sahipsiz kayıtları veya özetini açmaz; oluşturmayı reddeder.
- Gerçek kayıt/giriş, yerel dönüş adresi, dış veya ters bölü işaretli dönüşlerin reddi, genel giriş/tekrar kayıt hataları, parola geri yazılmaması, Identity V3 PBKDF2 hash'inde en az 210000 iterasyon, beş başarısız denemede 15 dakikalık kilit ve aktif kilidin doğru parolayla aşılamaması denetlenir.
- Cookie'nin Secure/HttpOnly/SameSite=Lax bayrakları, kayıt oturum cookie'si, hatırlanan girişin 30 dakikalık sınırı, bozulmuş ticket'ın reddi, POST çıkış ve hesap/görev endpoint'lerinde CSRF koruması doğrulanır.
- İki gerçek kullanıcı ve sahipsiz görevle Liste/Pano/Takvim'in üç görünümü, özet, Details/Edit GET, Edit/Delete/UpdateStatus/UpdateBoardStatus POST için sahiplik izolasyonu doğrulanır. Başkasının/sahipsiz görevin kimliği 404 alır; reddedilen yazmalarda bütün görevlerin snapshot'ı aynı kalır. Sahiplik/kod alanı sahteciliği ve başka kullanıcıdan alınmış antiforgery token'ı görev aktarımı veya değişiklik yapamaz.
- Hesap Chrome kontrolleri iki temada 1440/768/390/320 px formları, autocomplete ve hata ilişkilerini, boş parolaları, hata odağını, güvenli dönüş değerini, kullanıcı adını ve mobil JavaScript kapalı gerçek POST çıkışı denetler. Önceki Kanban/Takvim klavye, CDP dokunma, rollback, yedi sütun, +N, AX ve UTC kontrolleri korunur.

QA sunucusu yalnız loopback HTTPS üzerinden çalışır. Geçici sertifika HttpClient'ta tam sertifika SHA-256 piniyle, ayrı Chrome profilinde yalnız o anahtarın SPKI piniyle kabul edilir; genel sertifika doğrulaması veya üretim cookie politikası gevşetilmez. Browser oturumu gerçek girişten alınır; uygulamada test auth bypass'ı yoktur. Bağlantı/parola/cookie çıktıya veya commit'e yazılmaz; şifreli geçici PFX ve tanı logları Git dışındadır. Fiziksel ekran okuyucu/cihaz, ekran görüntüsüyle görsel onay, gerçek SMTP/e-posta doğrulaması ve üretim migration/dağıtımı bu testlerin kanıtı değildir.

Ana ajan tek seri QA komut yürütücüsüydü; QA ajanı kaynak testlerini hazırladı ve son logları bağımsız denetledi. Tüm özgün görevler/kodlar ve Identity kullanıcıları temizleme sonrasında aynı kaldı; migration geçmişi son tam koşu boyunca değişmedi. Canlı görev/migration/sequence/sahiplik/izin fingerprint'i aynıydı. Yalnız QA fixture'ları temizlendi ve QA HTTPS/Chrome süreçleri kapatıldı. Son loglar Git dışında `.local-checkpoints/authentication-final-{build,offline,isolated}.log`; ayrı migration kanıtı `.local-checkpoints/authentication-isolated.log` dosyasındadır.
