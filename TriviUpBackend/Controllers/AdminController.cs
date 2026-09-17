using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TriviUpBackend.Database;
using TriviUpBackend.Game.DTOs;

namespace TriviUpBackend.Controllers;

/// <summary>
/// Controlador de administración.
/// Proporciona endpoints exclusivos para administradores del sistema.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "ADMIN")]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class AdminController : ControllerBase
{
    private readonly Context _context;
    private readonly ILogger<AdminController> _logger;

    /// <summary>
    /// Constructor del controlador de administración.
    /// </summary>
    /// <param name="context">Contexto de la base de datos.</param>
    /// <param name="logger">Logger para mensajes de diagnóstico.</param>
    public AdminController(Context context, ILogger<AdminController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Obtiene estadísticas generales del sistema.
    /// Incluye total de partidas, quizzes, usuarios, usuarios activos y rankings.
    /// </summary>
    /// <returns>Estadísticas del sistema.</returns>
    [HttpGet("stats")]
    public async Task<ActionResult<AdminStatsDto>> GetStats()
    {
        _logger.LogInformation("[AdminController] Obteniendo estadísticas sin cache");

        // Total games played (from GameHistories)
        var totalGames = await _context.GameHistories.CountAsync();

        // Total quizzes
        var totalQuizzes = await _context.Quizzes.CountAsync();

        // Total users
        var totalUsers = await _context.Users.CountAsync(u => !u.IsDeleted);

        // Active users in last 7 days (users who logged in)
        var activeUsersThreshold = DateTime.UtcNow.AddDays(-7);
        var activeUsersLast24h = await _context.Users
            .Where(u => !u.IsDeleted && u.LastLoginAt >= activeUsersThreshold)
            .CountAsync();

        // Quiz with most favorites
        var mostFavoritesQuiz = await _context.Quizzes
            .OrderByDescending(q => q.Likes)
            .Select(q => new QuizWithMostFavoritesDto(q.Id, q.Nombre, q.Likes))
            .FirstOrDefaultAsync();

        // Quiz with most visits
        var mostVisitsQuiz = await _context.Quizzes
            .OrderByDescending(q => q.Visitas)
            .Select(q => new QuizWithMostVisitsDto(q.Id, q.Nombre, q.Visitas))
            .FirstOrDefaultAsync();

        // Games per day (last 7 days)
        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
        var gamesPerDay = await _context.GameHistories
            .Where(g => g.EndedAt >= sevenDaysAgo)
            .GroupBy(g => g.EndedAt.Date)
            .Select(g => new DailyGamesDto(g.Key, g.Count()))
            .ToListAsync();

        // Active users per day (last 7 days) - based on LastLoginAt
        var activeUsersPerDay = await _context.Users
            .Where(u => !u.IsDeleted && u.LastLoginAt >= sevenDaysAgo)
            .GroupBy(u => u.LastLoginAt!.Value.Date)
            .Select(g => new ActiveUsersDto(g.Key, g.Count()))
            .ToListAsync();

        var stats = new AdminStatsDto(
            totalGames,
            totalQuizzes,
            totalUsers,
            activeUsersLast24h,
            mostFavoritesQuiz,
            mostVisitsQuiz,
            gamesPerDay,
            activeUsersPerDay
        );

        _logger.LogInformation("[AdminController] Estadísticas obtenidas: {TotalGames} juegos, {TotalQuizzes} quizzes, {TotalUsers} usuarios",
            totalGames, totalQuizzes, totalUsers);

        return Ok(stats);
    }
}
