using System.ComponentModel.DataAnnotations;
using WorkFlow.Mvc.Enums;

namespace WorkFlow.Mvc.ViewModels;

public class TaskCreateViewModel
{
    [Required(ErrorMessage = "Başlık zorunludur.")]
    [StringLength(120, ErrorMessage = "Başlık en fazla 120 karakter olabilir.")]
    [Display(Name = "Başlık")]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Açıklama en fazla 1000 karakter olabilir.")]
    [Display(Name = "Açıklama")]
    public string Description { get; set; } = string.Empty;

    [EnumDataType(typeof(Priority), ErrorMessage = "Geçerli bir öncelik seçin.")]
    [Display(Name = "Öncelik")]
    public Priority Priority { get; set; } = Priority.Normal;

    [EnumDataType(typeof(Status), ErrorMessage = "Geçerli bir durum seçin.")]
    [Display(Name = "Durum")]
    public Status Status { get; set; } = Status.Todo;
}
