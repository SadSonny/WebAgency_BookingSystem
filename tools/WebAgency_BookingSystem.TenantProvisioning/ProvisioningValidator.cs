// [INTENT]: Validazione del file di provisioning (step 7.2). Raccoglie TUTTI gli errori e li restituisce in
// blocco con messaggi chiari, così l'operatore corregge il file in una sola passata invece di scoprire un
// errore alla volta. Controlla campi obbligatori, formati (orari/date), vincoli e i riferimenti incrociati
// staff↔servizi.

using System.Globalization;

namespace WebAgency_BookingSystem.TenantProvisioning;

internal static class ProvisioningValidator
{
    private static readonly string[] ValidBufferPositions = ["Before", "After", "Both"];
    private static readonly string[] ValidNotificationMethods = ["email", "none"];

    public static IReadOnlyList<string> Validate(ProvisioningInput input)
    {
        var errors = new List<string>();

        Require(errors, input.Slug, "slug");
        Require(errors, input.Name, "name");
        Require(errors, input.SiteUrl, "siteUrl");
        Require(errors, input.OwnerEmail, "ownerEmail");
        if (!string.IsNullOrWhiteSpace(input.OwnerEmail) && !input.OwnerEmail.Contains('@', StringComparison.Ordinal))
        {
            errors.Add("ownerEmail: formato email non valido.");
        }

        if (input.BookingRules?.NotificationMethod is { } method && !ValidNotificationMethods.Contains(method))
        {
            errors.Add($"bookingRules.notificationMethod: valore non valido '{method}' (ammessi: email, none).");
        }

        ValidateBusinessHours(errors, input.BusinessHours);
        ValidateClosures(errors, input.SpecialClosures);
        IReadOnlyList<string> serviceLocalIds = ValidateServices(errors, input.Services);
        ValidateStaff(errors, input.Staff, serviceLocalIds);

        return errors;
    }

    private static void ValidateBusinessHours(List<string> errors, IReadOnlyList<BusinessHoursInput>? hours)
    {
        if (hours is null)
        {
            return;
        }

        foreach (BusinessHoursInput h in hours)
        {
            string ctx = $"businessHours[dayOfWeek={h.DayOfWeek}]";
            if (h.DayOfWeek is < 0 or > 6)
            {
                errors.Add($"{ctx}: dayOfWeek deve essere 0..6.");
            }

            if (!h.IsOpen)
            {
                continue;
            }

            TimeOnly? open = ParseTime(errors, h.OpenTime, $"{ctx}.openTime", required: true);
            TimeOnly? close = ParseTime(errors, h.CloseTime, $"{ctx}.closeTime", required: true);
            if (open is { } o && close is { } c && o >= c)
            {
                errors.Add($"{ctx}: openTime deve precedere closeTime.");
            }

            ValidateBreak(errors, h.BreakStart, h.BreakEnd, ctx);
        }
    }

    private static void ValidateClosures(List<string> errors, IReadOnlyList<SpecialClosureInput>? closures)
    {
        if (closures is null)
        {
            return;
        }

        for (int i = 0; i < closures.Count; i++)
        {
            string ctx = $"specialClosures[{i}]";
            DateOnly? from = ParseDate(errors, closures[i].DateFrom, $"{ctx}.dateFrom", required: true);
            DateOnly? to = ParseDate(errors, closures[i].DateTo, $"{ctx}.dateTo", required: true);
            if (from is { } f && to is { } t && f > t)
            {
                errors.Add($"{ctx}: dateFrom non può essere successiva a dateTo.");
            }
        }
    }

    private static IReadOnlyList<string> ValidateServices(List<string> errors, IReadOnlyList<ServiceInput>? services)
    {
        var localIds = new List<string>();
        if (services is null || services.Count == 0)
        {
            errors.Add("services: è richiesto almeno un servizio.");
            return localIds;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < services.Count; i++)
        {
            ServiceInput s = services[i];
            string ctx = $"services[{i}]";
            Require(errors, s.Name, $"{ctx}.name");

            if (string.IsNullOrWhiteSpace(s.LocalId))
            {
                errors.Add($"{ctx}.localId: obbligatorio (serve a collegare lo staff ai servizi).");
            }
            else
            {
                localIds.Add(s.LocalId);
                if (!seen.Add(s.LocalId))
                {
                    errors.Add($"{ctx}.localId: duplicato '{s.LocalId}'.");
                }
            }

            if (s.DurationMinutes is null or <= 0)
            {
                errors.Add($"{ctx}.durationMinutes: deve essere > 0.");
            }

            if (s.ParallelSlots is <= 0)
            {
                errors.Add($"{ctx}.parallelSlots: deve essere >= 1.");
            }

            if (s.BufferPosition is { } pos && !ValidBufferPositions.Contains(pos))
            {
                errors.Add($"{ctx}.bufferPosition: valore non valido '{pos}' (ammessi: Before, After, Both).");
            }
        }

        return localIds;
    }

