using Microsoft.Extensions.Caching.Memory;
using TriviUpBackend.Repositories.Users;
using TriviUpBackend.Services.Auth;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Cuestionarios.Services;
using TriviUpBackend.Common.Storage;
using TriviUpBackend.Game.Repositories;
using TriviUpBackend.Services.Cache;

namespace TriviUpBackend.Infrastructure;

/// <summary>
/// Configuración de inyección de dependencias.
/// Registra todos los repositorios, servicios y almacenamiento en el contenedor de servicios.
/// </summary>
public static class DependencyInjectionConfig
{
    public static IServiceCollection AddRepositoriesAndServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Repositorios
        //services.AddScoped<IFunkoRepository, FunkoRepository>();
        //services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IQuizRepository, QuizRepository>();
        services.AddScoped<IBancoPreguntaRepository, BancoPreguntaRepository>();
        services.AddScoped<IBancoCategoriaRepository, BancoCategoriaRepository>();
        services.AddScoped<IGameHistoryRepository, GameHistoryRepository>();

        // Servicios
        //services.AddScoped<IFunkoService, FunkoService>();
        //services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IJwtTokenExtractor, JwtTokenExtractor>();
        services.AddScoped<IQuizService, QuizService>();
        services.AddScoped<IBancoPreguntaService, BancoPreguntaService>();
        services.AddScoped<IBancoCategoriaService, BancoCategoriaService>();

        // Storage
        services.AddScoped<IStorage, Storage>();
        services.AddScoped<IProfilePhotoStorage, ProfilePhotoStorage>();
        services.AddScoped<IQuestionImageStorage, QuestionImageStorage>();

        // Cache - memoria local (quizzes). El estado de partidas usa Redis vía IGameSessionStore.
        services.AddMemoryCache();
        services.AddScoped<ICacheService, MemoryCacheService>();

        //services.AddScoped<IEventPublisher, EventPublisher>();

        return services;
    }
}