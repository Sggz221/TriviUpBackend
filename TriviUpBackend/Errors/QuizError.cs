namespace TriviUpBackend.Errors;

/// <summary>
/// Error genérico de quiz.
/// </summary>
public record QuizError(string Error);

/// <summary>
/// Error cuando un quiz no es encontrado.
/// </summary>
public record QuizNotFoundError(string Error) : QuizError(Error);

/// <summary>
/// Error de validación en datos de quiz.
/// </summary>
public record QuizValidationError(string Error) : QuizError(Error);

/// <summary>
/// Error cuando el usuario no tiene permisos sobre el quiz.
/// </summary>
public record QuizForbiddenError(string Error) : QuizError(Error);
