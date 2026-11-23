# WorkFlow sunum demosu

Rider'da **WorkFlow.Mvc** klasöründeki PowerShell terminalinde:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Demo.ps1
```

Docker Desktop ve mevcut `workflow-postgres` konteyneri açık olmalı. Başlatıcı, mevcut Windows hesabına ait DPAPI ile korunan demo bağlantısını ve güvenilir localhost sertifikasını doğrular. Release derlenir ve HTTPS giriş sayfası hazır olduğunda terminal **https://localhost:5001** bağlantısını gösterir. Demo aynı terminalde Ctrl+C ile kapanır. 5001 başka bir uygulamaya aitse sona `-Port 5002` ekleyebilirsiniz. Başlatıcı demo veritabanını hazırlamaz veya migration uygulamaz.

Sunum hesabı yerel olarak hazırlandıysa e-posta adresini öğrenmek ve parolayı ekrana yazdırmadan panoya kopyalamak için ikinci bir Rider PowerShell terminalinde:

```powershell
$demoAccount = Import-Clixml -LiteralPath .local-checkpoints/presentation-account.clixml
$demoAccount.UserName
Set-Clipboard -Value $demoAccount.GetNetworkCredential().Password
```

Parolayı giriş formuna yapıştırın. Bu şifreli dosyayı aynı Windows hesabı açabilir.

## Beş dakikalık akış

1. Mevcut demo hesabınızla giriş yapın. Avatar menüsünden **Profilim** ve **Hesap Ayarları** ekranlarını gösterin.
2. **Pano** görünümünde **Devam Ediyor** sütununun ekleme bağlantısını kullanın. Örneğin “Sprint demo hazırlığı” başlıklı bir görev açıp öncelik ve teslim tarihi verin. Kaydedince panoya dönün.
3. Kartı başka bir sütuna taşıyın. Karttaki `WF-…` kodunu aratarak aynı görevi Liste ve Pano'da bulun.
4. **Takvim**'de teslim tarihine gidin, görev etiketinden detay sayfasına geçin ve düzenleme akışını gösterin.
5. Tema değiştirip avatar menüsünden güvenli çıkışı gösterin.

Bu demo kişisel görev alanı kullanır. Ekip çalışma alanları, görev atama ve yorumlar ayrı geliştirme kapsamıdır.
