using Microsoft.EntityFrameworkCore;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Database;

namespace TriviUpBackend.Cuestionarios.Repositories;

/// <inheritdoc />
public class BancoCategoriaRepository(Context context) : IBancoCategoriaRepository
{
    public async Task<BancoCategoria?> FindByIdAsync(long id) =>
        await context.BancoCategorias.FirstOrDefaultAsync(c => c.Id == id);

    public async Task<BancoCategoria?> FindByNombreAsync(long creatorId, string nombre)
    {
        var buscado = nombre.Trim().ToLower();
        return await context.BancoCategorias
            .FirstOrDefaultAsync(c => c.CreatorId == creatorId && c.Nombre.ToLower() == buscado);
    }

    public async Task<List<(BancoCategoria Categoria, int Total)>> FindByCreatorWithCountsAsync(long creatorId)
    {
        var categorias = await context.BancoCategorias
            .Where(c => c.CreatorId == creatorId)
            .OrderBy(c => c.Nombre)
            .ToListAsync();

        var conteos = await context.BancoPreguntas
            .Where(p => p.CreatorId == creatorId && p.CategoriaId != null)
            .GroupBy(p => p.CategoriaId!.Value)
            .Select(g => new { CategoriaId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.CategoriaId, x => x.Total);

        return categorias
            .Select(c => (c, conteos.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<int> CountSinCategoriaAsync(long creatorId) =>
        await context.BancoPreguntas.CountAsync(p => p.CreatorId == creatorId && p.CategoriaId == null);

    public async Task<BancoCategoria> AddAsync(BancoCategoria categoria)
    {
        context.BancoCategorias.Add(categoria);
        await context.SaveChangesAsync();
        return categoria;
    }

    public async Task<BancoCategoria> UpdateAsync(BancoCategoria categoria)
    {
        context.BancoCategorias.Update(categoria);
        await context.SaveChangesAsync();
        return categoria;
    }

    public async Task DeleteAsync(BancoCategoria categoria)
    {
        // Se desasignan a mano para no depender de que el proveedor aplique el SetNull
        var preguntas = await context.BancoPreguntas.Where(p => p.CategoriaId == categoria.Id).ToListAsync();
        foreach (var pregunta in preguntas)
        {
            pregunta.CategoriaId = null;
        }

        context.BancoCategorias.Remove(categoria);
        await context.SaveChangesAsync();
    }
}
