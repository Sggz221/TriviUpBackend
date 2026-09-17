using TriviUpBackend.Game.Models;
using TriviUpBackend.Game.Persistence;

namespace TriviUpTest.Services;

public class InMemoryGameSessionStoreTests
{
    private readonly InMemoryGameSessionStore _store = new();

    [Fact]
    public async Task TryCreateAsync_SameCodeTwice_SecondFails()
    {
        var session = CreateSession("ABC123");
        Assert.True(await _store.TryCreateAsync(session));
        Assert.False(await _store.TryCreateAsync(CreateSession("ABC123")));
    }

    [Fact]
    public async Task ScheduleAndGetDueDeadlines_ReturnsOnlyExpired()
    {
        await _store.TryCreateAsync(CreateSession("ROOM1"));
        await _store.TryCreateAsync(CreateSession("ROOM2"));

        var past = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds();
        var future = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();

        await _store.ScheduleDeadlineAsync("ROOM1", past, 1);
        await _store.ScheduleDeadlineAsync("ROOM2", future, 2);

        var due = await _store.GetDueDeadlinesAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 10);

        Assert.Single(due);
        Assert.Equal("ROOM1", due[0].RoomCode);
        Assert.Equal(1, due[0].TurnGeneration);
    }

    [Fact]
    public async Task ClearDeadline_RemovesFromDueList()
    {
        await _store.ScheduleDeadlineAsync("ROOM1", DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds(), 3);
        await _store.ClearDeadlineAsync("ROOM1");

        var due = await _store.GetDueDeadlinesAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 10);
        Assert.Empty(due);
    }

    [Fact]
    public async Task ConnectionAndUserMappings_RoundTrip()
    {
        await _store.SetConnectionMappingAsync("c1", 42, "ROOM1");
        await _store.SetUserRoomAsync(42, "ROOM1");

        var mapping = await _store.GetConnectionMappingAsync("c1");
        Assert.NotNull(mapping);
        Assert.Equal(42, mapping.UserId);
        Assert.Equal("ROOM1", mapping.RoomCode);
        Assert.Equal("ROOM1", await _store.GetUserRoomAsync(42));

        await _store.ClearConnectionMappingAsync("c1");
        await _store.ClearUserRoomAsync(42);

        Assert.Null(await _store.GetConnectionMappingAsync("c1"));
        Assert.Null(await _store.GetUserRoomAsync(42));
    }

    [Fact]
    public async Task AcquireLock_SerializesAccess()
    {
        await using var first = await _store.AcquireLockAsync("ROOM1", TimeSpan.FromSeconds(2));
        Assert.NotNull(first);

        var second = await _store.AcquireLockAsync("ROOM1", TimeSpan.FromMilliseconds(50));
        Assert.Null(second);

        await first.DisposeAsync();
        await using var third = await _store.AcquireLockAsync("ROOM1", TimeSpan.FromSeconds(1));
        Assert.NotNull(third);
    }

    [Fact]
    public async Task SaveAsync_IncrementsRevision()
    {
        var session = CreateSession("REV001");
        await _store.TryCreateAsync(session);
        var loaded = await _store.GetAsync("REV001");
        Assert.Equal(1, loaded!.Revision);

        loaded.QuizTitle = "Updated";
        await _store.SaveAsync(loaded);
        var again = await _store.GetAsync("REV001");
        Assert.Equal(2, again!.Revision);
        Assert.Equal("Updated", again.QuizTitle);
    }

    private static GameSessionDocument CreateSession(string code) => new()
    {
        RoomCode = code,
        QuizId = 1,
        QuizTitle = "Quiz",
        OwnerId = 1,
        State = GameState.Waiting
    };
}
