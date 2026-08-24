namespace Library.Output;

/// <summary>Represents a process output that carries a typed data payload.</summary>
/// <typeparam name="T">Type of the data payload.</typeparam>
public class DataOutput<T> : ProcessOutput
{
    /// <summary>The payload data for this output. May be <c>null</c>.</summary>
    public T? Data { get; set; }

    /// <summary>Creates a new <see cref="DataOutput{T}" /> instance.</summary>
    public static new DataOutput<T> New => new();

    /// <summary>Adds or replaces the data payload.</summary>
    public void AddData(T data)
    {
        Data = data;
    }

    /// <summary>Fluent helper to set the data payload and return the same instance.</summary>
    public DataOutput<T> WithData(T data)
    {
        Data = data;

        return this;
    }

    /// <summary>Fluent helper to add an error on the current instance.</summary>
    public new DataOutput<T> WithError(string error)
    {
        AddError(error);

        return this;
    }

    /// <summary>Fluent helper to add multiple errors on the current instance.</summary>
    public new DataOutput<T> WithErrors(IEnumerable<string>? errors)
    {
        AddErrors(errors);

        return this;
    }

    /// <summary>Fluent helper to add an informational message on the current instance.</summary>
    public new DataOutput<T> WithMessage(string message)
    {
        AddMessage(message);

        return this;
    }

    /// <summary>Fluent helper to add multiple informational messages on the current instance.</summary>
    public new DataOutput<T> WithMessages(IEnumerable<string>? messages)
    {
        AddMessages(messages);

        return this;
    }
}
