using System.ComponentModel.DataAnnotations;
using WorkFlow.Mvc.Enums;

namespace WorkFlow.Mvc.ViewModels;

public sealed class TaskFilterViewModel
{
    [StringLength(200, ErrorMessage = "Arama en fazla 200 karakter olabilir.")]
    public string? Search { get; set; }
    [EnumDataType(typeof(Status), ErrorMessage = "Geçerli bir durum seçin.")]
    public Status? Status { get; set; }
    [EnumDataType(typeof(Priority), ErrorMessage = "Geçerli bir öncelik seçin.")]
    public Priority? Priority { get; set; }
    [EnumDataType(typeof(TaskSort), ErrorMessage = "Geçerli bir sıralama seçin.")]
    public TaskSort Sort { get; set; } = TaskSort.Newest;
    [Range(1, int.MaxValue, ErrorMessage = "Sayfa numarası en az 1 olmalıdır.")]
    public int Page { get; set; } = 1;
    [Range(5, 50, ErrorMessage = "Sayfa boyutu 5 ile 50 arasında olmalıdır.")]
    public int PageSize { get; set; } = 10;
}
