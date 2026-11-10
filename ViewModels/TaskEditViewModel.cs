using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace WorkFlow.Mvc.ViewModels;

public class TaskEditViewModel : TaskFormViewModel
{
    [BindNever] public Guid Id { get; set; }
    [BindNever, ValidateNever] public string Code { get; set; } = string.Empty;
}
