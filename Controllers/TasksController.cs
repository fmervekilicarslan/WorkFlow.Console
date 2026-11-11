using Microsoft.AspNetCore.Mvc;
using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Exceptions;
using WorkFlow.Mvc.Interfaces;
using WorkFlow.Mvc.Models;
using WorkFlow.Mvc.ViewModels;
using WorkFlow.Mvc.Services;
using Microsoft.EntityFrameworkCore;

namespace WorkFlow.Mvc.Controllers;

public class TasksController : Controller
{
    private readonly ITaskService _taskService;
    private readonly Project _project;

    public TasksController(ITaskService taskService, Project project)
    {
        _taskService = taskService;
        _project = project;
    }

    public IActionResult Index([Bind(Prefix = "Filter")] TaskFilterViewModel filter)
    {
        //ViewModel, View'ın yani kullanıcının gördüğü ekranın
        //ihtiyaç duyduğu verileri taşımak için kullanılan sınıftır.
        
        //TaskListViewModel → Görevleri listelemek için.
        var page = ModelState.IsValid
            ? _taskService.SayfaliGorevleriGetir(filter.Search, filter.Status, filter.Priority, filter.Sort, filter.Page, filter.PageSize)
            : new TaskPage();
        if (ModelState.IsValid) filter.Page = page.Page;
        return View(new TaskListViewModel
        {
            ProjectName = _project.Name,
            Tasks = page.Items,
            Pagination = page,
            Filter = filter,
            Summary = _taskService.OzetGetir(),
            Today = TaskDates.LocalDate(DateTime.UtcNow)
        });
    }

    [HttpGet]
    public IActionResult Details(Guid id)
    {
        var task = _taskService.GorevGetir(id);
        return task is null ? MissingTask() : View(new TaskDetailsViewModel
        { Task = task, Today = TaskDates.LocalDate(DateTime.UtcNow) });
    }

