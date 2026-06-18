// [INTENT]: Validazione del corpo di POST /api/v1/platform/setup. Verifica SetupToken presente,
// email ben formata e password conforme alla policy configurata (PasswordMinLength) prima di
// raggiungere il servizio (che applica il confronto a tempo costante del token).

using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Platform;
using WebAgency_BookingSystem.Infrastructure.Auth;

namespace WebAgency_BookingSystem.Api.Validation;

/// <summary>
/// Regole di validazione per <see cref="PlatformSetupRequest"/>.
/// </summary>
public sealed class PlatformSetupRequestValidator : AbstractValidator<PlatformSetupRequest>
{
    public PlatformSetupRequestValidator(AccountSettings settings)
    {
        RuleFor(x => x.SetupToken)
            .NotEmpty().WithMessage("Il token di setup è obbligatorio.");
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("L'email è obbligatoria.")
            .EmailAddress().WithMessage("Formato email non valido.");
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La password è obbligatoria.")
            .MinimumLength(settings.PasswordMinLength)
            .WithMessage($"La password deve avere almeno {settings.PasswordMinLength} caratteri.");
    }
}
