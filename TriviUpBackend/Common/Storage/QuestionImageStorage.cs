using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace TriviUpBackend.Common.Storage;

    /// <summary>
    /// Implementación del servicio de almacenamiento de imágenes de preguntas.
    /// Utiliza almacenamiento local en el sistema de archivos.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class QuestionImageStorage : IQuestionImageStorage
    {
        private readonly IStorage _storage;
        private readonly IConfiguration _configuration;
        private readonly ILogger<QuestionImageStorage> _logger;
        private readonly string _container = "question-images";
        private readonly string[] _allowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
        private readonly string[] _allowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
        private readonly long _maxFileSize = 5 * 1024 * 1024; // 5MB

        /// <summary>
        /// Constructor del servicio de almacenamiento de imágenes de preguntas.
        /// </summary>
        /// <param name="storage">Servicio de almacenamiento base.</param>
        /// <param name="configuration">Configuración de la aplicación.</param>
        /// <param name="logger">Logger para mensajes de diagnóstico.</param>
        public QuestionImageStorage(
            IStorage storage,
            IConfiguration configuration,
            ILogger<QuestionImageStorage> logger)
        {
            _storage = storage;
            _configuration = configuration;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<Result<string, QuestionImageStorageError>> SaveQuestionImageAsync(long questionId, IFormFile file)
        {
            // Validar archivo
            var validationError = ValidateFile(file);
            if (validationError is not null)
            {
                return Result.Failure<string, QuestionImageStorageError>(validationError);
            }

            // Generar nombre de archivo único: question_{id}_{guid}.{ext}
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var filename = $"question_{questionId}_{Guid.NewGuid():N}{extension}";

            try
            {
                // Guardar usando el storage existente
                var result = await _storage.SaveFileAsync(file, _container);

                if (result.IsFailure)
                {
                    _logger.LogError("Error guardando imagen de pregunta: {Error}", result.Error);
                    return Result.Failure<string, QuestionImageStorageError>(
                        new QuestionImageStorageError("Error guardando la imagen de pregunta"));
                }

                _logger.LogInformation("Imagen de pregunta guardada para pregunta {QuestionId}: {Path}", questionId, result.Value);
                return Result.Success<string, QuestionImageStorageError>(result.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error guardando imagen de pregunta para pregunta {QuestionId}", questionId);
                return Result.Failure<string, QuestionImageStorageError>(
                    new QuestionImageStorageError("Error guardando la imagen de pregunta"));
            }
        }

        /// <inheritdoc />
        public async Task<Result<bool, QuestionImageStorageError>> DeleteQuestionImageAsync(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl))
            {
                return Result.Success<bool, QuestionImageStorageError>(true);
            }

            try
            {
                var result = await _storage.DeleteFileAsync(imageUrl);

                if (result.IsFailure)
                {
                    _logger.LogWarning("Error eliminando imagen de pregunta: {Error}", result.Error);
                    return Result.Failure<bool, QuestionImageStorageError>(
                        new QuestionImageStorageError("Error eliminando la imagen de pregunta"));
                }

                _logger.LogInformation("Imagen de pregunta eliminada: {Path}", imageUrl);
                return Result.Success<bool, QuestionImageStorageError>(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error eliminando imagen de pregunta: {Path}", imageUrl);
                return Result.Failure<bool, QuestionImageStorageError>(
                    new QuestionImageStorageError("Error eliminando la imagen de pregunta"));
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
            // relativePath viene como "/uploads/question-images/archivo.png"
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
        private QuestionImageStorageError? ValidateFile(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            return new QuestionImageStorageError("Archivo vacío");
        }

        if (file.Length > _maxFileSize)
        {
            return new QuestionImageStorageError($"El archivo excede el tamaño máximo permitido de {_maxFileSize / 1024 / 1024}MB");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_allowedExtensions.Contains(extension))
        {
            return new QuestionImageStorageError($"Extensión no permitida. Solo se aceptan: {string.Join(", ", _allowedExtensions)}");
        }

        var contentType = file.ContentType?.ToLowerInvariant() ?? "";
        if (!_allowedContentTypes.Any(ct => contentType.Contains(ct.Split('/')[1])))
        {
            return new QuestionImageStorageError($"Tipo de contenido no permitido. Solo se aceptan: {string.Join(", ", _allowedContentTypes)}");
        }

        return null;
    }
}