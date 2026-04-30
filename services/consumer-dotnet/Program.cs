using System.Text;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

record EventEnvelope(string schemaVersion, string message, string eventType, string timestamp, string traceId);

[ExcludeFromCodeCoverage]
static class Env
{
    private static readonly IConfigurationRoot Config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables()
        .Build();

    public static string Get(string key, string fallback) => Config[key] ?? fallback;
    public static int GetInt(string key, int fallback) => int.TryParse(Config[key], out var parsed) ? parsed : fallback;
}

[ExcludeFromCodeCoverage]
class Program
{
    private static readonly HashSet<string> SeenTraceIds = new();
    private static readonly Queue<string> TraceOrder = new();
    private const int DedupMax = 5000;

    static async Task Main()
    {
        while (true)
        {
            try
            {
                RunConsumer();
            }
            catch (Exception ex)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { level = "error", msg = "consumer crash", error = ex.Message }));
                await Task.Delay(3000);
            }
        }
    }

    private static void RunConsumer()
    {
        var rabbitUrl = new Uri(Env.Get("RABBITMQ_URL", Env.Get("Messaging:RabbitMqUrl", "amqp://guest:guest@rabbitmq:5672/")));
        var exchange = Env.Get("EXCHANGE_NAME", Env.Get("Messaging:ExchangeName", "poc.events"));
        var dlx = Env.Get("DLX_NAME", Env.Get("Messaging:DlxName", "poc.dlx"));
        var queue = Env.Get("QUEUE_NAME", Env.Get("Messaging:QueueName", "poc.queue"));
        var routingKey = Env.Get("ROUTING_KEY", Env.Get("Messaging:RoutingKey", "poc.message"));
        var retryQueue = Env.Get("RETRY_QUEUE_NAME", Env.Get("Messaging:RetryQueueName", "poc.queue.retry"));
        var dlq = Env.Get("DLQ_NAME", Env.Get("Messaging:DlqName", "poc.queue.dlq"));
        var retryDelayMs = Env.GetInt("RETRY_DELAY_MS", Env.GetInt("Messaging:RetryDelayMs", 5000));
        var maxRetries = Env.GetInt("MAX_RETRIES", Env.GetInt("Messaging:MaxRetries", 3));

        var factory = new ConnectionFactory
        {
            Uri = rabbitUrl,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true
        };

        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.BasicQos(0, 10, false);

        DeclareTopology(channel, exchange, dlx, queue, retryQueue, dlq, routingKey, retryDelayMs);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            var headers = ea.BasicProperties?.Headers ?? new Dictionary<string, object>();
            var retryCount = ConsumerLogic.ReadRetryCount(headers);
            var bodyBytes = ea.Body.ToArray();
            var bodyText = Encoding.UTF8.GetString(bodyBytes);

            EventEnvelope? payload = null;
            try
            {
                payload = JsonSerializer.Deserialize<EventEnvelope>(bodyText);
            }
            catch
            {
                PublishToDlq(channel, dlx, routingKey, bodyBytes, retryCount, "malformed payload");
                channel.BasicAck(ea.DeliveryTag, false);
                return;
            }

            if (payload is null || !ConsumerLogic.IsPayloadValid(payload))
            {
                PublishToDlq(channel, dlx, routingKey, bodyBytes, retryCount, "missing required fields");
                channel.BasicAck(ea.DeliveryTag, false);
                return;
            }

            if (SeenTraceIds.Contains(payload.traceId))
            {
                Console.WriteLine(JsonSerializer.Serialize(new { level = "warn", msg = "duplicate ignored", traceId = payload.traceId }));
                channel.BasicAck(ea.DeliveryTag, false);
                return;
            }

            try
            {
                RememberTraceId(payload.traceId);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    level = "info",
                    msg = "message consumed",
                    traceId = payload.traceId,
                    eventType = payload.eventType,
                    message = payload.message,
                    processedAt = DateTimeOffset.UtcNow
                }));
                channel.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                if (retryCount < maxRetries)
                {
                    PublishToRetry(channel, dlx, routingKey, bodyBytes, retryCount + 1, $"processing failure: {ex.Message}");
                }
                else
                {
                    PublishToDlq(channel, dlx, routingKey, bodyBytes, retryCount, $"processing retries exhausted: {ex.Message}");
                }
                channel.BasicAck(ea.DeliveryTag, false);
            }

            await Task.CompletedTask;
        };

        channel.BasicConsume(queue: queue, autoAck: false, consumer: consumer);
        Console.WriteLine(JsonSerializer.Serialize(new { level = "info", msg = "dotnet consumer started", queue }));
        var wait = new ManualResetEvent(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; wait.Set(); };
        wait.WaitOne();
    }

    private static void DeclareTopology(IModel channel, string exchange, string dlx, string queue, string retryQueue, string dlq, string routingKey, int retryDelayMs)
    {
        channel.ExchangeDeclare(exchange, ExchangeType.Direct, durable: true);
        channel.ExchangeDeclare(dlx, ExchangeType.Direct, durable: true);

        channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = dlx,
                ["x-dead-letter-routing-key"] = $"{routingKey}.dlq"
            });
        channel.QueueBind(queue, exchange, routingKey);

        channel.QueueDeclare(retryQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                ["x-message-ttl"] = retryDelayMs,
                ["x-dead-letter-exchange"] = exchange,
                ["x-dead-letter-routing-key"] = routingKey
            });
        channel.QueueBind(retryQueue, dlx, $"{routingKey}.retry");

        channel.QueueDeclare(dlq, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(dlq, dlx, $"{routingKey}.dlq");
    }

    private static void PublishToRetry(IModel channel, string dlx, string routingKey, byte[] body, int retryCount, string reason)
    {
        var props = channel.CreateBasicProperties();
        props.Persistent = true;
        props.Headers = new Dictionary<string, object>
        {
            ["x-retry-count"] = retryCount,
            ["x-failure-reason"] = reason
        };
        channel.BasicPublish(dlx, $"{routingKey}.retry", props, body);
    }

    private static void PublishToDlq(IModel channel, string dlx, string routingKey, byte[] body, int retryCount, string reason)
    {
        var props = channel.CreateBasicProperties();
        props.Persistent = true;
        props.Headers = new Dictionary<string, object>
        {
            ["x-retry-count"] = retryCount,
            ["x-failure-reason"] = reason
        };
        channel.BasicPublish(dlx, $"{routingKey}.dlq", props, body);
        Console.WriteLine(JsonSerializer.Serialize(new { level = "error", msg = "message moved to dlq", reason }));
    }

    private static void RememberTraceId(string traceId)
    {
        if (SeenTraceIds.Add(traceId))
        {
            TraceOrder.Enqueue(traceId);
            while (TraceOrder.Count > DedupMax)
            {
                var old = TraceOrder.Dequeue();
                SeenTraceIds.Remove(old);
            }
        }
    }
}
