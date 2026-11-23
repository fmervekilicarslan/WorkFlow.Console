using System.ComponentModel.DataAnnotations;

namespace WorkFlow.Mvc.ViewModels;

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Mevcut parolanızı girin.")]
    [StringLength(128, ErrorMessage = "Mevcut parola en fazla 128 karakter olabilir.")]
    [Display(Name = "Mevcut parola")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni parolanızı girin.")]
    [StringLength(128, ErrorMessage = "Yeni parola en fazla 128 karakter olabilir.")]
    [Display(Name = "Yeni parola")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni parolanızı tekrar girin.")]
    [StringLength(128, ErrorMessage = "Yeni parola tekrarı en fazla 128 karakter olabilir.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Yeni parolalar eşleşmiyor.")]
    [Display(Name = "Yeni parola tekrarı")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
