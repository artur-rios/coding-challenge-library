namespace Library.Shared.Messages;

/// <summary>Canonical messages produced by the reader use cases.</summary>
public static class UserMessages
{
    /// <summary>The reader was registered.</summary>
    public const string UserCreatedSuccessfully = "User created successfully.";

    /// <summary>A reader's loans were retrieved.</summary>
    public const string UserLoansRetrievedSuccessfully = "User loans retrieved successfully.";

    /// <summary>No reader holds that identifier.</summary>
    public const string UserNotFound = "User not found.";

    /// <summary>Another reader is already registered with the submitted address.</summary>
    public const string EmailAlreadyRegistered = "A user with this e-mail already exists.";

    /// <summary>The name was not supplied.</summary>
    public const string NameRequired = "Name is required.";

    /// <summary>The name is longer than the column that stores it.</summary>
    public const string NameTooLong = "Name must be at most 200 characters.";

    /// <summary>The e-mail address was not supplied.</summary>
    public const string EmailRequired = "E-mail is required.";

    /// <summary>The e-mail address is not well-formed.</summary>
    public const string EmailInvalid = "E-mail must be a valid address.";

    /// <summary>The e-mail address is longer than the column that stores it.</summary>
    public const string EmailTooLong = "E-mail must be at most 320 characters.";
}
