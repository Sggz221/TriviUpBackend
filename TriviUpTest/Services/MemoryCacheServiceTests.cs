using Xunit;
using Moq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TriviUpBackend.Services.Cache;

namespace TriviUpTest.Services;

public class MemoryCacheServiceTests
{
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<MemoryCacheService>> _mockLogger;
    private readonly MemoryCacheService _service;

    public MemoryCacheServiceTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _mockLogger = new Mock<ILogger<MemoryCacheService>>();
        _service = new MemoryCacheService(_cache, _mockLogger.Object);
    }

    // ========== GetAsync Tests ==========

    [Fact]
    public async Task GetAsync_ExistingKey_ReturnsValue()
    {
        // Arrange
        var key = "test-key";
        var expectedValue = new TestData { Name = "Test", Value = 42 };
        _cache.Set(key, expectedValue);

        // Act
        var result = await _service.GetAsync<TestData>(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task GetAsync_NonExistingKey_ReturnsDefault()
    {
        // Arrange
        var key = "non-existing-key";

        // Act
        var result = await _service.GetAsync<TestData>(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ComplexObject_ReturnsCorrectly()
    {
        // Arrange
        var key = "complex-key";
        var expectedValue = new ComplexData
        {
            Id = 1,
            Name = "Complex",
            Nested = new NestedData { Value = "nested-value" },
            Items = new List<int> { 1, 2, 3 }
        };
        _cache.Set(key, expectedValue);

        // Act
        var result = await _service.GetAsync<ComplexData>(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("Complex", result.Name);
        Assert.NotNull(result.Nested);
        Assert.Equal("nested-value", result.Nested.Value);
        Assert.Equal(3, result.Items.Count);
    }

    // ========== SetAsync Tests ==========

    [Fact]
    public async Task SetAsync_ValidKeyAndValue_SetsCache()
    {
        // Arrange
        var key = "test-key";
        var value = new TestData { Name = "Test", Value = 100 };
        TimeSpan? expiry = TimeSpan.FromMinutes(5);

        // Act
        await _service.SetAsync(key, value, expiry);

        // Assert
        var result = _cache.Get<TestData>(key);
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(100, result.Value);
    }

    [Fact]
    public async Task SetAsync_NullExpiry_SetsCache()
    {
        // Arrange
        var key = "test-key";
        var value = new TestData { Name = "No Expiry", Value = 0 };

        // Act
        await _service.SetAsync(key, value, null);

        // Assert
        var result = _cache.Get<TestData>(key);
        Assert.NotNull(result);
        Assert.Equal("No Expiry", result.Name);
    }

    [Fact]
    public async Task SetAsync_CacheThrowsException_DoesNotRethrow()
    {
        // Arrange
        var key = "error-key";
        var value = new TestData { Name = "Error", Value = 0 };

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => _service.SetAsync(key, value));
        Assert.Null(exception);
    }

    [Fact]
    public async Task SetAsync_ZeroExpiry_DoesNotThrow()
    {
        // Arrange
        var key = "zero-expiry-key";
        var value = new TestData { Name = "Zero Expiry", Value = 0 };
        TimeSpan? expiry = TimeSpan.Zero;

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => _service.SetAsync(key, value, expiry));
        Assert.Null(exception);
    }

    // ========== RemoveAsync Tests ==========

    [Fact]
    public async Task RemoveAsync_ExistingKey_RemovesFromCache()
    {
        // Arrange
        var key = "to-remove-key";
        var value = new TestData { Name = "Remove Me", Value = 99 };
        _cache.Set(key, value);

        // Act
        await _service.RemoveAsync(key);

        // Assert
        var result = _cache.Get<TestData>(key);
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_CacheThrowsException_DoesNotRethrow()
    {
        // Arrange
        var key = "error-key";

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => _service.RemoveAsync(key));
        Assert.Null(exception);
    }

    [Fact]
    public async Task RemoveAsync_MultipleKeys_RemovesAll()
    {
        // Arrange
        var keys = new[] { "key1", "key2", "key3" };
        foreach (var key in keys)
        {
            _cache.Set(key, new TestData { Name = key, Value = 0 });
        }

        // Act
        foreach (var key in keys)
        {
            await _service.RemoveAsync(key);
        }

        // Assert
        foreach (var key in keys)
        {
            var result = _cache.Get<TestData>(key);
            Assert.Null(result);
        }
    }

    // ========== RemoveByPrefixAsync Tests ==========

    [Fact]
    public async Task RemoveByPrefixAsync_AnyPrefix_DoesNotThrow()
    {
        // Arrange
        var prefix = "quizzes:public:";

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => _service.RemoveByPrefixAsync(prefix));
        Assert.Null(exception);
    }

    [Fact]
    public async Task RemoveByPrefixAsync_DifferentPrefixes_DoesNotThrow()
    {
        // Arrange
        var prefixes = new[] { "quizzes:public:", "quiz:1:", "users:" };

        // Act & Assert - should not throw for any prefix
        foreach (var prefix in prefixes)
        {
            var ex = await Record.ExceptionAsync(() => _service.RemoveByPrefixAsync(prefix));
            Assert.Null(ex);
        }
    }

    [Fact]
    public async Task RemoveByPrefixAsync_EmptyPrefix_DoesNotThrow()
    {
        // Arrange
        var prefix = "";

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => _service.RemoveByPrefixAsync(prefix));
        Assert.Null(exception);
    }

    // ========== Helper Classes ==========

    private class TestData
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    private class ComplexData
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public NestedData? Nested { get; set; }
        public List<int> Items { get; set; } = new();
    }

    private class NestedData
    {
        public string Value { get; set; } = "";
    }
}