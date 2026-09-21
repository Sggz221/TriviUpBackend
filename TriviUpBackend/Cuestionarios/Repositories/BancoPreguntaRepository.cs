using Microsoft.EntityFrameworkCore;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Database;

namespace TriviUpBackend.Cuestionarios.Repositories;

/// <inheritdoc />
public class BancoPreguntaRepository(Context context) : IBancoPreguntaRepository
{
    public async Task<BancoPregunta?> FindByIdAsync(long id) =>
        await context.BancoPreguntas.FirstOrDefaultAsync(p => p.Id == id);

    public async Task<(List<BancoPregunta> Items, int Total)> FindByCreatorAsync(
        long creatorId, string? search, string? etiqueta, int page, int pageSize)
    {
        var query = context.BancoPreguntas.Where(p => p.CreatorId == creatorId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p => p.Enunciado.ToLower().Contains(term) || p.EtiquetasTexto.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(etiqueta))
        {
            var token = BancoPregunta.TagToken(etiqueta.Trim().ToLower());
            query = query.Where(p => p.EtiquetasTexto.Contains(token));
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

    public async Task<List<string>> FindEtiquetasTextosAsync(long creatorId) =>
        await context.BancoPreguntas
            .Where(p => p.CreatorId == creatorId && p.EtiquetasTexto != string.Empty)
            .Select(p => p.EtiquetasTexto)
            .ToListAsync();

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

    public async Task DeleteAsync(BancoPregunta pregunta)
    {
        context.BancoPreguntas.Remove(pregunta);
        await context.SaveChangesAsync();
    }

}
