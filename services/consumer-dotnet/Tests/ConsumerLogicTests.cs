using System.Text;
using System.Text.Json;
using Xunit;

public class ConsumerLogicTests
{
    [Fact]
    public void ReadRetryCount_ReturnsZeroWhenHeaderMissing()
    {
        var headers = new Dictionary<string, object>();
        var result = ConsumerLogic.ReadRetryCount(headers);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ReadRetryCount_ReadsIntegerFromByteArray()
    {
        var headers = new Dictionary<string, object>
        {
            ["x-retry-count"] = Encoding.UTF8.GetBytes("2")
        };
        var result = ConsumerLogic.ReadRetryCount(headers);
        Assert.Equal(2, result);
    }

    [Fact]
    public void IsPayloadValid_ReturnsFalseWhenRequiredFieldsMissing()
    {
        var payload = new EventEnvelope("1.0", "", "demo.message", "2026-04-30T00:00:00Z", "trace-id");
        Assert.False(ConsumerLogic.IsPayloadValid(payload));
    }

    [Fact]
    public void IsPayloadValid_ReturnsTrueForValidPayload()
    {
        var payload = new EventEnvelope("1.0", "hello", "demo.message", "2026-04-30T00:00:00Z", "trace-id");
        Assert.True(ConsumerLogic.IsPayloadValid(payload));
    }

    [Fact]
    public void BuildConsumedEventJson_IncludesTraceAndRuntime()
    {
        var src = new EventEnvelope("1.0", "hello", "demo.message", "2026-04-30T00:00:00Z", "trace-xyz");
        var json = ConsumerLogic.BuildConsumedEventJson(src, "dotnet");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("trace-xyz", root.GetProperty("traceId").GetString());
        Assert.Equal("dotnet", root.GetProperty("consumerRuntime").GetString());
        Assert.Equal("1.0", root.GetProperty("sourceSchemaVersion").GetString());
        Assert.Equal("2026-04-30T00:00:00Z", root.GetProperty("sourceTimestamp").GetString());
        Assert.True(root.TryGetProperty("consumedAt", out _));
    }

    [Fact]
    public void BuildRabbitMqUrl_PrefersDirectUrlWhenSet()
    {
        Environment.SetEnvironmentVariable("RABBITMQ_URL", "amqps://from-secret-manager");
        var result = Env.BuildRabbitMqUrl();
        Assert.Equal("amqps://from-secret-manager", result);
        Environment.SetEnvironmentVariable("RABBITMQ_URL", null);
    }
}
