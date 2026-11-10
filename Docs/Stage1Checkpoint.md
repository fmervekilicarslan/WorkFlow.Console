# Aşama 1 — Veritabanı onayı öncesi kontrol noktası

Bu belge uygulama öncesi kaydı korur. Son durum: migration kullanıcı onayıyla uygulandı, 16 gerçek PostgreSQL kontrolü geçti. Ayrıntılar: [uygulama raporu](Stage1MigrationVerification.md).

Tarih: 9 Kasım 2026. Durum: kaynak kodu hazır; mevcut veritabanına migration uygulanmadı. Aşama 2 başlamadı.

## Hazırlanan davranışlar

- Mevcut öncelik sayıları korunarak Critical = 3 eklendi.
- Guid anahtar korunuyor. CodeNumber, PostgreSQL TaskCodeSequence dizisinden üretiliyor; benzersiz indeks ile korunuyor. Görünen kod WF-001, WF-1000 biçiminde.
- Başlık/açıklama araması, durum ve öncelik filtreleri SQL tarafında birlikte çalışacak şekilde hazırlandı. Aramada yüzde ve alt çizgi metin olarak ele alınıyor.
- CreatedDate azalan, eşitlikte Id sıralaması kullanılıyor. Özetler filtrelerden bağımsız.
- DueDate isteğe bağlı DateOnly form girdisinden Europe/Istanbul üzerinden UTC'ye çevriliyor. Görev seçilen yerel gün boyunca gecikmiş sayılmıyor.
- Bugün dahil sonraki 7 gün yaklaşan aralığı. Tamamlanan görevlerde gecikmiş/yaklaşan göstergesi yok.

## Bu aşamada değişen dosyalar

- Enums/Priority.cs; Models/TaskItem.cs; yeni Models/TaskSummary.cs.
- Data/WorkFlowDbContext.cs; Interfaces/ITaskService.cs; Services/TaskService.cs; yeni Services/TaskDates.cs.
- Controllers/TasksController.cs (mevcut yorumlar korundu).
- ViewModels/TaskCreateViewModel.cs; TaskListViewModel.cs; yeni TaskFilterViewModel.cs.
- Views/Tasks/Create.cshtml; Index.cshtml; wwwroot/css/site.css.
- WorkFlow.Mvc.csproj: test kaynaklarını web derlemesinden ayırma ve mevcut EF Relational sürümünü 10.0.14'e sabitleme.
- Migrations/20261109075834_AddPersistentTaskCodes.cs, Designer.cs ve model snapshot.
- Tests/WorkFlow.Stage1Checks: yeni paket gerektirmeyen yürütülebilir kontrol projesi.
- .gitignore: yerel kaynak kontrol noktası ve veritabanı yedeklerini dışlama.

## Doğrulama

- `dotnet build -c Release --no-restore`: 0 hata, 0 uyarı.
- `dotnet run --project Tests/WorkFlow.Stage1Checks/WorkFlow.Stage1Checks.csproj -c Release --no-restore`: 23 kontrol geçti; veritabanına bağlanmaz.
- Kontroller: enum uyumluluğu, tarih dönüşümleri ve sınırlar, alan doğrulaması, sorgunun SQL'e çevrilmesi, arama kaçışları, sıralama, sequence/benzersiz indeks, migration SQL'i ve kod biçimi.
- InitialPostgres ve Designer dosyalarının SHA256 değerleri başlangıç kopyasıyla aynı.
- Controller yorumlarının başlangıç kopyasıyla aynı olduğu doğrulandı.
- Debug derlemesi çalışan uygulamanın exe kilidi nedeniyle tamamlanamadı; Release doğrulaması başarılı. Çalışan uygulama durdurulmadı.
- Git diff whitespace denetimi, başlangıçtan gelen Controller boş satır boşluklarını bildiriyor; bunlar yeni işlevlerden kaynaklanmıyor.

## Mevcut veritabanı ve yedek

Salt okunur inceleme: Tasks ve __EFMigrationsHistory; Tasks üzerinde 7 sütun, 1 görev ve 1 benzersiz Guid. Mevcut görevin Priority değeri 0, DueDate boş. Yalnız InitialPostgres uygulanmış.

Yedek: `.local-backups/workflow-before-stage1-20261109.dump` (Git dışında).

SHA256: `45C969A479673014EB1AC64C1D44DDF751E309093BD68F6EBBEB36F301224864`.

pg_restore --list arşivi okuyabiliyor; schema, tablo verileri, anahtarlar ve migration geçmişi var. Bu kontrol, gerçek geri yükleme denemesi değildir. Geri yükleme önce ayrı bir test veritabanında denenmeli; mevcut veritabanı üzerine otomatik restore yapılmamalı. Uygulama öncesi yeni yazmalar durdurulup güncel yedek ve kayıt durumu tekrar alınmalı.

## Onay bekleyen işlem

Yeni migration tek transaction içinde sequence ve nullable CodeNumber sütunu oluşturur; mevcut satırları CreatedDate/Id sırasıyla nextval kullanarak numaralar; NOT NULL, varsayılan nextval ve benzersiz indeks ekler. MAX + 1 kullanmaz. Mevcut Id, Priority ve diğer görev alanlarını değiştirmez. InitialPostgres değiştirilmez.

İncelenen SQL: `.local-checkpoints/stage1-migration.sql`. Kaynak kontrol noktası: `.local-checkpoints/stage1-before/`.

Yeni uygulama kodu CodeNumber sütununu beklediğinden, migration uygulanmadan mevcut veritabanıyla yeni sürüm başlatılmamalı.

Migration uygulaması, gerçek PostgreSQL CRUD/filtre/özet kontrolü, eşzamanlı ekleme ve silinen numaranın kullanılmaması testi henüz yapılmadı. Bunlar geçmeden Aşama 1 tamamlandı sayılmayacak ve sonraki aşamaya geçilmeyecek.

## Sonraki aşamalar

2: düzenleme, detay, pagination, sıralama ve doğrulamalar.
3: altı Pen ekranına dayalı responsive Razor, erişilebilir onay, karanlık mod. Pen dosyası düzenlemeleri ayrı onay gerektirir.
4: DueDate üzerinden günlük/haftalık/aylık takvim.
5: Identity hesapları, ekip/üyelik, atama ve sunucu erişim kontrolleri; mevcut görevlere sahiplik ataması ayrıca kararlaştırılmalı.
6: kullanıcı/görev ilişkili, yetki kontrollü yorumlar.
7: bütünleşik testler, mobil/klavye kontrolleri ve README.

Her yeni migration mevcut veritabanına uygulanmadan önce açık kullanıcı onayı, güncel kayıt kontrolü ve yedek gerekir. Commit/push yapılmayacak.
