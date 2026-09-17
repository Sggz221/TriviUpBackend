using TriviUpBackend.Infrastructure;

namespace TriviUpTest.Services;

public class RedisConfigTests
{
    [Theory]
    [InlineData("localhost:6379", "localhost:6379")]
    [InlineData("my-redis.cache.windows.net:6380,password=secret,ssl=True", "my-redis.cache.windows.net:6380,password=secret,ssl=True")]
    public void ConvertRedisUrl_PlainConnectionString_ReturnsUnchanged(string input, string expected)
    {
        Assert.Equal(expected, RedisConfig.ConvertRedisUrl(input));
    }

    [Fact]
    public void ConvertRedisUrl_RailwayStyleUrl_ParsesHostPortAndPassword()
    {
        var result = RedisConfig.ConvertRedisUrl("redis://default:s3cret@redis.railway.internal:6379");

        Assert.Contains("redis.railway.internal:6379", result);
        Assert.Contains("password=s3cret", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertRedisUrl_Rediss_EnablesSsl()
    {
        var result = RedisConfig.ConvertRedisUrl("rediss://:pass@example.com:6380");

        Assert.Contains("ssl=True", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("example.com:6380", result);
    }
}
