// [INTENT]: Validazione del corpo di POST/PUT /api/v1/admin/services. Verifica nome, durata, postazioni e
// la configurazione del buffer prima di raggiungere il catalogo admin.

using FluentValidation;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Api.Validation;

/// <summary>
/// Regole di validazione per <see cref="ServiceWriteRequest"/>.
/// </summary>
public sealed class ServiceWriteRequestValidator : AbstractValidator<ServiceWriteRequest>
{
    private static readonly string[] ValidBufferPositions = ["Before", "After", "Both"];

    public ServiceWriteRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Il nome è obbligatorio.")
            .MaximumLength(255).WithMessage("Il nome non può superare 255 caratteri.");

        RuleFor(x => x.DurationMinutes).GreaterThan(0).WithMessage("La durata deve essere > 0.");

        RuleFor(x => x.ParallelSlots)
            .Must(p => p is null or >= 1).WithMessage("parallelSlots deve essere >= 1.");

        RuleFor(x => x.BufferMinutes)
            .Must(b => b is null or >= 0).WithMessage("bufferMinutes non può essere negativo.");

        RuleFor(x => x.BufferPosition)
            .Must(p => p is null || ValidBufferPositions.Contains(p))
            .WithMessage("bufferPosition non valido (ammessi: Before, After, Both).");
    }
}
