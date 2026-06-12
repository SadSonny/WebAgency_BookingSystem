// [INTENT]: Validazione del corpo di POST /api/v1/bookings. Verifica i formati e i campi obbligatori prima
// che la richiesta raggiunga il BookingService (regole di business e disponibilità sono verificate lì).
// I messaggi sono in italiano e finiscono nel dettaglio `errors` della response 422.

using System.Globalization;
using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Public;

namespace WebAgency_BookingSystem.Api.Validation;

/// <summary>
/// Regole di validazione per <see cref="CreateBookingRequest"/>.
/// </summary>
public sealed class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.ServiceId).NotEmpty().WithMessage("Il servizio è obbligatorio.");

        RuleFor(x => x.Date)
            .NotEmpty().WithMessage("La data è obbligatoria.")
            .Must(BeValidDate).WithMessage("Formato data non valido. Usare yyyy-MM-dd.");

        RuleFor(x => x.Time)
            .NotEmpty().WithMessage("L'orario è obbligatorio.")
            .Must(BeValidTime).WithMessage("Formato orario non valido. Usare HH:mm.");

        RuleFor(x => x.GdprConsent)
            .Equal(true).WithMessage("Il consenso al trattamento dei dati è obbligatorio.");

        RuleFor(x => x.Customer).NotNull().WithMessage("I dati del cliente sono obbligatori.");

        When(x => x.Customer is not null, () =>
        {
            RuleFor(x => x.Customer.Name)
                .NotEmpty().WithMessage("Il nome è obbligatorio.")
                .MaximumLength(255).WithMessage("Il nome non può superare 255 caratteri.");

            RuleFor(x => x.Customer.Phone)
                .NotEmpty().WithMessage("Il telefono è obbligatorio.");

            RuleFor(x => x.Customer.Email)
                .NotEmpty().WithMessage("L'email è obbligatoria.")
                .EmailAddress().WithMessage("Formato email non valido.");
        });
    }

    private static bool BeValidDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool BeValidTime(string value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
