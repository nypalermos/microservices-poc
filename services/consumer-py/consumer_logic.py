import json
import os
from collections import deque
from typing import Deque, Set
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
