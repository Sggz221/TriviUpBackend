using System.Security.Claims;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Services;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Controllers;

/// <summary>
/// Banco personal de preguntas: preguntas del usuario sin cuestionario, con categoría y dificultad.
/// </summary>
[ApiController]
[Route("api/banco-preguntas")]
[Produces("application/json")]
[Authorize]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class BancoPreguntasController(IBancoPreguntaService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(BancoPreguntaListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? q,
        [FromQuery] long? categoriaId,
        [FromQuery] bool sinCategoria = false,
        [FromQuery] string? dificultad = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Usuario no autenticado" });

        var result = await service.ListAsync(userId.Value, q, categoriaId, sinCategoria, dificultad, page, pageSize);
        return result.Match(Ok, HandleError);
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(BancoPreguntaResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(long id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Usuario no autenticado" });

        var result = await service.GetByIdAsync(id, userId.Value);
        return result.Match(Ok, HandleError);
    }

    [HttpPost]
    [ProducesResponseType(typeof(BancoPreguntaResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] BancoPreguntaRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Usuario no autenticado" });

        var result = await service.CreateAsync(request, userId.Value);
        return result.Match(
            response => CreatedAtAction(nameof(GetById), new { id = response.Id }, response),
            HandleError);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(BancoPreguntaResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(long id, [FromBody] BancoPreguntaRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Usuario no autenticado" });

        var result = await service.UpdateAsync(id, request, userId.Value);
        return result.Match(Ok, HandleError);
    }

    /// <summary>Mueve varias preguntas a una categoría, o las deja sin categoría si categoriaId es null.</summary>
    [HttpPost("asignar-categoria")]
    [ProducesResponseType(typeof(AsignarCategoriaResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AsignarCategoria([FromBody] AsignarCategoriaRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Usuario no autenticado" });

        var result = await service.AsignarCategoriaAsync(request, userId.Value);
        return result.Match(Ok, HandleError);
    }

    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Usuario no autenticado" });

        var result = await service.DeleteAsync(id, userId.Value);
        return result.IsSuccess ? NoContent() : HandleError(result.Error);
    }

    private long? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && long.TryParse(claim.Value, out var id) ? id : null;
    }

    private IActionResult HandleError(QuizError error) => error switch
    {
        QuizNotFoundError e => NotFound(new { message = e.Error }),
        QuizValidationError e => BadRequest(new { message = e.Error }),
        QuizForbiddenError e => StatusCode(403, new { message = e.Error }),
        _ => StatusCode(500, new { message = error.Error })
    };
}
