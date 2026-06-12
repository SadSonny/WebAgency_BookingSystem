// [INTENT]: Pattern Result per esiti di operazioni che possono fallire in modo ATTESO (no eccezioni per
// il flusso di controllo, come da convenzioni di progetto). Result rappresenta successo/fallimento senza
// valore; Result<T> trasporta anche un valore in caso di successo. Le conversioni implicite riducono il
// boilerplate nei servizi (return error; / return value;).

namespace WebAgency_BookingSystem.Core.Common;

/// <summary>
/// Esito di un'operazione senza valore di ritorno. In caso di fallimento espone l'<see cref="Error"/>.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        // WHY: invariante del pattern — un successo non può portare un errore e viceversa.
        // Violare questo significa un bug di costruzione, quindi è un'eccezione (non un Result).
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("Un Result di successo non può avere un errore.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("Un Result di fallimento deve avere un errore.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>True se l'operazione è riuscita.</summary>
    public bool IsSuccess { get; }

    /// <summary>True se l'operazione è fallita.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Errore associato al fallimento, oppure <see cref="Error.None"/> in caso di successo.</summary>
    public Error Error { get; }

    /// <summary>Crea un esito di successo senza valore.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>Crea un esito di fallimento con l'errore indicato.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Crea un esito di successo con valore.</summary>
    public static Result<T> Success<T>(T value) => new(value, true, Error.None);

    /// <summary>Crea un esito di fallimento tipizzato con l'errore indicato.</summary>
    public static Result<T> Failure<T>(Error error) => new(default, false, error);
}

/// <summary>
/// Esito di un'operazione che restituisce un valore <typeparamref name="T"/> in caso di successo.
/// </summary>
public class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, bool isSuccess, Error error) : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>
    /// Valore prodotto in caso di successo. Accedervi su un Result fallito è un errore di programmazione.
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Impossibile leggere il valore di un Result fallito.");

    /// <summary>Promuove implicitamente un valore a Result di successo.</summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>Promuove implicitamente un Error a Result di fallimento.</summary>
    public static implicit operator Result<T>(Error error) => Failure<T>(error);
}
