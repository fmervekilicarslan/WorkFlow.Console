using System.ComponentModel.DataAnnotations;

namespace WorkFlow.Mvc.ViewModels;

public sealed class ProfileViewModel
{
    public string Email { get; set; } = string.Empty;

    [StringLength(80, ErrorMessage = "Görünen ad en fazla 80 karakter olabilir.")]
    [Display(Name = "Görünen ad")]
    public string? DisplayName { get; set; }
}
