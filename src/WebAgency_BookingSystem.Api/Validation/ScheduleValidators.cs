// [INTENT]: Validazione dei corpi di PUT /admin/business-hours e PUT /admin/closures. Verifica formati orari/
// date e gli intervalli (open<close, break, dateFrom<=dateTo) prima di raggiungere il manager admin.

using System.Globalization;
using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Api.Validation;

internal static class TimeDateChecks
{
    public static bool IsTime(string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static bool IsDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static TimeOnly Time(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    public static DateOnly Date(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Validazione di un singolo giorno di orari del tenant.</summary>
public sealed class BusinessHoursItemValidator : AbstractValidator<BusinessHoursItem>
{
    public BusinessHoursItemValidator()
    {
        RuleFor(x => x.DayOfWeek).InclusiveBetween(0, 6).WithMessage("dayOfWeek deve essere 0..6.");

        When(x => x.IsOpen, () =>
        {
            RuleFor(x => x.OpenTime).Must(TimeDateChecks.IsTime).WithMessage("openTime: formato HH:mm obbligatorio.");
            RuleFor(x => x.CloseTime).Must(TimeDateChecks.IsTime).WithMessage("closeTime: formato HH:mm obbligatorio.");
            RuleFor(x => x).Must(OpenBeforeClose).WithMessage("openTime deve precedere closeTime.");
        });

        RuleFor(x => x).Must(ValidBreak).WithMessage("breakStart/breakEnd: entrambi o nessuno, formato HH:mm, e breakStart < breakEnd.");
    }

    private static bool OpenBeforeClose(BusinessHoursItem x) =>
        !TimeDateChecks.IsTime(x.OpenTime) || !TimeDateChecks.IsTime(x.CloseTime)
        || TimeDateChecks.Time(x.OpenTime!) < TimeDateChecks.Time(x.CloseTime!);

    private static bool ValidBreak(BusinessHoursItem x)
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

/// <summary>Validazione della sostituzione in blocco degli orari settimanali.</summary>
public sealed class SetBusinessHoursRequestValidator : AbstractValidator<SetBusinessHoursRequest>
{
    public SetBusinessHoursRequestValidator()
    {
        RuleFor(x => x.Days).NotEmpty().WithMessage("È richiesto almeno un giorno.");
        RuleForEach(x => x.Days).SetValidator(new BusinessHoursItemValidator());
    }
}

/// <summary>Validazione di una singola chiusura straordinaria.</summary>
public sealed class ClosureItemValidator : AbstractValidator<ClosureItem>
{
    public ClosureItemValidator()
    {
        RuleFor(x => x.DateFrom).Must(TimeDateChecks.IsDate).WithMessage("dateFrom: formato yyyy-MM-dd.");
        RuleFor(x => x.DateTo).Must(TimeDateChecks.IsDate).WithMessage("dateTo: formato yyyy-MM-dd.");
        RuleFor(x => x).Must(FromBeforeOrEqualTo).WithMessage("dateFrom non può essere successiva a dateTo.");
    }

    private static bool FromBeforeOrEqualTo(ClosureItem x) =>
        !TimeDateChecks.IsDate(x.DateFrom) || !TimeDateChecks.IsDate(x.DateTo)
        || TimeDateChecks.Date(x.DateFrom) <= TimeDateChecks.Date(x.DateTo);
}

/// <summary>Validazione della sostituzione in blocco delle chiusure.</summary>
public sealed class SetClosuresRequestValidator : AbstractValidator<SetClosuresRequest>
{
    public SetClosuresRequestValidator() => RuleForEach(x => x.Closures).SetValidator(new ClosureItemValidator());
}
