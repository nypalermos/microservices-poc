using System.Text;

internal static class ConsumerLogic
{
    internal static bool IsPayloadValid(EventEnvelope? payload)
    {
        return payload is not null &&
               !string.IsNullOrWhiteSpace(payload.message) &&
               !string.IsNullOrWhiteSpace(payload.traceId) &&
               !string.IsNullOrWhiteSpace(payload.eventType) &&
               !string.IsNullOrWhiteSpace(payload.timestamp) &&
               !string.IsNullOrWhiteSpace(payload.schemaVersion);
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
