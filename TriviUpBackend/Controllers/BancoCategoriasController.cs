using System.Security.Claims;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Services;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Controllers;

/// <summary>
/// Categorías del banco personal de preguntas.
/// </summary>
[ApiController]
[Route("api/banco-categorias")]
[Produces("application/json")]
[Authorize]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class BancoCategoriasController(IBancoCategoriaService service) : ControllerBase
{
    /// <summary>Categorías del usuario con su número de preguntas y cuántas preguntas no tienen categoría.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(BancoCategoriasResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Usuario no autenticado" });

        var result = await service.ListAsync(userId.Value);
        return result.Match(Ok, HandleError);
    }

    [HttpPost]
    [ProducesResponseType(typeof(BancoCategoriaResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] BancoCategoriaRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Usuario no autenticado" });

        var result = await service.CreateAsync(request, userId.Value);
        return result.Match(
            response => Created($"api/banco-categorias/{response.Id}", response),
            HandleError);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(BancoCategoriaResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Rename(long id, [FromBody] BancoCategoriaRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Usuario no autenticado" });

        var result = await service.RenameAsync(id, request, userId.Value);
        return result.Match(Ok, HandleError);
    }

    /// <summary>Borra la categoría; sus preguntas se conservan y quedan sin categoría.</summary>
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
