import json
import os
from collections import deque
from datetime import datetime, timezone
from typing import Any, Deque, Dict, Set
from urllib.parse import quote


REQUIRED_FIELDS = {"schemaVersion", "message", "eventType", "timestamp", "traceId"}


class Deduper:
    def __init__(self, max_size: int = 5000) -> None:
        self.max_size = max_size
        self.seen: Set[str] = set()
        self.order: Deque[str] = deque()

    def exists(self, trace_id: str) -> bool:
        return trace_id in self.seen

    def add(self, trace_id: str) -> None:
        if trace_id in self.seen:
            return
        self.seen.add(trace_id)
        self.order.append(trace_id)
        while len(self.order) > self.max_size:
            old = self.order.popleft()
            self.seen.discard(old)


def decode_payload(body: bytes):
    try:
        payload = json.loads(body.decode("utf-8"))
    except Exception as exc:
        return None, f"malformed payload: {exc}"
    return payload, None


def validate_payload(payload):
    if not isinstance(payload, dict):
        return False
    if not REQUIRED_FIELDS.issubset(payload.keys()):
        return False
    return bool(payload.get("message"))


def build_consumed_event(rabbit_payload: dict[str, Any], consumer_runtime: str) -> dict[str, Any]:
    """Build the outbound Kafka envelope after successful RabbitMQ validation (not emitted for duplicates)."""
    consumed_at = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    event: dict[str, Any] = {
        "schemaVersion": "1.0",
        "traceId": str(rabbit_payload.get("traceId")),
        "eventType": str(rabbit_payload.get("eventType")),
        "message": str(rabbit_payload.get("message")),
        "consumedAt": consumed_at,
        "consumerRuntime": consumer_runtime,
    }
    if rabbit_payload.get("schemaVersion") is not None:
        event["sourceSchemaVersion"] = str(rabbit_payload["schemaVersion"])
    if rabbit_payload.get("timestamp") is not None:
        event["sourceTimestamp"] = str(rabbit_payload["timestamp"])
    return event


def serialize_consumed_event(event: dict[str, Any]) -> bytes:
    return json.dumps(event, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def env_bool(key: str, fallback: bool) -> bool:
    value = os.getenv(key)
    if value is None:
        return fallback
    return value.lower() in ("1", "true", "yes", "on")


def build_rabbitmq_url() -> str:
    raw = os.getenv("RABBITMQ_URL")
    if raw:
        return raw

    scheme = os.getenv("RABBITMQ_SCHEME", "amqp")
    if env_bool("RABBITMQ_TLS_ENABLED", False):
        scheme = "amqps"
    host = os.getenv("RABBITMQ_HOST", "rabbitmq")
    port = os.getenv("RABBITMQ_PORT", "5672")
    username = quote(os.getenv("RABBITMQ_USERNAME", "guest"), safe="")
    password = quote(os.getenv("RABBITMQ_PASSWORD", "guest"), safe="")
    vhost = os.getenv("RABBITMQ_VHOST", "/")
    if not vhost.startswith("/"):
        vhost = "/" + vhost

    return f"{scheme}://{username}:{password}@{host}:{port}{vhost}"
