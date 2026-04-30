import json
import os
import signal
import sys
import time
from datetime import datetime, timezone

import pika
from prometheus_client import Counter, Gauge, start_http_server
from consumer_logic import Deduper, decode_payload, validate_payload


CONSUMED_TOTAL = Counter("consumer_processed_total", "Total successfully processed messages")
FAILED_TOTAL = Counter("consumer_failed_total", "Total failed messages")
DLQ_TOTAL = Counter("consumer_dlq_total", "Total messages moved to dead-letter queue")
RETRY_TOTAL = Counter("consumer_retry_total", "Total messages retried")
READY = Gauge("consumer_ready", "Consumer readiness")


class Consumer:
    def __init__(self) -> None:
        self.rabbitmq_url = os.getenv("RABBITMQ_URL", "amqp://guest:guest@rabbitmq:5672/")
        self.exchange = os.getenv("EXCHANGE_NAME", "poc.events")
        self.dlx = os.getenv("DLX_NAME", "poc.dlx")
        self.queue = os.getenv("QUEUE_NAME", "poc.queue")
        self.retry_queue = os.getenv("RETRY_QUEUE_NAME", "poc.queue.retry")
        self.dlq_name = os.getenv("DLQ_NAME", "poc.queue.dlq")
        self.routing_key = os.getenv("ROUTING_KEY", "poc.message")
        self.retry_delay_ms = int(os.getenv("RETRY_DELAY_MS", "5000"))
        self.max_retries = int(os.getenv("MAX_RETRIES", "3"))
        self.metrics_port = int(os.getenv("CONSUMER_METRICS_PORT", "9100"))
        self.deduper = Deduper()
        self.connection = None
        self.channel = None
        self.running = True

    def setup(self) -> None:
        params = pika.URLParameters(self.rabbitmq_url)
        self.connection = pika.BlockingConnection(params)
        self.channel = self.connection.channel()
        self.channel.basic_qos(prefetch_count=10)
        self._declare_topology()
        start_http_server(self.metrics_port)
        READY.set(1)

    def _declare_topology(self) -> None:
        self.channel.exchange_declare(exchange=self.exchange, exchange_type="direct", durable=True)
        self.channel.exchange_declare(exchange=self.dlx, exchange_type="direct", durable=True)

        self.channel.queue_declare(
            queue=self.queue,
            durable=True,
            arguments={
                "x-dead-letter-exchange": self.dlx,
                "x-dead-letter-routing-key": f"{self.routing_key}.dlq",
            },
        )
        self.channel.queue_bind(queue=self.queue, exchange=self.exchange, routing_key=self.routing_key)

        self.channel.queue_declare(
            queue=self.retry_queue,
            durable=True,
            arguments={
                "x-message-ttl": self.retry_delay_ms,
                "x-dead-letter-exchange": self.exchange,
                "x-dead-letter-routing-key": self.routing_key,
            },
        )
        self.channel.queue_bind(queue=self.retry_queue, exchange=self.dlx, routing_key=f"{self.routing_key}.retry")

        self.channel.queue_declare(queue=self.dlq_name, durable=True)
        self.channel.queue_bind(queue=self.dlq_name, exchange=self.dlx, routing_key=f"{self.routing_key}.dlq")

    def run(self) -> None:
        self.channel.basic_consume(queue=self.queue, on_message_callback=self._on_message, auto_ack=False)
        print(json.dumps({"level": "info", "msg": "consumer started", "queue": self.queue}))
        while self.running:
            self.connection.process_data_events(time_limit=1)

    def _on_message(self, ch, method, properties, body: bytes) -> None:
        retry_count = int((properties.headers or {}).get("x-retry-count", 0))
        payload, parse_error = decode_payload(body)
        if parse_error is not None:
            FAILED_TOTAL.inc()
            self._to_dlq(body, retry_count, parse_error)
            ch.basic_ack(delivery_tag=method.delivery_tag)
            return

        if not validate_payload(payload):
            FAILED_TOTAL.inc()
            self._to_dlq(body, retry_count, "missing required fields")
            ch.basic_ack(delivery_tag=method.delivery_tag)
            return

        trace_id = str(payload.get("traceId"))
        if self.deduper.exists(trace_id):
            ch.basic_ack(delivery_tag=method.delivery_tag)
            print(json.dumps({"level": "warn", "msg": "duplicate ignored", "traceId": trace_id}))
            return

        try:
            # Simulated processing with structured logging for observability.
            print(
                json.dumps(
                    {
                        "level": "info",
                        "msg": "message consumed",
                        "traceId": trace_id,
                        "eventType": payload.get("eventType"),
                        "message": payload.get("message"),
                        "processedAt": datetime.now(timezone.utc).isoformat(),
                    }
                )
            )
            self.deduper.add(trace_id)
            CONSUMED_TOTAL.inc()
            ch.basic_ack(delivery_tag=method.delivery_tag)
        except Exception as exc:
            FAILED_TOTAL.inc()
            if retry_count < self.max_retries:
                self._to_retry(body, retry_count + 1, f"processing failure: {exc}")
            else:
                self._to_dlq(body, retry_count, f"processing retries exhausted: {exc}")
            ch.basic_ack(delivery_tag=method.delivery_tag)

    def _to_retry(self, body: bytes, retry_count: int, reason: str) -> None:
        RETRY_TOTAL.inc()
        self.channel.basic_publish(
            exchange=self.dlx,
            routing_key=f"{self.routing_key}.retry",
            body=body,
            properties=pika.BasicProperties(
                delivery_mode=2,
                headers={"x-retry-count": retry_count, "x-failure-reason": reason},
            ),
        )

    def _to_dlq(self, body: bytes, retry_count: int, reason: str) -> None:
        DLQ_TOTAL.inc()
        self.channel.basic_publish(
            exchange=self.dlx,
            routing_key=f"{self.routing_key}.dlq",
            body=body,
            properties=pika.BasicProperties(
                delivery_mode=2,
                headers={"x-retry-count": retry_count, "x-failure-reason": reason},
            ),
        )
        print(json.dumps({"level": "error", "msg": "message moved to dlq", "reason": reason}))

    def shutdown(self, *_args) -> None:
        self.running = False
        READY.set(0)
        if self.connection and self.connection.is_open:
            self.connection.close()
        print(json.dumps({"level": "info", "msg": "consumer shutdown"}))
        sys.exit(0)


def main() -> None:
    consumer = Consumer()
    signal.signal(signal.SIGINT, consumer.shutdown)
    signal.signal(signal.SIGTERM, consumer.shutdown)
    while True:
        try:
            consumer.setup()
            consumer.run()
        except Exception as exc:
            READY.set(0)
            print(json.dumps({"level": "error", "msg": "consumer error", "error": str(exc)}))
            time.sleep(3)


if __name__ == "__main__":
    main()
