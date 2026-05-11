using System.Text;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Confluent.Kafka;
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
    public static bool GetBool(string key, bool fallback) => bool.TryParse(Config[key], out var parsed) ? parsed : fallback;

    public static string BuildRabbitMqUrl()
    {
        var direct = Get("RABBITMQ_URL", Get("Messaging:RabbitMqUrl", ""));
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var scheme = Get("RABBITMQ_SCHEME", Get("Messaging:RabbitMqScheme", "amqp"));
        if (GetBool("RABBITMQ_TLS_ENABLED", GetBool("Messaging:RabbitMqTlsEnabled", false)))
        {
            scheme = "amqps";
        }
        var host = Get("RABBITMQ_HOST", Get("Messaging:RabbitMqHost", "rabbitmq"));
        var port = Get("RABBITMQ_PORT", Get("Messaging:RabbitMqPort", "5672"));
        var username = Uri.EscapeDataString(Get("RABBITMQ_USERNAME", Get("Messaging:RabbitMqUsername", "guest")));
        var password = Uri.EscapeDataString(Get("RABBITMQ_PASSWORD", Get("Messaging:RabbitMqPassword", "guest")));
        var vhost = Get("RABBITMQ_VHOST", Get("Messaging:RabbitMqVHost", "/"));
        if (!vhost.StartsWith("/"))
        {
            vhost = "/" + vhost;
        }
        return $"{scheme}://{username}:{password}@{host}:{port}{vhost}";
    }
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
        var rabbitUrl = new Uri(Env.BuildRabbitMqUrl());
        var exchange = Env.Get("EXCHANGE_NAME", Env.Get("Messaging:ExchangeName", "poc.events"));
        var dlx = Env.Get("DLX_NAME", Env.Get("Messaging:DlxName", "poc.dlx"));
        var queue = Env.Get("QUEUE_NAME", Env.Get("Messaging:QueueName", "poc.queue"));
        var routingKey = Env.Get("ROUTING_KEY", Env.Get("Messaging:RoutingKey", "poc.message"));
        var retryQueue = Env.Get("RETRY_QUEUE_NAME", Env.Get("Messaging:RetryQueueName", "poc.queue.retry"));
        var dlq = Env.Get("DLQ_NAME", Env.Get("Messaging:DlqName", "poc.queue.dlq"));
        var retryDelayMs = Env.GetInt("RETRY_DELAY_MS", Env.GetInt("Messaging:RetryDelayMs", 5000));
        var maxRetries = Env.GetInt("MAX_RETRIES", Env.GetInt("Messaging:MaxRetries", 3));
        var kafkaEnabled = Env.GetBool("KAFKA_ENABLED", Env.GetBool("Messaging:KafkaEnabled", false));
        var kafkaTopic = Env.Get("KAFKA_TOPIC", Env.Get("Messaging:KafkaTopic", "poc.consumed"));

        IProducer<string, string>? kafkaProducer = null;
        if (kafkaEnabled)
        {
            var producerConfig = new ProducerConfig
            {
                BootstrapServers = Env.Get("KAFKA_BOOTSTRAP_SERVERS", Env.Get("Messaging:KafkaBootstrapServers", "localhost:9092")),
                Acks = Acks.All,
                EnableIdempotence = true,
            };
            kafkaProducer = new ProducerBuilder<string, string>(producerConfig).Build();
        }

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

            if (kafkaEnabled)
            {
                if (kafkaProducer is null)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { level = "error", msg = "KAFKA_ENABLED but Kafka producer is null" }));
                    return;
                }

                try
                {
                    var json = ConsumerLogic.BuildConsumedEventJson(payload, "dotnet");
                    var message = new Message<string, string>
                    {
                        Key = payload.traceId,
                        Value = json,
                        Headers = new Confluent.Kafka.Headers
                        {
                            { "eventType", Encoding.UTF8.GetBytes(payload.eventType) },
                            { "consumerRuntime", Encoding.UTF8.GetBytes("dotnet") },
                        }
                    };
                    await kafkaProducer.ProduceAsync(kafkaTopic, message).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        level = "error",
                        msg = "kafka publish failed; message not acked for redelivery",
                        traceId = payload.traceId,
                        error = ex.Message
                    }));
                    return;
                }
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
        try
        {
            wait.WaitOne();
        }
        finally
        {
            kafkaProducer?.Flush(TimeSpan.FromSeconds(30));
            kafkaProducer?.Dispose();
        }
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
