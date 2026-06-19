// [INTENT]: Validazione della richiesta di cancellazione DSAR: l'email del cliente è obbligatoria e formalmente
// valida (altrimenti 422 con messaggio dedicato invece di un match silenzioso a vuoto).

using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Api.Validation;

public sealed class EraseCustomerRequestValidator : AbstractValidator<EraseCustomerRequest>
{
    public EraseCustomerRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("L'email è obbligatoria.")
            .EmailAddress().WithMessage("Formato email non valido.");
    }
}
