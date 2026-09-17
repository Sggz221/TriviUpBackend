using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Services;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Controllers;

/// <summary>
/// Controlador de quizzes públicos.
/// Proporciona endpoints públicos para consultar, dar like y rastrear visitas de quizzes.
/// </summary>
[ApiController]
[Route("api/quizzes")]
[Produces("application/json")]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class QuizzesController(
    IQuizService quizService,
    ILogger<QuizzesController> logger
) : ControllerBase
{
    /// <summary>
    /// Obtiene una lista paginada de cuestionarios públicos con filtros y ordenación
    /// </summary>
    /// <param name="search">Texto de búsqueda por título (opcional, se aplica trim y Contains)</param>
    /// <param name="page">Número de página (default: 1)</param>
    /// <param name="pageSize">Tamaño de página (default: 20)</param>
    /// <returns>Lista de cuestionarios públicos con conteo total</returns>
    [HttpGet("public")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublicQuizzesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPublicQuizzes(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        logger.LogInformation("GetPublicQuizzes - Search: {Search}, Page: {Page}, PageSize: {PageSize}",
            search, page, pageSize);

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var result = await quizService.GetPublicQuizzesAsync(search, page, pageSize);

        return result.Match(
            response => Ok(new PublicQuizzesResponse
            {
                Quizzes = response.Quizzes,
                TotalCount = response.TotalCount,
                Page = page,
                PageSize = pageSize
            }),
            error => HandleError(error)
        );
    }

    /// <summary>
    /// Incrementa el contador de likes de un cuestionario público
    /// </summary>
    /// <param name="id">ID del cuestionario</param>
    /// <returns>Nuevo conteo de likes</returns>
    [HttpPost("{id:long}/like")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LikeQuiz(long id)
    {
        logger.LogInformation("LikeQuiz - ID: {Id}", id);

        var result = await quizService.IncrementLikesAsync(id);

        return result.Match(
            count => Ok(new CountResponse { Count = count }),
            error => HandleError(error)
        );
    }

    /// <summary>
    /// Quita el like de un cuestionario público (decrementa el contador en 1)
    /// </summary>
    /// <param name="id">ID del cuestionario</param>
    /// <returns>Nuevo conteo de likes</returns>
    [HttpDelete("{id:long}/like")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlikeQuiz(long id)
    {
        logger.LogInformation("UnlikeQuiz - ID: {Id}", id);

        var result = await quizService.DecrementLikesAsync(id);

        return result.Match(
            count => Ok(new CountResponse { Count = count }),
            error => HandleError(error)
        );
    }

    /// <summary>
    /// Incrementa el contador de visitas de un cuestionario público
    /// </summary>
    /// <param name="id">ID del cuestionario</param>
    /// <returns>Nuevo conteo de visitas</returns>
    [HttpPost("{id:long}/visit")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VisitQuiz(long id)
    {
        logger.LogInformation("VisitQuiz - ID: {Id}", id);

        var result = await quizService.IncrementVisitasAsync(id);

        return result.Match(
            count => Ok(new CountResponse { Count = count }),
            error => HandleError(error)
        );
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
