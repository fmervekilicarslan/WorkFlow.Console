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

Betik sırasıyla `--prepare-test-database`, `--database` ve `--stage2` modlarını çalıştırır. Son iki grup 16 PostgreSQL ve 72 servis/HTTP kontrolü içerir. HTTP kapsamı İngilizce dil isteğinde boş/boşluklu başlığın tam Türkçe alan ve özet mesajını, erişilebilirlik bağlantılarını, 120/121 karakter sınırını, geçersiz enumları, form değerlerinin ve mevcut kayıtların korunmasını da doğrular. Toplam 37 bağlantısız kontrolle birlikte 125 kontrol vardır. Bu kontroller yeni test kayıtlarını temizler; test veritabanını ve rolünü silmez. Herhangi bir kaynak silme işlemi için ayrıca kullanıcı onayı gerekir.

Canlı `workflow_db` görevleri, migration geçmişi, sequence durumu ve public şema/nesne sahipliği/izinleri READ ONLY transaction içinde özetlenip önce/sonra karşılaştırılır. Eşzamanlı kullanıcı işlemi de fark yaratabilir; fark varsa incelemeden devam etmeyin. Bu karşılaştırma yedek veya tam sunucu denetimi değildir.

Bu testler tarayıcı etkileşimi çalıştırmaz. Mobil menü, modal klavye davranışı, ekran genişlikleri ve yerleşik tarayıcı doğrulama mesajları için ayrıca tarayıcı kontrolleri gerekir.
