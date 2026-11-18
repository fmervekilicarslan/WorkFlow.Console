using Microsoft.AspNetCore.Identity;

namespace WorkFlow.Mvc.Services;

public sealed class TurkishIdentityErrorDescriber : IdentityErrorDescriber
{
    private static IdentityError Error(string code, string text) => new() { Code = code, Description = text };
    public override IdentityError DefaultError() => Error(nameof(DefaultError), "İşlem tamamlanamadı. Lütfen tekrar deneyin.");
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), "Kayıt tamamlanamadı. Bilgilerinizi kontrol edin veya giriş yapmayı deneyin.");
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), "Kayıt tamamlanamadı. Bilgilerinizi kontrol edin veya giriş yapmayı deneyin.");
    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), "Geçerli bir e-posta adresi girin.");
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), "Geçerli bir e-posta adresi girin.");
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), $"Parola en az {length} karakter olmalıdır.");
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "Parola en az bir rakam içermelidir.");
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "Parola en az bir küçük harf içermelidir.");
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "Parola en az bir büyük harf içermelidir.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), "Parola en az bir özel karakter içermelidir.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), "Parola daha fazla farklı karakter içermelidir.");
}
