// [INTENT]: Validazione del corpo di POST/PUT /api/v1/admin/staff. Verifica nome, orari dello staff e gli
// override prezzo. La validità dei serviceId (devono appartenere a servizi attivi del tenant) è verificata
// nel manager perché DB-dipendente.

using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Api.Validation;

/// <summary>Validazione di un giorno di orari dello staff.</summary>
public sealed class StaffBusinessHoursItemValidator : AbstractValidator<StaffBusinessHoursItem>
{
    public StaffBusinessHoursItemValidator()
    {
        RuleFor(x => x.DayOfWeek).InclusiveBetween(0, 6).WithMessage("dayOfWeek deve essere 0..6.");

        When(x => x.IsAvailable, () =>
        {
            RuleFor(x => x.StartTime).Must(TimeDateChecks.IsTime).WithMessage("startTime: formato HH:mm obbligatorio.");
            RuleFor(x => x.EndTime).Must(TimeDateChecks.IsTime).WithMessage("endTime: formato HH:mm obbligatorio.");
            RuleFor(x => x).Must(StartBeforeEnd).WithMessage("startTime deve precedere endTime.");
        });

        RuleFor(x => x).Must(ValidBreak).WithMessage("breakStart/breakEnd: entrambi o nessuno, formato HH:mm, e breakStart < breakEnd.");
    }

    private static bool StartBeforeEnd(StaffBusinessHoursItem x) =>
        !TimeDateChecks.IsTime(x.StartTime) || !TimeDateChecks.IsTime(x.EndTime)
        || TimeDateChecks.Time(x.StartTime!) < TimeDateChecks.Time(x.EndTime!);

    private static bool ValidBreak(StaffBusinessHoursItem x)
    {
        bool hasStart = !string.IsNullOrWhiteSpace(x.BreakStart);
        bool hasEnd = !string.IsNullOrWhiteSpace(x.BreakEnd);
        if (hasStart != hasEnd)
        {
            return false;
        }

        if (!hasStart)
        {
            return true;
        }

        return TimeDateChecks.IsTime(x.BreakStart) && TimeDateChecks.IsTime(x.BreakEnd)
            && TimeDateChecks.Time(x.BreakStart!) < TimeDateChecks.Time(x.BreakEnd!);
    }
}

/// <summary>Regole di validazione per <see cref="StaffWriteRequest"/>.</summary>
public sealed class StaffWriteRequestValidator : AbstractValidator<StaffWriteRequest>
{
    public StaffWriteRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Il nome è obbligatorio.")
            .MaximumLength(255).WithMessage("Il nome non può superare 255 caratteri.");

        RuleForEach(x => x.BusinessHours).SetValidator(new StaffBusinessHoursItemValidator());

        RuleForEach(x => x.Services).Must(s => s.PriceOverride is null or >= 0)
            .WithMessage("priceOverride non può essere negativo.");
    }
}
