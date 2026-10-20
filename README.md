# WorkFlow.Mvc

Bu ASP.NET Core MVC projesi, `../WorkFlow.Console/WorkFlow.Console_v0.3` içindeki görev yönetimi örneğinin web karşılığıdır. İki proje `RiderProjects` altında yan yana durur.

## Çalıştırma

.NET 10 SDK kurulu olmalıdır. `RiderProjects` klasöründe:

```powershell
dotnet run --project WorkFlow.Mvc/WorkFlow.Mvc.csproj
```

Terminalde yazan yerel adresi tarayıcıda aç. Rider kullanıyorsan `RiderProjects/WorkFlow.sln` dosyasını açıp başlangıç projesi olarak `WorkFlow.Mvc` seçebilirsin.

## Sınıflar ne yapıyor?

| Parça | Sorumluluğu |
| --- | --- |
| `TaskItem` | Tek bir görevin başlığını, açıklamasını, önceliğini, durumunu ve tarihlerini tutar. |
| `Project` | Görevleri `Guid` anahtarlı bir sözlük içinde tutar. |
| `Priority`, `Status` | Seçilebilecek öncelik ve durum değerlerini sınırlar. |
| `ITaskService` | Görev işlemlerinin sözleşmesini tanımlar. |
| `TaskService` | Görev ekleme, silme, durum değiştirme ve listeleme kurallarını uygular. |
| `GecersizGorevException` | Görevle ilgili iş kuralı hatalarını ifade eder. |
| `TasksController` | Tarayıcıdan gelen isteği alır, servisi çağırır ve uygun görünümü döndürür. |
| `TaskCreateViewModel` | Görev ekleme formundaki alanları ve doğrulama kurallarını taşır. |
| `Views/Tasks` | Kullanıcının gördüğü HTML sayfalarını oluşturur. |

## MVC akışı

Örneğin görev eklerken tarayıcı formu `TasksController.Create` eylemine gönderir. Controller, form modelinin geçerli olup olmadığını kontrol eder, `TaskItem` oluşturur ve `ITaskService.GorevEkle` metodunu çağırır. Servis görevi `Project` içindeki sözlüğe koyar. Sonra controller liste sayfasına yönlendirir. Böylece **gösterim**, **istek yönetimi** ve **iş kuralları** ayrı yerlerde kalır.

MVC ve console sürümleri aynı sınıf adlarını ve temel yapıyı kullanır, ancak kendi nesneleri vardır. Görevler yalnızca çalışan uygulamanın belleğinde saklanır; uygulama yeniden başlayınca silinir. Kalıcı saklama ileride veritabanı eklenerek yapılabilir.

Yeni bir değişikliğe başlamadan önce `RiderProjects/AGENTS.md` ve `RiderProjects/PROJECT_MEMORY.md` dosyalarını oku.
