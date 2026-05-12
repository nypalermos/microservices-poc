"""Synchronous publish of consumed events to Kafka (optional via KAFKA_ENABLED)."""

from __future__ import annotations

import os
from typing import Any, Optional

from confluent_kafka import Producer

from consumer_logic import build_consumed_event, env_bool, serialize_consumed_event


def _kafka_config() -> dict[str, Any]:
    servers = os.getenv("KAFKA_BOOTSTRAP_SERVERS", "localhost:9092")
    return {
        "bootstrap.servers": servers,
        # Avoid PID acquisition races on a just-started KRaft broker; acks=all still waits for ISR.
        "enable.idempotence": False,
        "acks": "all",
    }


class KafkaConsumedEmitter:
    """Single producer reused for the lifetime of the consumer process."""

    def __init__(self) -> None:
        self.enabled = env_bool("KAFKA_ENABLED", False)
        self.topic = os.getenv("KAFKA_TOPIC", "poc.consumed")
        self._producer: Optional[Producer] = None
        if self.enabled:
            self._producer = Producer(_kafka_config())

    def close(self) -> None:
        if self._producer is not None:
            self._producer.flush(timeout=30)
            self._producer = None

    def publish_consumed(self, rabbit_payload: dict[str, Any], consumer_runtime: str) -> None:
        if not self.enabled or self._producer is None:
            return
        event = build_consumed_event(rabbit_payload, consumer_runtime)
        body = serialize_consumed_event(event)
        trace_id = str(event["traceId"])
        delivery_errors: list[str] = []

        def delivery_report(err, _msg) -> None:
            if err is not None:
                delivery_errors.append(str(err))

        self._producer.produce(
            self.topic,
            key=trace_id.encode("utf-8"),
            value=body,
            headers=[
                ("eventType", str(event["eventType"]).encode("utf-8")),
                ("consumerRuntime", consumer_runtime.encode("utf-8")),
            ],
            callback=delivery_report,
        )
        self._producer.flush(timeout=30)
        if delivery_errors:
            raise RuntimeError(f"kafka delivery failed: {delivery_errors[0]}")
