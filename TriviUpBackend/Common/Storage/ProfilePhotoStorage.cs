using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace TriviUpBackend.Common.Storage;

    /// <summary>
    /// Implementación del servicio de almacenamiento de fotos de perfil.
    /// Utiliza almacenamiento local en el sistema de archivos.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class ProfilePhotoStorage : IProfilePhotoStorage
    {
        private readonly IStorage _storage;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ProfilePhotoStorage> _logger;
        private readonly string _container = "profile-photos";
        private readonly string[] _allowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
        private readonly string[] _allowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
        private readonly long _maxFileSize = 5 * 1024 * 1024; // 5MB

        /// <summary>
        /// Constructor del servicio de almacenamiento de fotos de perfil.
        /// </summary>
        /// <param name="storage">Servicio de almacenamiento base.</param>
        /// <param name="configuration">Configuración de la aplicación.</param>
        /// <param name="logger">Logger para mensajes de diagnóstico.</param>
        public ProfilePhotoStorage(
            IStorage storage,
            IConfiguration configuration,
            ILogger<ProfilePhotoStorage> logger)
        {
            _storage = storage;
            _configuration = configuration;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<Result<string, ProfilePhotoStorageError>> SaveProfilePhotoAsync(long userId, IFormFile file)
        {
            // Validar archivo
            var validationError = ValidateFile(file);
            if (validationError is not null)
            {
                return Result.Failure<string, ProfilePhotoStorageError>(validationError);
            }

            // Generar nombre de archivo único: user_{id}_{guid}.{ext}
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var filename = $"user_{userId}_{Guid.NewGuid():N}{extension}";

            try
            {
                // Guardar usando el storage existente
                var result = await _storage.SaveFileAsync(file, _container);

                if (result.IsFailure)
                {
                    _logger.LogError("Error guardando foto de perfil: {Error}", result.Error);
                    return Result.Failure<string, ProfilePhotoStorageError>(
                        new ProfilePhotoStorageError("Error guardando la foto de perfil"));
                }

                _logger.LogInformation("Foto de perfil guardada para usuario {UserId}: {Path}", userId, result.Value);
                return Result.Success<string, ProfilePhotoStorageError>(result.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error guardando foto de perfil para usuario {UserId}", userId);
                return Result.Failure<string, ProfilePhotoStorageError>(
                    new ProfilePhotoStorageError("Error guardando la foto de perfil"));
            }
        }

        /// <inheritdoc />
        public async Task<Result<bool, ProfilePhotoStorageError>> DeleteProfilePhotoAsync(string photoUrl)
        {
            if (string.IsNullOrEmpty(photoUrl))
            {
                return Result.Success<bool, ProfilePhotoStorageError>(true);
            }

            try
            {
                var result = await _storage.DeleteFileAsync(photoUrl);

                if (result.IsFailure)
                {
                    _logger.LogWarning("Error eliminando foto de perfil: {Error}", result.Error);
                    return Result.Failure<bool, ProfilePhotoStorageError>(
                        new ProfilePhotoStorageError("Error eliminando la foto de perfil"));
                }

                _logger.LogInformation("Foto de perfil eliminada: {Path}", photoUrl);
                return Result.Success<bool, ProfilePhotoStorageError>(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error eliminando foto de perfil: {Path}", photoUrl);
                return Result.Failure<bool, ProfilePhotoStorageError>(
                    new ProfilePhotoStorageError("Error eliminando la foto de perfil"));
            }
        }

        /// <inheritdoc />
        public string GetFullUrl(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return string.Empty;
            }

            // Si la ruta ya es absoluta, devolverla
            if (relativePath.StartsWith("http://") || relativePath.StartsWith("https://"))
            {
                return relativePath;
            }

            // Obtener la base URL completa desde la configuración (ej: http://localhost:5164)
            var baseUrl = _configuration["Storage:BaseUrl"] ?? "http://localhost:5164";

            // Construir URL usando el endpoint /storage/
            // relativePath viene como "/uploads/profile-photos/archivo.png"
            // necesitamos quitar el "/uploads" porque el StorageController sirve desde ahí
            var path = relativePath.TrimStart('/');
            if (path.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            {
                path = path["uploads/".Length..];
            }

            return $"{baseUrl.TrimEnd('/')}/storage/{path}";
        }

        /// <summary>
        /// Valida que el archivo cumple los requisitos de formato y tamaño.
        /// </summary>
        /// <param name="file">Archivo a validar.</param>
        /// <returns>Error de validación o null si es válido.</returns>
        private ProfilePhotoStorageError? ValidateFile(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            return new ProfilePhotoStorageError("Archivo vacío");
        }

        if (file.Length > _maxFileSize)
        {
            return new ProfilePhotoStorageError($"El archivo excede el tamaño máximo permitido de {_maxFileSize / 1024 / 1024}MB");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_allowedExtensions.Contains(extension))
        {
            return new ProfilePhotoStorageError($"Extensión no permitida. Solo se aceptan: {string.Join(", ", _allowedExtensions)}");
        }

        var contentType = file.ContentType?.ToLowerInvariant() ?? "";
        if (!_allowedContentTypes.Any(ct => contentType.Contains(ct.Split('/')[1])))
        {
            return new ProfilePhotoStorageError($"Tipo de contenido no permitido. Solo se aceptan: {string.Join(", ", _allowedContentTypes)}");
        }

        return null;
    }
}
