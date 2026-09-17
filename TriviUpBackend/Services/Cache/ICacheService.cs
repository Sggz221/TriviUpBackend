namespace TriviUpBackend.Services.Cache;

/// <summary>
/// Interfaz para el servicio de caché en memoria.
/// Proporciona operaciones de almacenamiento y recuperación de datos en caché.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Obtiene un valor del caché.
    /// </summary>
    /// <param name="key">Clave única del elemento en caché.</param>
    /// <returns>El valor almacenado o null si no existe.</returns>
    Task<T?> GetAsync<T>(string key);

    /// <summary>
    /// Almacena un valor en el caché.
    /// </summary>
    /// <param name="key">Clave única del elemento.</param>
    /// <param name="value">Valor a almacenar.</param>
    /// <param name="expiry">Tiempo de expiración opcional.</param>
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);

    /// <summary>
    /// Elimina un elemento del caché.
    /// </summary>
    /// <param name="key">Clave del elemento a eliminar.</param>
    Task RemoveAsync(string key);

    /// <summary>
    /// Elimina todos los elementos del caché que coincidan con un prefijo.
    /// </summary>
    /// <param name="prefix">Prefijo de las claves a eliminar.</param>
    Task RemoveByPrefixAsync(string prefix);
}