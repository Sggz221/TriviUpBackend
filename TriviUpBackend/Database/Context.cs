using Microsoft.EntityFrameworkCore;
using TriviUpBackend.Data;
using TriviUpBackend.Models.Auth;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Game.Models;

namespace TriviUpBackend.Database;

/// <summary>
/// Contexto de Entity Framework Core para la base de datos.
/// Configura los DbSets y las entidades del modelo.
/// </summary>
public class Context(DbContextOptions options) : DbContext(options)
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Quiz> Quizzes { get; set; } = null!;
    public DbSet<Pregunta> Preguntas { get; set; } = null!;
    public DbSet<Respuesta> Respuestas { get; set; } = null!;
    public DbSet<GameHistory> GameHistories { get; set; } = null!;
    public DbSet<QuizVersion> QuizVersions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasQueryFilter(u => !u.IsDeleted);
            entity.ConfigureTimestamps();
            entity.HasIndex(u => u.GoogleId).IsUnique();
        });

        modelBuilder.Entity<Quiz>(entity =>
        {
            entity.ConfigureTimestamps();
            entity.HasIndex(q => q.GameCode).IsUnique();
            entity.HasMany(q => q.Preguntas)
                .WithOne(p => p.Quiz)
                .HasForeignKey(p => p.QuizId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(q => q.EsPublico).HasDefaultValue(false);
            entity.Property(q => q.Visitas).HasDefaultValue(0);
            entity.Property(q => q.Likes).HasDefaultValue(0);
        });

        modelBuilder.Entity<QuizVersion>(entity =>
        {
            entity.Property(v => v.Estado).HasConversion<string>().HasMaxLength(20);
            entity.Property(v => v.Contenido).HasColumnType("jsonb");
            entity.Property(v => v.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(v => v.Quiz)
                .WithMany(q => q.Versiones)
                .HasForeignKey(v => v.QuizId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(v => new { v.QuizId, v.Numero }).IsUnique();
            // Un solo borrador pendiente por quiz
            entity.HasIndex(v => v.QuizId)
                .IsUnique()
                .HasFilter("\"Estado\" = 'Borrador'")
                .HasDatabaseName("IX_quiz_versions_QuizId_Borrador");
        });

        modelBuilder.Entity<Pregunta>(entity =>
        {
            entity.ConfigureTimestamps();
            entity.HasMany(p => p.Respuestas)
                .WithOne(r => r.Pregunta)
                .HasForeignKey(r => r.PreguntaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Respuesta>(entity =>
        {
            entity.ConfigureTimestamps();
        });

        modelBuilder.Entity<GameHistory>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.HasIndex(g => g.GameId).IsUnique();
            entity.HasIndex(g => g.QuizId);
            entity.HasIndex(g => g.EndedAt);
            entity.Ignore(g => g.PlayerResults);
        });

        modelBuilder.Ignore<PlayerResult>();

        SeedData(modelBuilder);
    }

    // Valores fijos: el seed debe ser determinista para que el snapshot de migraciones no cambie entre compilaciones.
    private static readonly DateTime SeedDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static void SeedData(ModelBuilder modelBuilder)
    {
        var adminUser = new User
        {
            Id = 1,
            Username = "admin",
            Email = "admin@funkoapi.com",
            PasswordHash = "$2a$12$FMQKFvn9GqrqkuY8Gwm8J.eo2xq9ZHxiGeoUObpf/c3DL8/5lo2PW",
            Role = UserRoles.ADMIN,
            IsDeleted = false,
            CreatedAt = SeedDate,
            UpdatedAt = SeedDate
        };

        var normalUser = new User
        {
            Id = 2,
            Username = "user",
            Email = "user@funkoapi.com",
            PasswordHash = "$2a$12$YgFVgrCGk6TyiQiKYBujmOvf.Sx94.9AAZ0X4T7COcDAdtR0HZkGm",
            Role = UserRoles.USER,
            IsDeleted = false,
            CreatedAt = SeedDate,
            UpdatedAt = SeedDate
        };

        var testUser = new User
        {
            Id = 3,
            Username = "testuser",
            Email = "test@test.com",
            PasswordHash = "$2a$12$.q7bnzBb9.r4cpqLyO3tBOlO54ozRPxjp0hv06OWD64A.OomLAYuK",
            Role = UserRoles.USER,
            IsDeleted = false,
            CreatedAt = SeedDate,
            UpdatedAt = SeedDate
        };

        modelBuilder.Entity<User>().HasData(adminUser, normalUser, testUser);

        var publicQuizzes = new[]
        {
            new Quiz
            {
                Id = 1,
                Nombre = "Trivia de Historia General",
                GameCode = "HIST01",
                CreatorId = 1,
                EsPublico = true,
                Visitas = 150,
                VersionPublicada = 1,
                Likes = 42,
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            },
            new Quiz
            {
                Id = 2,
                Nombre = "Cultura General - Nivel F\u00e1cil",
                GameCode = "CULT02",
                CreatorId = 2,
                EsPublico = true,
                Visitas = 89,
                VersionPublicada = 1,
                Likes = 23,
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            },
            new Quiz
            {
                Id = 3,
                Nombre = "Ciencia y Naturaleza",
                GameCode = "SCIN03",
                CreatorId = 1,
                EsPublico = true,
                Visitas = 234,
                VersionPublicada = 1,
                Likes = 67,
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            },
            new Quiz
            {
                Id = 4,
                Nombre = "Geograf\u00eda Mundial",
                GameCode = "GEOM04",
                CreatorId = 3,
                EsPublico = true,
                Visitas = 178,
                VersionPublicada = 1,
                Likes = 51,
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            },
            new Quiz
            {
                Id = 5,
                Nombre = "Entretenimiento y Cine",
                GameCode = "CINE05",
                CreatorId = 2,
                EsPublico = true,
                Visitas = 312,
                VersionPublicada = 1,
                Likes = 95,
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            },
            new Quiz
            {
                Id = 6,
                Nombre = "Quiz Privado de Prueba",
                GameCode = "PRIV06",
                CreatorId = 1,
                EsPublico = false,
                Visitas = 0,
                VersionPublicada = 1,
                Likes = 0,
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            }
        };

        modelBuilder.Entity<Quiz>().HasData(publicQuizzes);
    }
}
