using System.ComponentModel.DataAnnotations;
using WorkFlow.Mvc.Enums;

namespace WorkFlow.Mvc.ViewModels;

public class TaskFormViewModel
{
    [Required(ErrorMessage = "Görev başlığı boş bırakılamaz.")]
    [StringLength(120, ErrorMessage = "Başlık en fazla 120 karakter olabilir.")]
    [Display(Name = "Başlık")]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Açıklama en fazla 1000 karakter olabilir.")]
    [Display(Name = "Açıklama")]
    public string? Description { get; set; }

    [EnumDataType(typeof(Priority), ErrorMessage = "Geçerli bir öncelik seçin.")]
    [Display(Name = "Öncelik")]
    public Priority Priority { get; set; } = Priority.Normal;

    [EnumDataType(typeof(Status), ErrorMessage = "Geçerli bir durum seçin.")]
    [Display(Name = "Durum")]
    public Status Status { get; set; } = Status.Todo;

    [Display(Name = "Son teslim tarihi")]
    [DataType(DataType.Date)]
    [Range(typeof(DateOnly), "1900-01-01", "9998-12-31", ParseLimitsInInvariantCulture = true,
        ErrorMessage = "1900 ile 9998 yılları arasında bir tarih seçin.")]
    public DateOnly? DueDate { get; set; }
}
