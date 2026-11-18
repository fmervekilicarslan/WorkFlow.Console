using System.ComponentModel.DataAnnotations;

namespace WorkFlow.Mvc.ViewModels;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "E-posta adresinizi girin.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(256, ErrorMessage = "E-posta adresi en fazla 256 karakter olabilir.")]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Parolanızı girin.")]
    [DataType(DataType.Password)]
    [StringLength(128, ErrorMessage = "Parola en fazla 128 karakter olabilir.")]
    [Display(Name = "Parola")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Beni hatırla")]
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
