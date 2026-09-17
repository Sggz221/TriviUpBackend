using TriviUpBackend.Models.Auth;

namespace TriviUpBackend.Repositories.Users;

/// <summary>
/// Interfaz del repositorio de usuarios.
/// Proporciona métodos para acceder y manipular datos de usuarios en la base de datos.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Busca un usuario por su ID.
    /// </summary>
    /// <param name="id">ID del usuario.</param>
    /// <returns>Usuario encontrado o null si no existe.</returns>
    Task<User?> FindByIdAsync(long id);

    /// <summary>
    /// Busca un usuario por su nombre de usuario.
    /// </summary>
    /// <param name="username">Nombre de usuario.</param>
    /// <returns>Usuario encontrado o null si no existe.</returns>
    Task<User?> FindByUsernameAsync(string username);

    /// <summary>
    /// Busca un usuario por su correo electrónico.
    /// </summary>
    /// <param name="email">Correo electrónico.</param>
    /// <returns>Usuario encontrado o null si no existe.</returns>
    Task<User?> FindByEmailAsync(string email);

    /// <summary>
    /// Busca un usuario por su ID de Google.
    /// </summary>
    /// <param name="googleId">Identificador único de Google.</param>
    /// <returns>Usuario encontrado o null si no existe.</returns>
    Task<User?> FindByGoogleIdAsync(string googleId);

    /// <summary>
    /// Obtiene todos los usuarios.
    /// </summary>
    /// <returns>Lista de todos los usuarios.</returns>
    Task<IEnumerable<User>> FindAllAsync();

    /// <summary>
    /// Guarda un nuevo usuario en la base de datos.
    /// </summary>
    /// <param name="user">Usuario a guardar.</param>
    /// <returns>Usuario guardado con su ID asignado.</returns>
    Task<User> SaveAsync(User user);

    /// <summary>
    /// Actualiza un usuario existente.
    /// </summary>
    /// <param name="user">Usuario con los datos actualizados.</param>
    /// <returns>Usuario actualizado.</returns>
    Task<User> UpdateAsync(User user);

    /// <summary>
    /// Elimina un usuario suavemente (soft delete) estableciendo IsDeleted a true.
    /// </summary>
    /// <param name="id">ID del usuario a eliminar.</param>
    Task DeleteAsync(long id);

    /// <summary>
    /// Obtiene todos los usuarios activos (no eliminados).
    /// </summary>
    /// <returns>Lista de usuarios activos ordenados por email.</returns>
    Task<IEnumerable<User>> GetActiveUsersAsync();
}