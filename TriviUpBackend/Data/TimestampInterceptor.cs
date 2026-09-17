using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TriviUpBackend.Data;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class TimestampInterceptor : SaveChangesInterceptor
{

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context == null)
            return base.SavingChanges(eventData, result);

        UpdateTimestamps(eventData.Context);
        return base.SavingChanges(eventData, result);
    }


    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context == null)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        UpdateTimestamps(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void UpdateTimestamps(DbContext context)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<ITimestamped>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(e => e.CreatedAt).CurrentValue = now;
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    break;
                case EntityState.Modified:
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    break;
            }
        }
    }
}


public static class TimestampExtensions
{

    public static void ConfigureTimestamps(this EntityTypeBuilder entity)
    {
        entity.Property("CreatedAt")
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.Property("UpdatedAt")
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}