    [HttpGet]
    public IActionResult Edit(Guid id)
    {
        var task = _taskService.GorevGetir(id);
        return task is null ? MissingTask() : View(new TaskEditViewModel
        {
            Id = task.Id, Code = task.Code, Title = task.Title, Description = task.Description,
            Priority = task.Priority, Status = task.Status,
            DueDate = task.DueDate.HasValue ? TaskDates.LocalDate(task.DueDate.Value) : null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(Guid id, TaskEditViewModel model)
    {
        var existing = _taskService.GorevGetir(id);
        if (existing is null) return MissingTask();
        model.Id = existing.Id;
        model.Code = existing.Code;
        if (!ModelState.IsValid) return View(model);
        try
        {
            if (!_taskService.GorevGuncelle(id, model.Title, model.Description, model.Priority, model.Status,
                model.DueDate.HasValue ? TaskDates.ToUtc(model.DueDate.Value) : null)) return MissingTask();
            TempData["Success"] = "Görev güncellendi.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (GecersizGorevException ex) { ModelState.AddModelError(string.Empty, ex.Message); }
        catch (DbUpdateException) { ModelState.AddModelError(string.Empty, "Görev kaydedilemedi. Bilgileriniz korundu; tekrar deneyin."); }
        return View(model);
    }

    private ViewResult MissingTask()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("NotFound");
    }

    [HttpGet]
    //[HttpGet] nedir?
    //Bu bir attribute.
    // ASP.NET Core MVC'ye bu action'ın HTTP GET isteklerini karşılaması gerektiğini belirtir.
    
    //TaskCreateViewModel → Yeni görev bilgilerini almak için.
    
    public IActionResult Create() => View(new TaskCreateViewModel());
    //kullanıcı görev ekleme sayfasını açtığında boş bir form gösterilmesi amaçlanıyor.

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(TaskCreateViewModel model)
    {
        foreach (var (field, message) in new[]
                 {
                     (nameof(model.Priority), "Geçerli bir öncelik seçin."),
                     (nameof(model.Status), "Geçerli bir durum seçin.")
                 })
        {
            if (ModelState[field] is { Errors.Count: > 0 } entry)
            {
                entry.Errors.Clear();
                ModelState.AddModelError(field, message);
            }
        }

        if (string.IsNullOrWhiteSpace(model.Title) &&
            (ModelState[nameof(model.Title)]?.Errors.Count ?? 0) == 0)
        {
            //ModelState'e, adı Title olan alan için "Başlık boş olamaz." hatasını ekle.
            ModelState.AddModelError(nameof(model.Title), "Görev başlığı boş bırakılamaz.");
        }
           //IsValid -> Geçerli mi?
        if (!ModelState.IsValid)//Burada ModelState nesnesinin doğrulama durumu kontrol edilir.
        //Eğer ModelState geçerli değilse, yani doğrulama hatası varsa formu tekrar göster.
        {
            //Buradaki view metodunu ben ayrıca tanımlamadım.
            //Kalıtım aldığım hazır Controller sınıfı sayesinde kullanabiliyorum.
            //Frameworkten gelen hazır metot.
            return View(model);
        }
        // Burada görevin başlığı daha eklenmedi!!!

        try
        {
            //Trim() → Metnin başındaki ve sonundaki boşlukları kaldırır.
            //?. → Nesne null değilse ilgili işlemi gerçekleştirir.
            //?? → Sol taraf null ise sağdaki değeri kullanır.
            var task = new TaskItem(model.Title.Trim(), model.Description?.Trim() ?? string.Empty,
                model.Priority, model.Status)
            {
                DueDate = model.DueDate.HasValue ? TaskDates.ToUtc(model.DueDate.Value) : null
            };
            _taskService.GorevEkle(task);
            
            //TempData, genellikle bir sonraki istekte gösterilmek üzere kısa süreli mesaj saklamak için kullanılır.
            /*Örneğin kullanıcı görev ekledikten sonra görev listesine yönlendirildiğinde ekranda şu mesaj gösterilebilir:
             Görev eklendi.
             Tabii bunun görünmesi için View tarafında TempData["Success"] değerini okuyup gösteren kod bulunmalıdır.*/
            TempData["Success"] = "Görev eklendi.";
            
            //Mevcut action'ı bitir ve tarayıcıyı aynı Controller içerisindeki Index action'ına yönlendir.
            return RedirectToAction(nameof(Index));
        }
        catch (GecersizGorevException ex)
        {
            ModelState.AddModelError(nameof(model.Title), ex.Message);
            return View(model);
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(string.Empty, "Görev kaydedilemedi. Bilgileriniz korundu; tekrar deneyin.");
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(Guid gorevId, [Bind(Prefix = "Filter")] TaskFilterViewModel? filter = null)
    {
        try
        {
            _taskService.GorevSil(gorevId);
            
            //TempData özellikle yönlendirme sonrasında mesaj göstermek amacıyla kullanılır.
            TempData["Success"] = "Görev silindi.";
        }
        catch (GecersizGorevException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Görev silinemedi. Listeyi yenileyip tekrar deneyin.";
        }
        //tarayıcıyı görev listesi action'ına yönlendirir.
        return ReturnToList(filter);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    /*ASP.NET Core MVC'de gelen isteğin geçerli bir antiforgery token içerip içermediğini kontrol ederek CSRF saldırılarına karşı koruma sağlamaya yardımcı olan bir attribute'tur.

     CSRF (Cross-Site Request Forgery), kullanıcının oturumundan yararlanılarak onun bilgisi dışında başka bir siteden istek gönderilmesine dayanan saldırı türüdür.

     Örneğin kötü niyetli bir site, kullanıcının oturumundan yararlanarak uygulamana görev silme isteği göndermeye çalışabilir.

     Antiforgery token kontrolü, bu tür sahte istekleri engellemeye yardımcı olur.

     Bu attribute'un düzgün çalışabilmesi için isteği gönderen formun da uygun token'ı içermesi gerekir.*/
    public IActionResult UpdateStatus(Guid gorevId, Status yeniDurum, [Bind(Prefix = "Filter")] TaskFilterViewModel? filter = null)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Geçerli bir görev ve durum seçin.";
            return RedirectToAction(nameof(Index));
        }
        try
        {
            _taskService.GorevDurumunuGuncelle(gorevId, yeniDurum);
            TempData["Success"] = "Görev durumu güncellendi.";
        }
        catch (GecersizGorevException ex)
        {
            TempData["Error"] = ex.Message;
        }

        catch (DbUpdateException)
        {
            TempData["Error"] = "Durum kaydedilemedi. Listeyi yenileyip tekrar deneyin.";
        }
        return ReturnToList(filter);
    }

    private IActionResult ReturnToList(TaskFilterViewModel? filter) => RedirectToAction(nameof(Index),
        filter is null || !ModelState.IsValid ? null : new Microsoft.AspNetCore.Routing.RouteValueDictionary
        {
            ["Filter.Search"] = filter.Search, ["Filter.Status"] = filter.Status,
            ["Filter.Priority"] = filter.Priority, ["Filter.Sort"] = filter.Sort,
            ["Filter.Page"] = filter.Page, ["Filter.PageSize"] = filter.PageSize
        });
/*ResponseCache nedir?
 ASP.NET Core MVC'de bir HTTP yanıtının önbelleğe alınmasıyla ilgili ayarları belirlemek için kullanılan attribute'tur.

Cache (önbellek) nedir?
Önbellek, daha önce elde edilmiş verilerin veya yanıtların tekrar kullanılmak üzere geçici olarak saklanmasıdır.

Amaç genellikle performansı artırmak ve gereksiz işlemleri azaltmaktır.

Ancak hata sayfalarında eski veya kullanıcıya özel hata bilgilerinin önbellekte saklanmasını istemeyebiliriz.

Bu nedenle Error() metodunda önbellekleme kısıtlanmış*/
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    /*Parametre	Genel anlamı
Duration = 0	Önbellekleme süresini 0 saniye olarak ayarlar.
Location = ResponseCacheLocation.None	Yanıtın önbellekte tutulabileceği bir konum belirtmez; istemci ve aracı önbellekler için saklamama yönergelerine katkı sağlar.
NoStore = true	HTTP yanıtının saklanmaması gerektiğini belirtir.
Bu ayarlar HTTP yanıt başlıkları üzerinden önbellek davranışını yönlendirmek içindir.
ResponseCacheLocation.None içindeki None ne demek?
ResponseCacheLocation, ASP.NET Core MVC'nin hazır bir enum türüdür.

None, burada önbellekleme konumu seçilmediğini ifade eden enum üyesidir.*/
    //Hata sayfası metodu
    public IActionResult Error() => View();
}
