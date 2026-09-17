using System.Security.Claims;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TriviUpBackend.Common.Storage;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Database;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Controllers;

/// <summary>
/// Controlador para gestionar imágenes asociadas a preguntas de quizzes.
/// Permite subir y eliminar imágenes de preguntas específicas.
/// </summary>
[ApiController]
[Route("api/cuestionarios/preguntas")]
[Produces("application/json")]
[Authorize]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class QuestionImagesController(
    IQuestionImageStorage questionImageStorage,
    IQuizRepository quizRepository,
    Context context,
    ILogger<QuestionImagesController> logger
) : ControllerBase
{
    /// <summary>
    /// Sube una imagen para una pregunta específica.
    /// Solo el creador del cuestionario puede subir imágenes a sus preguntas.
    /// </summary>
    /// <param name="preguntaId">ID único de la pregunta.</param>
    /// <param name="file">Archivo de imagen a subir (máximo 5MB, formatos permitidos: png, jpg, jpeg, gif, webp).</param>
    /// <returns>ID de la pregunta y URL relativa de la imagen subida.</returns>
    [HttpPut("{preguntaId:long}/imagen")]
    [ProducesResponseType(typeof(PreguntaImageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadImage(long preguntaId, IFormFile file)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized(new { message = "Usuario no autenticado" });
        }

        logger.LogInformation("Subiendo imagen para pregunta {PreguntaId} por usuario {UserId}", preguntaId, userId);

        // Verificar que la pregunta existe y el usuario tiene permisos
        var quizWithQuestion = await quizRepository.FindByQuestionIdAsync(preguntaId);
        if (quizWithQuestion == null)
        {
            return NotFound(new { message = "Pregunta no encontrada" });
        }

        var pregunta = quizWithQuestion.Preguntas.FirstOrDefault(p => p.Id == preguntaId);
        if (pregunta == null)
        {
            return NotFound(new { message = "Pregunta no encontrada" });
        }

        if (quizWithQuestion.CreatorId != userId.Value)
        {
            logger.LogWarning("Usuario {UserId} intentó subir imagen a pregunta {PreguntaId} creada por {CreatorId}",
                userId.Value, preguntaId, quizWithQuestion.CreatorId);
            return StatusCode(403, new { message = "No tienes permiso para modificar esta pregunta" });
        }

        // Si ya existe una imagen, eliminarla primero
        if (!string.IsNullOrEmpty(pregunta.ImagenUrl))
        {
            var deleteResult = await questionImageStorage.DeleteQuestionImageAsync(pregunta.ImagenUrl);
            if (deleteResult.IsFailure)
            {
                logger.LogWarning("Error eliminando imagen anterior de pregunta {PreguntaId}: {Error}",
                    preguntaId, deleteResult.Error);
            }
        }

        // Guardar la nueva imagen
        var saveResult = await questionImageStorage.SaveQuestionImageAsync(preguntaId, file);

        if (saveResult.IsFailure)
        {
            return BadRequest(new { message = saveResult.Error.Error });
        }

        // Guardar la ruta RELATIVA en la base de datos (no la URL completa)
        // El frontend concatenará la base URL cuando necesite mostrar la imagen
        pregunta.ImagenUrl = saveResult.Value;
        await context.SaveChangesAsync();

        // Devolver la ruta relativa para que el frontend la use
        // El frontend concatenará la base URL al mostrar la imagen
        return Ok(new PreguntaImageResponse
        {
            PreguntaId = preguntaId,
            ImagenUrl = saveResult.Value
        });
    }

    /// <summary>
    /// Elimina la imagen de una pregunta específica.
    /// Solo el creador del cuestionario puede eliminar imágenes de sus preguntas.
    /// </summary>
    /// <param name="preguntaId">ID único de la pregunta.</param>
    /// <returns>Sin contenido en caso de éxito.</returns>
    [HttpDelete("{preguntaId:long}/imagen")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteImage(long preguntaId)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized(new { message = "Usuario no autenticado" });
        }

        logger.LogInformation("Eliminando imagen de pregunta {PreguntaId} por usuario {UserId}", preguntaId, userId);

        // Verificar que la pregunta existe y el usuario tiene permisos
        var quizWithQuestion = await quizRepository.FindByQuestionIdAsync(preguntaId);
        if (quizWithQuestion == null)
        {
            return NotFound(new { message = "Pregunta no encontrada" });
        }

        var pregunta = quizWithQuestion.Preguntas.FirstOrDefault(p => p.Id == preguntaId);
        if (pregunta == null)
        {
            return NotFound(new { message = "Pregunta no encontrada" });
        }

        if (quizWithQuestion.CreatorId != userId.Value)
        {
            logger.LogWarning("Usuario {UserId} intentó eliminar imagen de pregunta {PreguntaId} creada por {CreatorId}",
                userId.Value, preguntaId, quizWithQuestion.CreatorId);
            return StatusCode(403, new { message = "No tienes permiso para modificar esta pregunta" });
        }

        if (string.IsNullOrEmpty(pregunta.ImagenUrl))
        {
            return NoContent();
        }

        var result = await questionImageStorage.DeleteQuestionImageAsync(pregunta.ImagenUrl);

        if (result.IsFailure)
        {
            logger.LogError("Error eliminando imagen de pregunta {PreguntaId}: {Error}", preguntaId, result.Error);
            return StatusCode(500, new { message = result.Error.Error });
        }

        // Limpiar la URL en la base de datos
        pregunta.ImagenUrl = null;
        await context.SaveChangesAsync();

        return NoContent();
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

public record PreguntaImageResponse
{
    public long PreguntaId { get; init; }
    public string ImagenUrl { get; init; } = string.Empty;
}