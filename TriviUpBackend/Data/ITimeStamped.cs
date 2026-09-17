namespace TriviUpBackend.Data;

/// <summary>
/// Interfaz para entidades con timestamps de creación y actualización.
/// </summary>
public interface ITimestamped
{
    /// <summary>
    /// Fecha de creación del registro.
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// Fecha de última actualización del registro.
    /// </summary>
    DateTime UpdatedAt { get; }
}