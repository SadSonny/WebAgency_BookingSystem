// [INTENT]: Converte un esito di validazione FluentValidation nella response 422 del contratto:
// { type: "validation_error", message, errors: { campo: [messaggi] } }. I nomi dei campi sono normalizzati
// in camelCase con notazione a punti (es. Customer.Email → customer.email) per allinearsi al frontend.

using FluentValidation.Results;
using WebAgency_BookingSystem.Core.Dtos;

namespace WebAgency_BookingSystem.Api.Http;

/// <summary>
/// Helper di mappatura dei fallimenti di validazione verso la response 422.
/// </summary>
internal static class ValidationResults
{
    /// <summary>Costruisce la response 422 con il dettaglio per campo a partire dagli errori di validazione.</summary>
    public static IResult ToValidationProblem(this ValidationResult result)
    {
        Dictionary<string, string[]> errors = result.Errors
            .GroupBy(e => ToCamelCasePath(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        var body = new ErrorResponse("validation_error", "Dati non validi.", errors);
        return Results.Json(body, statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    // WHY: FluentValidation usa PascalCase con notazione a punti (Customer.Email). Il contratto espone
    // i campi in camelCase (customer.email), quindi normalizziamo ogni segmento del path.
    private static string ToCamelCasePath(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(ToCamelCase));

    private static string ToCamelCase(string segment) =>
        string.IsNullOrEmpty(segment) || char.IsLower(segment[0])
            ? segment
            : char.ToLowerInvariant(segment[0]) + segment[1..];
}
