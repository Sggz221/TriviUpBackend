using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Cuestionarios.Services;

/// <inheritdoc />
public partial class BancoCategoriaService(
    IBancoCategoriaRepository repository,
    ILogger<BancoCategoriaService> logger
) : IBancoCategoriaService
{
    public async Task<Result<BancoCategoriasResponse, QuizError>> ListAsync(long userId)
    {
        var categorias = await repository.FindByCreatorWithCountsAsync(userId);
        var sinCategoria = await repository.CountSinCategoriaAsync(userId);

        return Result.Success<BancoCategoriasResponse, QuizError>(new BancoCategoriasResponse(
            categorias.Select(c => new BancoCategoriaResponse(c.Categoria.Id, c.Categoria.Nombre, c.Total)).ToList(),
            sinCategoria));
    }

    public async Task<Result<BancoCategoriaResponse, QuizError>> CreateAsync(BancoCategoriaRequest request, long userId)
    {
        var nombre = ValidarNombre(request.Nombre);
        if (nombre.IsFailure)
        {
            return Result.Failure<BancoCategoriaResponse, QuizError>(nombre.Error);
        }

        if (await repository.FindByNombreAsync(userId, nombre.Value) is not null)
        {
            return Result.Failure<BancoCategoriaResponse, QuizError>(
                new QuizValidationError($"Ya existe una categoría llamada \"{nombre.Value}\""));
        }

        var categoria = await repository.AddAsync(new BancoCategoria { CreatorId = userId, Nombre = nombre.Value });
        logger.LogInformation("Categoría {Id} creada por el usuario {UserId}", categoria.Id, userId);

        return Result.Success<BancoCategoriaResponse, QuizError>(new BancoCategoriaResponse(categoria.Id, categoria.Nombre, 0));
    }

    public async Task<Result<BancoCategoriaResponse, QuizError>> RenameAsync(long id, BancoCategoriaRequest request, long userId)
    {
        var owned = await GetOwnedAsync(id, userId);
        if (owned.IsFailure)
        {
            return Result.Failure<BancoCategoriaResponse, QuizError>(owned.Error);
        }

        var nombre = ValidarNombre(request.Nombre);
        if (nombre.IsFailure)
        {
            return Result.Failure<BancoCategoriaResponse, QuizError>(nombre.Error);
        }

        var existente = await repository.FindByNombreAsync(userId, nombre.Value);
        if (existente is not null && existente.Id != id)
        {
            return Result.Failure<BancoCategoriaResponse, QuizError>(
                new QuizValidationError($"Ya existe una categoría llamada \"{nombre.Value}\""));
        }

        owned.Value.Nombre = nombre.Value;
        await repository.UpdateAsync(owned.Value);

        var total = (await repository.FindByCreatorWithCountsAsync(userId)).FirstOrDefault(c => c.Categoria.Id == id).Total;
        return Result.Success<BancoCategoriaResponse, QuizError>(new BancoCategoriaResponse(id, owned.Value.Nombre, total));
    }

    public async Task<UnitResult<QuizError>> DeleteAsync(long id, long userId)
    {
        var owned = await GetOwnedAsync(id, userId);
        if (owned.IsFailure)
        {
            return UnitResult.Failure(owned.Error);
        }

        await repository.DeleteAsync(owned.Value);
        return UnitResult.Success<QuizError>();
    }

    public async Task<Result<BancoCategoria, QuizError>> GetOwnedAsync(long id, long userId)
    {
        var categoria = await repository.FindByIdAsync(id);
        if (categoria is null || categoria.CreatorId != userId)
        {
            // Mismo 404 para "no existe" y "es de otro" para no revelar ids ajenos
            return Result.Failure<BancoCategoria, QuizError>(new QuizNotFoundError("Categoría no encontrada"));
        }

        return Result.Success<BancoCategoria, QuizError>(categoria);
    }

    public async Task<Result<BancoCategoria, QuizError>> FindOrCreateAsync(string nombre, long userId)
    {
        var limpio = ValidarNombre(nombre);
        if (limpio.IsFailure)
        {
            return Result.Failure<BancoCategoria, QuizError>(limpio.Error);
        }

        var existente = await repository.FindByNombreAsync(userId, limpio.Value);
        if (existente is not null)
        {
            return Result.Success<BancoCategoria, QuizError>(existente);
        }

        var creada = await repository.AddAsync(new BancoCategoria { CreatorId = userId, Nombre = limpio.Value });
        return Result.Success<BancoCategoria, QuizError>(creada);
    }

    /// <summary>Recorta y colapsa espacios; no puede quedar vacío ni pasar de 50 caracteres.</summary>
    public static Result<string, QuizError> ValidarNombre(string? nombre)
    {
        var limpio = EspaciosRegex().Replace(nombre ?? string.Empty, " ").Trim();

        if (limpio.Length == 0)
        {
            return Result.Failure<string, QuizError>(new QuizValidationError("El nombre de la categoría no puede estar vacío"));
        }

        if (limpio.Length > BancoCategoria.NombreMaxLength)
        {
            return Result.Failure<string, QuizError>(new QuizValidationError(
                $"El nombre de la categoría no puede exceder {BancoCategoria.NombreMaxLength} caracteres"));
        }

        return Result.Success<string, QuizError>(limpio);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspaciosRegex();
}
