namespace TriviUpBackend.Errors;

/// <summary>
/// Error genérico de autenticación.
/// </summary>
public record AuthError(string Error)
{
    public string Error { get; set; } = Error;
}

/// <summary>
/// Error de credenciales inválidas en autenticación.
/// </summary>
public record AuthUnauthorizedError(string Error) : AuthError(Error);

/// <summary>
/// Error de conflicto en registro (usuario o email ya existente).
/// </summary>
public record AuthConflictError(string Error) : AuthError(Error);

/// <summary>
/// Error de validación en datos de autenticación.
/// </summary>
public record AuthValidationError(string Error) : AuthError(Error);