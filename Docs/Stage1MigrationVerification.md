# Aşama 1 — Migration uygulama ve PostgreSQL doğrulama raporu

Tarih: 9 Kasım 2026. Hedef: yalnızca workflow_db, mevcut workflow-postgres konteyneri, PostgreSQL 17.11.

Kullanıcının koşullu uygulama onayı kapsamında bütün ön kontroller geçtikten sonra 20261109075834_AddPersistentTaskCodes uygulandı. Aşama 2 başlamadı; ayrıca onay bekleniyor.

## Migration incelemesi

- Up: bigint TaskCodeSequence, önce nullable CodeNumber, mevcut satırlara CreatedDate/Id sırasıyla nextval, ardından NOT NULL, nextval varsayılanı ve IX_Tasks_CodeNumber benzersiz indeksi.
- Up mevcut görevleri silmez; Guid, başlık, açıklama, durum, öncelik ve tarihlere dokunmaz. MAX + 1 kullanılmaz.
- İleri SQL yeniden üretildi ve önceki kontrol noktasındaki SQL ile aynı olduğu doğrulandı. DROP TABLE, DELETE, TRUNCATE veya ilgisiz şema değişikliği yok.
- Down yeni indeksi, CodeNumber sütununu ve sequence'i kaldırır. Görev satırlarını korur fakat atanmış kodları kaybettirir. Down çalıştırılmadı ve kodların kalıcılığı nedeniyle olağan geri alma yöntemi olarak kullanılmamalıdır.
- Uygulama, EF tarafından üretilen SQL üzerinden yapıldı. Ek doğrulama sarmalayıcısı hedef veritabanını ve başlangıç migration geçmişini kontrol etti; tabloları transaction boyunca yazmalara kilitledi. Mevcut satırlar geçici tabloya alındı; commit öncesinde iki yönlü EXCEPT karşılaştırmasıyla eski yedi alanın aynı olduğu doğrulandı.
- Geçici kontrol tablosu commit sırasında kaldırıldı; ek kalıcı kontrol tablosu oluşturulmadı.

## Önce ve sonra

| Kontrol | Önce | Sonra / test temizliği sonrası |
|---|---|---|
| Görev sayısı | 1 | 1 |
| Benzersiz Guid | 1 | 1; aynı kimlik |
| Mevcut görevin önceliği | 0 | 0 |
| Mevcut görevin diğer alanları | Başlangıç referansı | Bütün alanlar aynı |
| CodeNumber | Sütun yok | 1, görünen kod WF-001 |
| Migration sayısı | 1 | 2 |
| Test kaydı | 0 | 0 |

CodeNumber bigint ve NOT NULL. Varsayılan nextval('"TaskCodeSequence"'::regclass). Sequence bigint, başlangıç 1, artış 1, NO CYCLE. IX_Tasks_CodeNumber benzersiz B-tree indeksi; Guid birincil anahtarı korunuyor.

Migration geçmişi:

- 20261104172505_InitialPostgres
- 20261109075834_AddPersistentTaskCodes

## Yedek

Yeni uygulama öncesi yedek: `.local-backups/workflow-pre-apply-20261109-111818.dump`.

Boyut: 3175 bayt. SHA256: `C086235373F2E06ADEF7D110720D377A1758740C163D2C33427D9E055781395B`.

Önceki yedek `.local-backups/workflow-before-stage1-20261109.dump` da korunuyor; önceki hash değeri değişmedi.

Yeni arşiv pg_restore --list ile okundu ve tüm içeriği pg_restore --file=/dev/null ile hatasız çözümlendi. Tablo şeması, görev verisi, anahtarlar ve migration geçmişi mevcut. Yedek Git tarafından dışlanıyor. Gerçek bir veritabanına geri yükleme denemesi yapılmadı; bu kontrol tam restore testi olarak değerlendirilmemeli. İhtiyaç halinde pg_restore ile ayrı hedefte geri yükleme doğrulanabilir; mevcut veritabanının üzerine otomatik restore yapılmadı.

## Test sonuçları

- Release build: 0 hata, 0 uyarı.
- Mevcut çevrimdışı kontroller: 23/23 başarılı.
- Yeni gerçek PostgreSQL kontrolleri: 16/16 başarılı.
- TaskService ile ekleme, okuma, durum güncelleme ve silme.
- UTC DueDate kalıcılığı; başlık/açıklama araması, literal yüzde/alt çizgi, durum ve öncelik filtrelerinin birlikte çalışması.
- Özetin filtrelerden bağımsız hesaplanması; CreatedDate azalan sıralama.
- Ayrı DbContext ve bağlantılarla bariyer üzerinden aynı anda başlatılan 8 ekleme: 8 farklı, kalıcı kod.
- Aynı CodeNumber ile ayrı Guid ekleme denemesi: beklenen unique_violation, IX_Tasks_CodeNumber tarafından reddedildi.
- En büyük kod silindikten sonraki kayıt daha büyük kod aldı; numara yeniden kullanılmadı.
- Temizlik yalnızca bu çalıştırmada oluşturulan Guid'ler ve benzersiz test başlığı işaretiyle sınırlandı. Mevcut kullanıcı görevi değiştirilmedi.
- Test sonunda tüm mevcut görev alanları ve kodları başlangıçla aynı; bağımsız SQL kontrolünde de kayıt sayısı 1 ve test kaydı sayısı 0.

Testler 2–11 numaralarını kullandı. Sequence son değeri 11; sıfırlanmadı. Başka ekleme olmazsa sonraki görev WF-012 olacaktır. Aradaki boşluklar benzersiz ve yeniden kullanılmayan kod politikasının sonucudur.

Bu testler gerçek EF Core/TaskService/PostgreSQL akışını kapsar; tarayıcı üzerinden uçtan uca UI testi değildir.

## Dosyalar ve korunmuş alanlar

Bu turda test projesine DatabaseChecks.cs eklendi; Program.cs'ye isteğe bağlı --database modu eklendi. Migration ve uygulama işlev kodları değiştirilmedi. Çalıştırma: `dotnet run --project Tests/WorkFlow.Stage1Checks/WorkFlow.Stage1Checks.csproj -c Release -- --database`.

Yerel SQL denetim çıktıları .local-checkpoints altında, Git dışında. Rapor ve PROJECT_MEMORY.md güncellendi.

InitialPostgres ve Designer dosyalarının hash değerleri başlangıç kopyasıyla aynı. Controller yorumları karşılaştırılarak korundu. Pen tasarım dosyası, Docker volume ve User Secrets değiştirilmedi. Commit/push yapılmadı.
