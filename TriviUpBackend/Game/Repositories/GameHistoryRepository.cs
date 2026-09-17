using TriviUpBackend.Database;
using TriviUpBackend.Game.Models;
using Microsoft.EntityFrameworkCore;

namespace TriviUpBackend.Game.Repositories;

/// <summary>
/// Interfaz del repositorio de historial de partidas.
/// </summary>
public interface IGameHistoryRepository
{
    /// <summary>
    /// Añade un nuevo registro de historial de partida.
    /// </summary>
    /// <param name="gameHistory">Historial a guardar.</param>
    Task AddAsync(GameHistory gameHistory);

    /// <summary>
    /// Obtiene el historial de partidas de un usuario.
    /// </summary>
    /// <param name="userId">ID del usuario.</param>
    /// <returns>Lista de historiales del usuario ordenados por fecha.</returns>
    Task<List<GameHistory>> GetByUserIdAsync(long userId);

    /// <summary>
    /// Obtiene un historial de partida por su ID.
    /// </summary>
    /// <param name="gameId">ID único de la partida.</param>
    /// <returns>Historial encontrado o null.</returns>
    Task<GameHistory?> GetByIdAsync(long gameId);
}

/// <summary>
/// Implementación del repositorio de historial de partidas.
/// </summary>
public class GameHistoryRepository(Context context) : IGameHistoryRepository
{
    private readonly Context _context = context;

    public async Task AddAsync(GameHistory gameHistory)
    {
        await _context.GameHistories.AddAsync(gameHistory);
        await _context.SaveChangesAsync();
    }

    public async Task<List<GameHistory>> GetByUserIdAsync(long userId)
    {
        return await _context.GameHistories
            .Where(g => g.OwnerId == userId || g.PlayerResultsJson.Contains($"\"UserId\":{userId}"))
            .OrderByDescending(g => g.EndedAt)
            .ToListAsync();
    }

    public async Task<GameHistory?> GetByIdAsync(long gameId)
    {
        return await _context.GameHistories
            .FirstOrDefaultAsync(g => g.GameId == gameId);
    }
}
