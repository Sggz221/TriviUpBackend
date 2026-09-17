using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TriviUpBackend.Common.Storage;

namespace TriviUpBackend.Controllers;

/// <summary>
/// Controlador para gestionar imágenes asociadas a quizzes.
/// Permite subir imágenes que se utilizarán en las preguntas de un cuestionario.
/// </summary>
[ApiController]
[Route("api/cuestionarios/imagenes")]
[Produces("application/json")]
[Authorize]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class QuizImagesController(
    IQuestionImageStorage questionImageStorage,
    ILogger<QuizImagesController> logger
) : ControllerBase
{
    /// <summary>
    /// Sube una imagen de pregunta sin asociarla a una pregunta específica.
    /// Útil cuando se crea un quiz nuevo y las preguntas aún no tienen ID.
    /// </summary>
    /// <param name="file">Archivo de imagen a subir (máximo 5MB, formatos permitidos: png, jpg, jpeg, gif, webp).</param>
    /// <returns>Ruta relativa y URL completa de la imagen subida.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(QuizImageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized(new { message = "Usuario no autenticado" });
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No se proporcionó ningún archivo" });
        }

        logger.LogInformation("Subiendo imagen de quiz por usuario {UserId}", userId);

        // Usar questionId = 0 ya que no tenemos pregunta todavía
        // El ID solo se usa para generar el nombre del archivo
        var result = await questionImageStorage.SaveQuestionImageAsync(0, file);

        if (result.IsFailure)
        {
            return BadRequest(new { message = result.Error.Error });
        }

        var relativePath = result.Value;
        var fullUrl = questionImageStorage.GetFullUrl(relativePath);

        logger.LogInformation("Imagen de quiz guardada: {Path} por usuario {UserId}", relativePath, userId);

        return Ok(new QuizImageResponse
        {
            Path = relativePath,
            Url = fullUrl
        });
    }

    /// <summary>
    /// Extrae el ID del usuario autenticado desde los claims del token JWT.
    /// </summary>
    /// <returns>ID del usuario o null si no se encuentra.</returns>
    private long? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim != null && long.TryParse(userIdClaim.Value, out var userId))
        {
            return userId;
        }
        return null;
    }
}

public class QuizImageResponse
{
    public string Path { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}
