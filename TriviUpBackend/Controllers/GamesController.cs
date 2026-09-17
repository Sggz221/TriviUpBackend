using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TriviUpBackend.Game.DTOs;
using TriviUpBackend.Game.Repositories;

namespace TriviUpBackend.Game.Controllers;

/// <summary>
/// Controlador para gestionar el historial de partidas jugadas.
/// </summary>
[ApiController]
[Route("api/game")]
[Produces("application/json")]
[Authorize]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class GamesController(
    IGameHistoryRepository gameHistoryRepository,
    ILogger<GamesController> logger
) : ControllerBase
{
    /// <summary>
    /// Obtiene el historial de partidas del usuario autenticado.
    /// </summary>
    /// <returns>Lista de historiales de partidas jugadas por el usuario.</returns>
    [HttpGet("history")]
    [ProducesResponseType(typeof(List<GameHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !long.TryParse(userIdClaim.Value, out var userId))
        {
            return Unauthorized(new { message = "Invalid user token" });
        }

        logger.LogInformation("Getting game history for user {UserId}", userId);

        var histories = await gameHistoryRepository.GetByUserIdAsync(userId);

        var dtos = histories.Select(h => new GameHistoryDto(
            h.GameId,
            h.QuizId,
            h.QuizTitle,
            h.StartedAt,
            h.EndedAt,
            h.OwnerId,
            h.PlayerResults.Select(p => new HistoryPlayerResultDto(
                p.UserId,
                p.Username,
                p.FinalScore,
                p.CorrectAnswers,
                p.WrongAnswers,
                p.Rank
            )).ToList()
        )).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Obtiene el historial de una partida específica por su ID.
    /// </summary>
    /// <param name="gameId">ID único de la partida.</param>
    /// <returns>Historial completo de la partida con resultados de todos los jugadores.</returns>
    [HttpGet("{gameId:long}")]
    [ProducesResponseType(typeof(GameHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long gameId)
    {
        logger.LogInformation("Getting game history for game {GameId}", gameId);

        var history = await gameHistoryRepository.GetByIdAsync(gameId);
        if (history == null)
        {
            return NotFound(new { message = "Game not found" });
        }

        var dto = new GameHistoryDto(
            history.GameId,
            history.QuizId,
            history.QuizTitle,
            history.StartedAt,
            history.EndedAt,
            history.OwnerId,
            history.PlayerResults.Select(p => new HistoryPlayerResultDto(
                p.UserId,
                p.Username,
                p.FinalScore,
                p.CorrectAnswers,
                p.WrongAnswers,
                p.Rank
            )).ToList()
        );

        return Ok(dto);
    }
}
