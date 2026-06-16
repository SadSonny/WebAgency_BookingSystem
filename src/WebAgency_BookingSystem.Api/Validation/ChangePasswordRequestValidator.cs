// [INTENT]: Validazione del cambio password autenticato: password corrente presente, nuova conforme alla policy
// e diversa dalla corrente.

using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Validation;

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator(AccountSettings settings)
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("La password attuale è obbligatoria.");
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("La nuova password è obbligatoria.")
            .MinimumLength(settings.PasswordMinLength)
            .WithMessage($"La password deve avere almeno {settings.PasswordMinLength} caratteri.")
            .NotEqual(x => x.CurrentPassword).WithMessage("La nuova password deve essere diversa da quella attuale.");
    }
}
