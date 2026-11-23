using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WorkFlow.Mvc.ViewModels;

public sealed class ProfileViewModel
{
    [BindNever]
    public string Email { get; set; } = string.Empty;

    [StringLength(80, ErrorMessage = "Görünen ad en fazla 80 karakter olabilir.")]
    [Display(Name = "Görünen ad")]
    public string? DisplayName { get; set; }

    [StringLength(240, ErrorMessage = "Hakkımda en fazla 240 karakter olabilir.")]
    [Display(Name = "Hakkımda")]
    public string? AboutMe { get; set; }

    [BindNever]
    public DateTimeOffset? JoinedAtUtc { get; set; }
    [BindNever]
    public Guid? PhotoKey { get; set; }
    [BindNever]
    public string Initials { get; set; } = "?";
}
