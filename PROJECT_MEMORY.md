# WorkFlow.Mvc proje hafızası

- Bu proje `RiderProjects/WorkFlow.Mvc` konumunda, `WorkFlow.Console` ile kardeş klasördedir. Yerini değiştirme.
- Kaynak örnek `../WorkFlow.Console/WorkFlow.Console_v0.3` projesidir. `TaskItem`, `Project`, enum, exception, `ITaskService` ve `TaskService` kavramları paraleldir.
- MVC tarafında `TasksController` HTTP isteklerini karşılar; Razor view'lar HTML oluşturur; `TaskCreateViewModel` form doğrulamasını taşır.
- Görevler singleton `Project` nesnesindeki sözlükte, yalnızca uygulama çalışırken tutulur. `TaskService` eş zamanlı istekler için `lock` kullanır.
- Kullanıcı her güncellemeden önce yapılacakların ve nedenlerinin listesini, sonrasında da öğrenmeye yönelik açıklamayı istiyor.
- Üst klasördeki `WorkFlow.sln` Console v0.3 ve MVC'yi birlikte açar.
- Kullanıcı `https://github.com/fmervekilicarslan/WorkFlow.Console` GitHub deposunu MVC için kullanmak istiyor. Depo adı farklı olsa da adres MVC kodu için kullanılabilir. **Her push öncesinde kullanıcıya değişiklikleri gösterip açık onayını bekle.** Uzak geçmiş ağ engeli nedeniyle henüz okunamadı; push yapılmadı. Kullanıcı, ancak mevcut depo kullanılamıyorsa silip yeni depo oluşturmayı önerdi; gereklilik kanıtlanmadığı için hiçbir GitHub deposu silinmedi veya oluşturulmadı.
