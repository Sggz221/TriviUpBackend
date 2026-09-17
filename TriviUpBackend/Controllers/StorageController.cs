using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using IWebHostEnvironment = Microsoft.AspNetCore.Hosting.IWebHostEnvironment;

namespace TriviUpBackend.Controllers;

/// <summary>
/// Controlador para servir archivos estáticos de la carpeta uploads.
/// Proporciona acceso a fotos de perfil e imágenes de preguntas.
/// </summary>
[ApiController]
[Route("[controller]")]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class StorageController(
    IWebHostEnvironment env,
    ILogger<StorageController> logger
) : ControllerBase
{
    /// <summary>
    /// Sirve archivos de la carpeta uploads.
    /// Ejemplo: GET /storage/profile-photos/archivo.png
    /// </summary>
    /// <param name="path">Ruta del archivo dentro de la carpeta uploads.</param>
    /// <returns>Archivo solicitado con el tipo de contenido apropiado.</returns>
    [HttpGet("{**path}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetFile(string path)
    {
        logger.LogInformation("[StorageController] Intentando servir archivo: {Path}", path);

        var uploadsPath = Path.Combine(env.ContentRootPath, "uploads");
        var fullPath = Path.GetFullPath(Path.Combine(uploadsPath, path));

        // Seguridad: asegurar que el path está dentro de uploads
        if (!fullPath.StartsWith(Path.GetFullPath(uploadsPath)))
        {
            logger.LogWarning("[StorageController] Intento de acceso fuera de uploads: {Path}", path);
            return NotFound();
        }

        if (!System.IO.File.Exists(fullPath))
        {
            logger.LogWarning("[StorageController] Archivo no encontrado: {Path}", fullPath);
            return NotFound(new { message = "Archivo no encontrado" });
        }

        var contentType = GetContentType(fullPath);
        logger.LogInformation("[StorageController] Sirviendo archivo: {Path}, ContentType: {Type}", fullPath, contentType);

        return PhysicalFile(fullPath, contentType);
    }

    /// <summary>
    /// Determina el tipo de contenido MIME según la extensión del archivo.
    /// </summary>
    /// <param name="path">Ruta o nombre del archivo.</param>
    /// <returns>Tipo de contenido MIME correspondiente.</returns>
    private static string GetContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}