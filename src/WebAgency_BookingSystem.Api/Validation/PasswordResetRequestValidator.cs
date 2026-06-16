// [INTENT]: Validazione della richiesta di reset: email presente e formalmente valida. L'esito del servizio è
// comunque neutro (non rivela se l'email è registrata).

using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Api.Validation;

public sealed class PasswordResetRequestValidator : AbstractValidator<PasswordResetRequest>
{
    public PasswordResetRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("L'email è obbligatoria.")
            .EmailAddress().WithMessage("Formato email non valido.");
    }
}
