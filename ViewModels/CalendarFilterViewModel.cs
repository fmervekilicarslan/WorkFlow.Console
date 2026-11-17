using System.ComponentModel.DataAnnotations;
using WorkFlow.Mvc.Enums;

namespace WorkFlow.Mvc.ViewModels;

public sealed class CalendarFilterViewModel
{
    [StringLength(200, ErrorMessage = "Arama en fazla 200 karakter olabilir.")]
    public string? Search { get; set; }

    [EnumDataType(typeof(Status), ErrorMessage = "Geçerli bir durum seçin.")]
    public Status? Status { get; set; }

    [EnumDataType(typeof(Priority), ErrorMessage = "Geçerli bir öncelik seçin.")]
    public Priority? Priority { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Tarihsiz görev sayfası en az 1 olmalıdır.")]
    public int UndatedPage { get; set; } = 1;
}
