using System.Text;
using System.Text.Json;

internal static class ConsumerLogic
{
    private static readonly JsonSerializerOptions ConsumedEventJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static bool IsPayloadValid(EventEnvelope? payload)
    {
        return payload is not null &&
               !string.IsNullOrWhiteSpace(payload.message) &&
               !string.IsNullOrWhiteSpace(payload.traceId) &&
               !string.IsNullOrWhiteSpace(payload.eventType) &&
               !string.IsNullOrWhiteSpace(payload.timestamp) &&
               !string.IsNullOrWhiteSpace(payload.schemaVersion);
    }

    internal static string BuildConsumedEventJson(EventEnvelope source, string consumerRuntime)
    {
        var consumedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var dict = new Dictionary<string, object?>
        {
            ["schemaVersion"] = "1.0",
            ["traceId"] = source.traceId,
            ["eventType"] = source.eventType,
            ["message"] = source.message,
            ["consumedAt"] = consumedAt,
            ["consumerRuntime"] = consumerRuntime,
        };
        if (!string.IsNullOrWhiteSpace(source.schemaVersion))
        {
            dict["sourceSchemaVersion"] = source.schemaVersion;
        }

        if (!string.IsNullOrWhiteSpace(source.timestamp))
        {
            dict["sourceTimestamp"] = source.timestamp;
        }

        return JsonSerializer.Serialize(dict, ConsumedEventJsonOptions);
    }

    internal static int ReadRetryCount(IDictionary<string, object> headers)
    {
        if (!headers.TryGetValue("x-retry-count", out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            byte b => b,
            sbyte sb => sb,
            short s => s,
            ushort us => us,
            int i => i,
            long l => (int)l,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0
        };
    }
}