    private static void ValidateStaff(List<string> errors, IReadOnlyList<StaffInput>? staff, IReadOnlyList<string> serviceLocalIds)
    {
        if (staff is null)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < staff.Count; i++)
        {
            StaffInput s = staff[i];
            string ctx = $"staff[{i}]";
            Require(errors, s.Name, $"{ctx}.name");

            if (string.IsNullOrWhiteSpace(s.LocalId))
            {
                errors.Add($"{ctx}.localId: obbligatorio.");
            }
            else if (!seen.Add(s.LocalId))
            {
                errors.Add($"{ctx}.localId: duplicato '{s.LocalId}'.");
            }

            if (s.BusinessHours is not null)
            {
                foreach (StaffBusinessHoursInput h in s.BusinessHours)
                {
                    string hctx = $"{ctx}.businessHours[dayOfWeek={h.DayOfWeek}]";
                    if (h.DayOfWeek is < 0 or > 6)
                    {
                        errors.Add($"{hctx}: dayOfWeek deve essere 0..6.");
                    }

                    if (!h.IsAvailable)
                    {
                        continue;
                    }

                    TimeOnly? start = ParseTime(errors, h.StartTime, $"{hctx}.startTime", required: true);
                    TimeOnly? end = ParseTime(errors, h.EndTime, $"{hctx}.endTime", required: true);
                    if (start is { } st && end is { } en && st >= en)
                    {
                        errors.Add($"{hctx}: startTime deve precedere endTime.");
                    }

                    ValidateBreak(errors, h.BreakStart, h.BreakEnd, hctx);
                }
            }

            if (s.Services is not null)
            {
                foreach (StaffServiceInput link in s.Services)
                {
                    if (string.IsNullOrWhiteSpace(link.ServiceLocalId))
                    {
                        errors.Add($"{ctx}.services: serviceLocalId obbligatorio.");
                    }
                    else if (!serviceLocalIds.Contains(link.ServiceLocalId))
                    {
                        errors.Add($"{ctx}.services: serviceLocalId '{link.ServiceLocalId}' non corrisponde ad alcun servizio.");
                    }

                    if (link.PriceOverride is < 0)
                    {
                        errors.Add($"{ctx}.services: priceOverride non può essere negativo.");
                    }
                }
            }
        }
    }

    private static void ValidateBreak(List<string> errors, string? breakStart, string? breakEnd, string ctx)
    {
        bool hasStart = !string.IsNullOrWhiteSpace(breakStart);
        bool hasEnd = !string.IsNullOrWhiteSpace(breakEnd);
        if (hasStart != hasEnd)
        {
            errors.Add($"{ctx}: breakStart e breakEnd vanno indicati entrambi o nessuno dei due.");
            return;
        }

        if (!hasStart)
        {
            return;
        }

        TimeOnly? bs = ParseTime(errors, breakStart, $"{ctx}.breakStart", required: true);
        TimeOnly? be = ParseTime(errors, breakEnd, $"{ctx}.breakEnd", required: true);
        if (bs is { } s && be is { } e && s >= e)
        {
            errors.Add($"{ctx}: breakStart deve precedere breakEnd.");
        }
    }

    private static void Require(List<string> errors, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field}: campo obbligatorio mancante.");
        }
    }

    private static TimeOnly? ParseTime(List<string> errors, string? value, string field, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors.Add($"{field}: orario obbligatorio (formato HH:mm).");
            }

            return null;
        }

        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly parsed))
        {
            return parsed;
        }

        errors.Add($"{field}: formato orario non valido '{value}' (atteso HH:mm).");
        return null;
    }

    private static DateOnly? ParseDate(List<string> errors, string? value, string field, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors.Add($"{field}: data obbligatoria (formato yyyy-MM-dd).");
            }

            return null;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
        {
            return parsed;
        }

        errors.Add($"{field}: formato data non valido '{value}' (atteso yyyy-MM-dd).");
        return null;
    }
}
