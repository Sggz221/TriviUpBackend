using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace TriviUpBackend.Services.Cache;

/// <summary>
/// Implementación del servicio de caché en memoria.
/// Utiliza IMemoryCache de Microsoft.Extensions.Caching.
/// </summary>
public class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryCacheService> _logger;
    private readonly ConcurrentDictionary<string, HashSet<string>> _keysByPrefix = new();

    /// <summary>
    /// Constructor del servicio de caché en memoria.
    /// </summary>
    /// <param name="cache">Caché de memoria de Microsoft.</param>
    /// <param name="logger">Logger para mensajes de diagnóstico.</param>
    public MemoryCacheService(IMemoryCache cache, ILogger<MemoryCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
        _logger.LogInformation("[MemoryCacheService] Servicio inicializado");
    }

    /// <inheritdoc cref="ICacheService.GetAsync"/>
    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            if (_cache.TryGetValue(key, out var value))
            {
                _logger.LogDebug("[MemoryCache] GET HIT para key: {Key}", key);
                return (T?)value;
            }
            _logger.LogDebug("[MemoryCache] GET MISS para key: {Key}", key);
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MemoryCache] GET FALLO para key {Key}. Error: {Error}", key, ex.Message);
            return default;
        }
    }

    /// <inheritdoc cref="ICacheService.SetAsync"/>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        try
        {
            _logger.LogInformation("[MemoryCache] SET para key: {Key}, expiry: {Expiry}", key, expiry);

            // Registrar la key bajo todos sus prefixes
            TrackKeyByPrefixes(key);

            var options = new MemoryCacheEntryOptions();
            if (expiry.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = expiry;
            }
            _cache.Set(key, value, options);
            _logger.LogInformation("[MemoryCache] SET exitoso para key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MemoryCache] SET FALLO para key {Key}. Error: {Error}", key, ex.Message);
        }
    }

    /// <inheritdoc cref="ICacheService.RemoveAsync"/>
    public async Task RemoveAsync(string key)
    {
        try
        {
            _logger.LogInformation("[MemoryCache] REMOVE para key: {Key}", key);
            _cache.Remove(key);
            RemoveKeyFromPrefixTracking(key);
            _logger.LogInformation("[MemoryCache] REMOVE exitoso para key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MemoryCache] REMOVE FALLO para key {Key}. Error: {Error}", key, ex.Message);
        }
    }

    /// <inheritdoc cref="ICacheService.RemoveByPrefixAsync"/>
    public async Task RemoveByPrefixAsync(string prefix)
    {
        try
        {
            _logger.LogInformation("[MemoryCache] INVALIDAR CACHE por prefijo: {Prefix}", prefix);

            if (_keysByPrefix.TryGetValue(prefix, out var keys))
            {
                var keysToRemove = keys.ToList();
                _logger.LogInformation("[MemoryCache] Se encontraron {Count} keys para el prefijo '{Prefix}': {Keys}", keysToRemove.Count, prefix, string.Join(", ", keysToRemove));

                foreach (var key in keysToRemove)
                {
                    _cache.Remove(key);
                    _logger.LogInformation("[MemoryCache] Eliminada key: {Key} del prefijo: {Prefix}", key, prefix);
                }
                _keysByPrefix.TryRemove(prefix, out _);
                _logger.LogInformation("[MemoryCache] Invalidacion completada. {Count} keys eliminadas para prefijo: {Prefix}", keysToRemove.Count, prefix);
            }
            else
            {
                _logger.LogWarning("[MemoryCache] NO se encontraron keys para el prefijo: {Prefix}", prefix);
                // Mostrar todos los prefixes registrados para debug
                _logger.LogWarning("[MemoryCache] Prefixes registrados: {Prefixes}", string.Join(", ", _keysByPrefix.Keys));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MemoryCache] INVALIDACION FALLO para prefijo {Prefix}. Error: {Error}", prefix, ex.Message);
        }
    }

    /// <summary>
    /// Registra una clave bajo todos sus prefixes posibles para permitir invalidación por prefijo.
    /// </summary>
    /// <param name="key">Clave a registrar.</param>
    private void TrackKeyByPrefixes(string key)
    {
        // Registrar la key bajo todos sus prefixes posibles
        // Para "quizzes:public:search:1:10", registrar prefixes:
        // "quizzes:public:", "quizzes:", "quizzes"
        var parts = key.Split(':');
        for (int i = 1; i <= parts.Length; i++)
        {
            var prefix = string.Join(":", parts.Take(i)) + ":";
            _keysByPrefix.AddOrUpdate(
                prefix,
                _ => new HashSet<string> { key },
                (_, set) => { set.Add(key); return set; }
            );
            _logger.LogDebug("[MemoryCache] Registrando key '{Key}' bajo prefijo: '{Prefix}'", key, prefix);
        }
    }

    /// <summary>
    /// Elimina una clave del tracking de prefixes.
    /// </summary>
    /// <param name="key">Clave a eliminar del tracking.</param>
    private void RemoveKeyFromPrefixTracking(string key)
    {
        var parts = key.Split(':');
        for (int i = 1; i <= parts.Length; i++)
        {
            var prefix = string.Join(":", parts.Take(i)) + ":";
            if (_keysByPrefix.TryGetValue(prefix, out var set))
            {
                set.Remove(key);
                _logger.LogDebug("[MemoryCache] Eliminando key '{Key}' del tracking del prefijo: '{Prefix}'", key, prefix);
            }
        }
    }
}