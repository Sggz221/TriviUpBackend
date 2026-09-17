using CSharpFunctionalExtensions;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Common.Storage;

    /// <summary>
    /// Implementación del servicio de almacenamiento de archivos.
    /// Gestiona la guardado y eliminación de archivos en el sistema de archivos local.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class Storage : IStorage
    {
        private readonly string _rootPath;
        private readonly string _uploadPath;
        private readonly long _maxFileSize;
        private readonly string[] _allowedExtensions;
        private readonly string[] _allowedContentTypes;
        private readonly ILogger<Storage> _logger;

        /// <summary>
        /// Constructor del servicio de almacenamiento.
        /// </summary>
        /// <param name="configuration">Configuración de la aplicación.</param>
        /// <param name="logger">Logger para mensajes de diagnóstico.</param>
        /// <param name="env">Entorno de hosting para determinar la ruta base.</param>
        public Storage(IConfiguration configuration, ILogger<Storage> logger, IWebHostEnvironment env)
        {
            _logger = logger;

            // Configuración desde appsettings.json (ruta relativa a wwwroot)
            _uploadPath = configuration["Storage:UploadPath"] ?? "uploads";
            _maxFileSize = configuration.GetValue<long>("Storage:MaxFileSize", 5 * 1024 * 1024);
            _allowedExtensions = configuration.GetSection("Storage:AllowedExtensions").Get<string[]>()
                                 ?? [".jpg", ".jpeg", ".png", ".gif", ".webp"];
            _allowedContentTypes = configuration.GetSection("Storage:AllowedContentTypes").Get<string[]>()
                                   ?? ["image/jpeg", "image/png", "image/gif", "image/webp"];

            // Ruta absoluta: usar WebRootPath si existe, si no usar ContentRootPath
            // En Docker, WebRootPath puede ser null si no hay wwwroot
            var basePath = env.WebRootPath ?? env.ContentRootPath;
            _rootPath = Path.Combine(basePath, _uploadPath);

            _logger.LogInformation("Storage service inicializado en: {Path}", _rootPath);
            _logger.LogInformation("WebRootPath: {WebRoot}, ContentRootPath: {ContentRoot}", env.WebRootPath, env.ContentRootPath);

            // Crear directorio si no existe
            if (!Directory.Exists(_rootPath))
            {
                _logger.LogInformation("Creando directorio de uploads: {Path}", _rootPath);
                Directory.CreateDirectory(_rootPath);
            }
        }

        /// <summary>
        /// Genera un nombre de archivo único con marca de tiempo y GUID.
        /// </summary>
        /// <param name="originalFilename">Nombre original del archivo.</param>
        /// <returns>Nombre de archivo único y seguro.</returns>
        private static string GenerateUniqueFilename(string originalFilename)
        {
            var extension = Path.GetExtension(originalFilename).ToLowerInvariant();
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var uniqueId = Guid.NewGuid().ToString("N")[..8];
            var sanitizedName = Path.GetFileNameWithoutExtension(originalFilename)
                .Replace(" ", "_")
                .Replace("-", "_");
            return $"{timestamp}_{uniqueId}_{sanitizedName}{extension}";
        }

        /// <summary>
        /// Valida que el archivo cumple los requisitos de formato, tamaño y nombre.
        /// </summary>
        /// <param name="file">Archivo a validar.</param>
        /// <returns>Resultado con error o éxito.</returns>
        private UnitResult<StorageError> ValidateFile(IFormFile file)
    {
        if (file is null or { Length: 0 })
        {
            return UnitResult.Failure<StorageError>(new StorageError("Archivo vacío"));
        }

        if (file.Length > _maxFileSize)
        {
            return UnitResult.Failure<StorageError>(
                new StorageError($"Archivo demasiado grande. Tamaño máximo: {_maxFileSize / 1024 / 1024}MB"));
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_allowedExtensions.Contains(extension))
        {
            return UnitResult.Failure<StorageError>(
                new StorageError($"Extensión no permitida: {extension}"));
        }

        var contentType = file.ContentType?.ToLowerInvariant();
        if (contentType == null || !_allowedContentTypes.Any(ct => contentType.Contains(ct.Split('/')[1])))
        {
            return UnitResult.Failure<StorageError>(
                new StorageError($"Tipo de contenido no permitido: {contentType}"));
        }

        var filename = Path.GetFileName(file.FileName);
        if (filename.Contains("..") || filename.Contains('/') || filename.Contains('\\'))
        {
            return UnitResult.Failure<StorageError>(new StorageError("Nombre de archivo no válido"));
        }

        return UnitResult.Success<StorageError>();
    }

    /// <inheritdoc />
    public async Task<Result<string, StorageError>> SaveFileAsync(IFormFile file, string folder)
    {
        var validation = ValidateFile(file);
        if (validation.IsFailure)
        {
            return Result.Failure<string, StorageError>(validation.Error);
        }

        try
        {
            // Generar nombre único
            var filename = GenerateUniqueFilename(file.FileName);

            // Crear directorio destino
            var folderPath = Path.Combine(_rootPath, folder);
            Directory.CreateDirectory(folderPath);

            // Guardar fichero
            var filePath = Path.Combine(folderPath, filename);
            var relativePath = GetRelativePath(filename, folder);

            using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);

            _logger.LogInformation("Archivo guardado: {Path}", relativePath);

            return Result.Success<string, StorageError>(relativePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error guardando archivo");
            return Result.Failure<string, StorageError>(
                new StorageError("Error guardando archivo"));
        }
    }

    /// <inheritdoc />
    public Task<Result<bool, StorageError>> DeleteFileAsync(string filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return Task.FromResult(Result.Success<bool, StorageError>(true));
        }

        try
        {
            var fullPath = GetFullPath(filename);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("Archivo eliminado: {Filename}", filename);
            }

            return Task.FromResult(Result.Success<bool, StorageError>(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error eliminando archivo {Filename}", filename);
            return Task.FromResult(Result.Failure<bool, StorageError>(
                new StorageError("Error eliminando archivo")));
        }
    }

    /// <inheritdoc />
    public bool FileExists(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return false;

        var fullPath = GetFullPath(filename);
        return File.Exists(fullPath);
    }

    /// <inheritdoc />
    public string GetFullPath(string filename)
    {
        if (Path.IsPathRooted(filename))
            return filename;

        var cleanFilename = filename;
        var prefix = $"/{_uploadPath}/";

        if (filename.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
            cleanFilename = filename["/storage/".Length..];
        else if (filename.StartsWith("/storage", StringComparison.OrdinalIgnoreCase))
            cleanFilename = filename["/storage".Length..].TrimStart('/');
        else if (filename.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            cleanFilename = filename[prefix.Length..];

        return Path.Combine(_rootPath, cleanFilename);
    }

    /// <inheritdoc />
    public string GetRelativePath(string filename, string folder = "products")
    {
        return $"/{_uploadPath}/{folder}/{filename}";
    }
}
