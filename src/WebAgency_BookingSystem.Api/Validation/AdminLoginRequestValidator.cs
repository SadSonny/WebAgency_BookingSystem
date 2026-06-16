// [INTENT]: Validazione del corpo di POST /api/v1/admin/auth/token. Verifica presenza e formati prima di
// raggiungere il servizio di autenticazione (che applica la verifica credenziali con messaggio neutro).

using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Api.Validation;

/// <summary>
/// Regole di validazione per <see cref="AdminLoginRequest"/>.
/// </summary>
public sealed class AdminLoginRequestValidator : AbstractValidator<AdminLoginRequest>
{
    public AdminLoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("L'email è obbligatoria.")
            .EmailAddress().WithMessage("Formato email non valido.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("La password è obbligatoria.");
    }
}
