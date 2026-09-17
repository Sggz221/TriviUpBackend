using TriviUpBackend.Game.Configuration;
using TriviUpBackend.Game.Persistence;
using TriviUpBackend.Game.Services;

namespace TriviUpBackend.Game.Workers;

/// <summary>
/// Worker que reclama deadlines de turno vencidos de forma idempotente entre instancias.
/// </summary>
public sealed class TurnDeadlineWorker(
    IGameSessionStore store,
    ITurnDeadlineProcessor timeoutProcessor,
    GameOptions options,
    ILogger<TurnDeadlineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TurnDeadlineWorker started. Poll interval: {Interval}ms", options.DeadlinePollIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var due = await store.GetDueDeadlinesAsync(nowMs, limit: 25, stoppingToken);

                foreach (var deadline in due)
                {
                    try
                    {
                        await timeoutProcessor.ProcessDueTimeoutAsync(deadline.RoomCode, deadline.TurnGeneration);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed processing timeout for room {RoomCode}", deadline.RoomCode);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TurnDeadlineWorker poll failed");
            }

            try
            {
                await Task.Delay(options.DeadlinePollIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("TurnDeadlineWorker stopped");
    }
}
