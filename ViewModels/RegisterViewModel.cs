using System.ComponentModel.DataAnnotations;

namespace WorkFlow.Mvc.ViewModels;

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "E-posta adresinizi girin.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(256, ErrorMessage = "E-posta adresi en fazla 256 karakter olabilir.")]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Parolanızı girin.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Parola 12 ile 128 karakter arasında olmalıdır.")]
    [DataType(DataType.Password)]
    [Display(Name = "Parola")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Parolanızı tekrar girin.")]
    [Compare(nameof(Password), ErrorMessage = "Parolalar eşleşmiyor.")]
    [DataType(DataType.Password)]
    [Display(Name = "Parola tekrarı")]
    public string ConfirmPassword { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}
