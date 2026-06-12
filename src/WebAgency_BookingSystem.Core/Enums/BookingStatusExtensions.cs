// [INTENT]: Conversione tra BookingStatus e la rappresentazione snake_case esposta dall'API (contratto:
// confirmed | cancelled | no_show | completed). Tenuta separata dallo storage DB: nel database lo stato è
// persistito come nome dell'enum, mentre verso il client si usa sempre questa forma.

namespace WebAgency_BookingSystem.Core.Enums;

/// <summary>
/// Estensioni di mappatura di <see cref="BookingStatus"/> verso le stringhe del contratto API.
/// </summary>
public static class BookingStatusExtensions
{
    /// <summary>Restituisce la rappresentazione snake_case dello stato usata nelle response API.</summary>
    public static string ToApiString(this BookingStatus status) => status switch
    {
        BookingStatus.Confirmed => "confirmed",
        BookingStatus.Cancelled => "cancelled",
        BookingStatus.NoShow => "no_show",
        BookingStatus.Completed => "completed",
        _ => status.ToString().ToLowerInvariant(),
    };
}
