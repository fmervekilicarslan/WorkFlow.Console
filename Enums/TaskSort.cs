using System.ComponentModel.DataAnnotations;

namespace WorkFlow.Mvc.Enums;

public enum TaskSort
{
    [Display(Name = "En yeni önce")] Newest,
    [Display(Name = "En eski önce")] Oldest,
    [Display(Name = "Öncelik: yüksekten düşüğe")] PriorityHigh,
    [Display(Name = "Öncelik: düşükten yükseğe")] PriorityLow,
    [Display(Name = "Son tarih: en yakın önce")] DueSoon,
    [Display(Name = "Son tarih: en uzak önce")] DueLatest
}
