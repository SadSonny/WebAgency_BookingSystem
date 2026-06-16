// [INTENT]: Validazione dell'impostazione password da token (attivazione/reset). La lunghezza minima è
// configurabile (AccountSettings.PasswordMinLength) per centralizzare la policy.

using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Validation;

public sealed class SetPasswordRequestValidator : AbstractValidator<SetPasswordRequest>
{
    public SetPasswordRequestValidator(AccountSettings settings)
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage("Token mancante.");
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("La password è obbligatoria.")
            .MinimumLength(settings.PasswordMinLength)
            .WithMessage($"La password deve avere almeno {settings.PasswordMinLength} caratteri.");
    }
}
