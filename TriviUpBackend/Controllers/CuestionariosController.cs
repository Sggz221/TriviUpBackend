using System.Security.Claims;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Services;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Controllers;

/// <summary>
/// Controlador de cuestionarios (quizzes).
/// Gestiona la creación, consulta, actualización y eliminación de quizzes.
/// </summary>
[ApiController]
[Route("api/cuestionarios")]
[Produces("application/json")]
[Authorize]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class CuestionariosController(
    IQuizService quizService,
    ILogger<CuestionariosController> logger
) : ControllerBase
{
    /// <summary>
    /// Crea un nuevo cuestionario.
    /// </summary>
    /// <param name="request">Datos del cuestionario a crear (nombre, descripción, preguntas, visibilidad, categoría).</param>
    /// <returns>El cuestionario creado con su ID y código de juego.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(QuizResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create([FromBody] CreateQuizRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized(new { message = "Usuario no autenticado" });
        }

        logger.LogInformation("Crear request de cuestionario con userId: {UserId}", userId);

        var result = await quizService.CreateAsync(request, userId.Value);

        return result.Match(
            response => CreatedAtAction(nameof(GetById), new { id = response.Id }, response),
            error => HandleError(error)
        );
    }

    /// <summary>
    /// Obtiene una lista paginada de cuestionarios públicos.
    /// </summary>
    /// <param name="page">Número de página (por defecto 1).</param>
    /// <param name="pageSize">Cantidad de resultados por página (por defecto 10).</param>
    /// <returns>Lista de cuestionarios con información de paginación.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(QuizListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        logger.LogInformation("Get all quizzes - Page: {Page}, PageSize: {PageSize}", page, pageSize);

        var result = await quizService.GetAllAsync(page, pageSize);

        return result.Match(
            response => Ok(new
            {
                quizzes = response.Quizzes,
                totalCount = response.TotalCount,
                page,
                pageSize
            }),
            error => HandleError(error)
        );
    }

    /// <summary>
    /// Obtiene un cuestionario por su ID.
    /// Este endpoint es público (no requiere autenticación).
    /// </summary>
    /// <param name="id">ID único del cuestionario.</param>
    /// <returns>Información completa del cuestionario.</returns>
    [HttpGet("{id:long}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(QuizResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id)
    {
        logger.LogInformation("Get quiz by ID: {Id}", id);

        var result = await quizService.GetByIdAsync(id);

        return result.Match(
            response => Ok(response),
            error => HandleError(error)
        );
    }

    /// <summary>
    /// Obtiene un cuestionario por su código de juego.
    /// Este endpoint es público (no requiere autenticación).
    /// </summary>
    /// <param name="gameCode">Código de juego único del cuestionario.</param>
    /// <returns>Información completa del cuestionario.</returns>
    [HttpGet("gamecode/{gameCode}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(QuizResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByGameCode(string gameCode)
    {
        logger.LogInformation("Get quiz by GameCode: {GameCode}", gameCode);

        var result = await quizService.GetByGameCodeAsync(gameCode);

        return result.Match(
            response => Ok(response),
            error => HandleError(error)
        );
    }

    /// <summary>
    /// Obtiene los cuestionarios creados por el usuario autenticado.
    /// </summary>
    /// <returns>Lista de cuestionarios del usuario.</returns>
    [HttpGet("mis-cuestionarios")]
    [ProducesResponseType(typeof(List<QuizResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyQuizzes()
    {
        logger.LogInformation("GetMyQuizzes - User claims: {Claims}",
            User.Claims.Select(c => $"{c.Type}={c.Value}").ToList());
        var userId = GetCurrentUserId();
        logger.LogInformation("GetMyQuizzes - Extracted userId: {UserId}", userId);

        if (userId == null)
        {
            return Unauthorized(new { message = "Usuario no autenticado" });
        }

        logger.LogInformation("Get my quizzes for user: {UserId}", userId);

        var result = await quizService.GetByCreatorIdAsync(userId.Value);

        return result.Match(
            response => Ok(response),
            error => HandleError(error)
        );
    }

    /// <summary>
    /// Actualiza un cuestionario existente.
    /// Solo el creador del cuestionario puede actualizarlo.
    /// </summary>
    /// <param name="id">ID del cuestionario a actualizar.</param>
    /// <param name="request">Campos a actualizar (nombre, descripción, preguntas, visibilidad, categoría).</param>
    /// <returns>El cuestionario actualizado.</returns>
    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(QuizResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateQuizRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized(new { message = "Usuario no autenticado" });
        }

        logger.LogInformation("Update quiz {Id} by user: {UserId}", id, userId);

        var result = await quizService.UpdateAsync(id, request, userId.Value);

        return result.Match(
            response => Ok(response),
            error => HandleError(error)
        );
    }

    /// <summary>
    /// Elimina un cuestionario.
    /// Solo el creador del cuestionario puede eliminarlo.
    /// </summary>
    /// <param name="id">ID del cuestionario a eliminar.</param>
    /// <returns>Sin contenido en caso de éxito.</returns>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized(new { message = "Usuario no autenticado" });
        }

        logger.LogInformation("Delete quiz {Id} by user: {UserId}", id, userId);

        var result = await quizService.DeleteAsync(id, userId.Value);

        if (result.IsSuccess)
        {
            return NoContent();
        }

        return HandleError(result.Error);
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

    /// <summary>
    /// Maneja los errores de tipo QuizError y los convierte en respuestas HTTP apropiadas.
    /// </summary>
    /// <param name="error">Error producido durante la operación del quiz.</param>
    /// <returns>Respuesta HTTP con el código y mensaje de error correspondiente.</returns>
    private IActionResult HandleError(QuizError error)
    {
        return error switch
        {
            QuizNotFoundError notFoundError => NotFound(new { message = notFoundError.Error }),
            QuizValidationError validationError => BadRequest(new { message = validationError.Error }),
            QuizForbiddenError forbiddenError => StatusCode(403, new { message = forbiddenError.Error }),
            _ => StatusCode(500, new { message = error.Error })
        };
    }
}
