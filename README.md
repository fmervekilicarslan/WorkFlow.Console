# WorkFlow – Task Management Application

WorkFlow, görevlerin oluşturulmasını, listelenmesini ve yönetilmesini sağlayan, **C# ve ASP.NET Core MVC** kullanılarak geliştirilmiş bir görev yönetimi uygulamasıdır.

Bu proje, nesne yönelimli programlama prensiplerini ve ASP.NET Core MVC mimarisini uygulamalı olarak öğrenmek amacıyla geliştirilmektedir.

## Kullanılan Teknolojiler

- **C#**
- **.NET 10**
- **ASP.NET Core MVC**
- **Razor Views**
- **HTML ve CSS**
- **Git ve GitHub**

## Projenin Özellikleri

- Yeni görev oluşturma
- Mevcut görevleri listeleme
- Görev silme
- Görev durumunu güncelleme
- Görevlere öncelik ve tarih bilgisi atama
- Form doğrulama ve hata yönetimi

## Proje Mimarisi

Uygulama, ASP.NET Core MVC mimarisine uygun olarak katmanlara ayrılmıştır.

| Katman | Açıklama |
|---|---|
| Models | Görev ve proje verilerini temsil eder. |
| Views | Kullanıcının etkileşim kurduğu web sayfalarını oluşturur. |
| Controllers | HTTP isteklerini karşılar ve uygulama akışını yönetir. |
| Services | Görev yönetimiyle ilgili iş kurallarını uygular. |
| Interfaces | Servislerin uyması gereken sözleşmeleri tanımlar. |
| ViewModels | Form verilerini ve doğrulama kurallarını yönetir. |
| Enums | Görev önceliği ve durum değerlerini tanımlar. |
| Exceptions | Uygulamaya özgü hata durumlarını temsil eder. |

## Kurulum ve Çalıştırma

Projeyi çalıştırabilmek için bilgisayarınızda **.NET 10 SDK** kurulu olmalıdır.

1. Repository'yi bilgisayarınıza klonlayın:

   ```bash
   git clone https://github.com/fmervekilicarslan/WorkFlow.Console.git
   ```

2. Proje klasörüne geçin:

   ```bash
   cd WorkFlow.Console
   ```

3. MVC uygulamasını başlatın:

   ```bash
   dotnet run --project WorkFlow.Mvc.csproj
   ```

4. Terminalde belirtilen yerel adresi tarayıcıda açın.

## Veri Saklama

Uygulama şu anda görevleri bellekte saklamaktadır. Bu nedenle uygulama kapatıldığında görev verileri korunmaz.

Kalıcı veri saklama için ilerleyen aşamalarda veritabanı entegrasyonu yapılabilir.

## Geliştirme Durumu

WorkFlow, yazılım geliştirme stajı kapsamında üzerinde çalışılmaya devam edilen bir öğrenme ve uygulama projesidir.

Projenin geliştirilmesi sırasında MVC mimarisi, Dependency Injection, servis katmanı, HTTP istekleri ve Git sürüm kontrolü gibi konular üzerinde çalışılmaktadır.