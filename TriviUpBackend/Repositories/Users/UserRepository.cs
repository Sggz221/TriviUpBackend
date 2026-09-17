using Microsoft.EntityFrameworkCore;
using TriviUpBackend.Database;
using TriviUpBackend.Models.Auth;

namespace TriviUpBackend.Repositories.Users;

/// <summary>
/// Implementación del repositorio de usuarios.
/// Utiliza Entity Framework Core para acceder a la base de datos.
/// </summary>
public class UserRepository(
    Context context,
    ILogger<UserRepository> logger
) : IUserRepository
{
    /// <inheritdoc cref="IUserRepository.FindByIdAsync"/>
    public async Task<User?> FindByIdAsync(long id)
    {
        logger.LogDebug("Executing query with global filter - UserId: {UserId}", id);
        return await context.Users.FindAsync(id);
    }

    /// <inheritdoc cref="IUserRepository.FindByUsernameAsync"/>
    public async Task<User?> FindByUsernameAsync(string username)
    {
        logger.LogInformation("[REPO] FindByUsernameAsync START - Username: {Username}", username);
        var result = await context.Users.FirstOrDefaultAsync(u => u.Username == username);
        logger.LogInformation("[REPO] FindByUsernameAsync END - Found: {Found}, UserId: {UserId}", result != null, result?.Id);
        return result;
    }

    /// <inheritdoc cref="IUserRepository.FindByEmailAsync"/>
    public async Task<User?> FindByEmailAsync(string email)
    {
        logger.LogDebug("Executing query with global filter - Email: {Email}", email);
        return await context.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    /// <inheritdoc cref="IUserRepository.FindByGoogleIdAsync"/>
    public async Task<User?> FindByGoogleIdAsync(string googleId)
    {
        logger.LogDebug("Executing query with global filter - GoogleId: {GoogleId}", googleId);
        return await context.Users.FirstOrDefaultAsync(u => u.GoogleId == googleId);
    }

    /// <inheritdoc cref="IUserRepository.FindAllAsync"/>
    public async Task<IEnumerable<User>> FindAllAsync()
    {
        return await context.Users
            .OrderBy(u => u.Id)
            .ToListAsync();
    }
    
    /// <inheritdoc cref="IUserRepository.SaveAsync"/>
    public async Task<User> SaveAsync(User user)
    {
        context.Users.Add(user);
        await context.SaveChangesAsync();
        logger.LogInformation("Usuario creado con ID: {Id}", user.Id);
        return user;
    }
    
    /// <inheritdoc cref="IUserRepository.UpdateAsync"/>
    public async Task<User> UpdateAsync(User user)
    {
        context.Users.Update(user);
        await context.SaveChangesAsync();
        logger.LogInformation("Usuario actualizado con ID: {Id}", user.Id);
        return user;
    }
    
    /// <inheritdoc cref="IUserRepository.DeleteAsync"/>
    public async Task DeleteAsync(long id)
    {
        var user = await FindByIdAsync(id);
        if (user is not null)
        {
            user.IsDeleted = true;
            await context.SaveChangesAsync();
            logger.LogInformation("Usuario eliminado con ID: {Id}", id);
        }
    }
    
    /// <inheritdoc cref="IUserRepository.GetActiveUsersAsync"/>
    public async Task<IEnumerable<User>> GetActiveUsersAsync()
    {
        logger.LogDebug("Obteniendo usuarios activos");
        return await context.Users
            .Where(u => !u.IsDeleted)
            .OrderBy(u => u.Email)
            .ToListAsync();
    }


}