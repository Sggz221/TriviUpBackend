using Microsoft.EntityFrameworkCore;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Database;

namespace TriviUpBackend.Cuestionarios.Repositories;

/// <inheritdoc />
public class BancoPreguntaRepository(Context context) : IBancoPreguntaRepository
{
    public async Task<BancoPregunta?> FindByIdAsync(long id) =>
        await context.BancoPreguntas.Include(p => p.Categoria).FirstOrDefaultAsync(p => p.Id == id);

    public async Task<List<BancoPregunta>> FindByIdsAsync(long creatorId, IReadOnlyCollection<long> ids) =>
        await context.BancoPreguntas
            .Include(p => p.Categoria)
            .Where(p => p.CreatorId == creatorId && ids.Contains(p.Id))
            .ToListAsync();

    public async Task<(List<BancoPregunta> Items, int Total)> FindByCreatorAsync(
        long creatorId, string? search, long? categoriaId, bool sinCategoria, string? dificultad, int page, int pageSize)
    {
        var query = context.BancoPreguntas.Include(p => p.Categoria).Where(p => p.CreatorId == creatorId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p => p.Enunciado.ToLower().Contains(term) ||
                                     (p.Categoria != null && p.Categoria.Nombre.ToLower().Contains(term)));
        }

        if (sinCategoria)
        {
            query = query.Where(p => p.CategoriaId == null);
        }
        else if (categoriaId.HasValue)
        {
            query = query.Where(p => p.CategoriaId == categoriaId.Value);
        }

        if (!string.IsNullOrWhiteSpace(dificultad))
        {
            var normalizada = Dificultades.Normalizar(dificultad);
            query = query.Where(p => p.Dificultad == normalizada);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.UpdatedAt)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<BancoPregunta> AddAsync(BancoPregunta pregunta)
    {
        context.BancoPreguntas.Add(pregunta);
        await context.SaveChangesAsync();
        return pregunta;
    }

    public async Task<BancoPregunta> UpdateAsync(BancoPregunta pregunta)
    {
        context.BancoPreguntas.Update(pregunta);
        await context.SaveChangesAsync();
        return pregunta;
    }

    public async Task UpdateRangeAsync(IEnumerable<BancoPregunta> preguntas)
    {
        context.BancoPreguntas.UpdateRange(preguntas);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(BancoPregunta pregunta)
    {
        context.BancoPreguntas.Remove(pregunta);
        await context.SaveChangesAsync();
    }
}
