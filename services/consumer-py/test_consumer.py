import unittest
from unittest.mock import patch

from consumer_logic import (
    Deduper,
    build_consumed_event,
    build_rabbitmq_url,
    decode_payload,
    serialize_consumed_event,
    validate_payload,
)


class TestDeduper(unittest.TestCase):
    def test_deduper_evicts_old_entries(self):
        deduper = Deduper(max_size=2)
        deduper.add("trace-1")
        deduper.add("trace-2")
        deduper.add("trace-3")

        self.assertFalse(deduper.exists("trace-1"))
        self.assertTrue(deduper.exists("trace-2"))
        self.assertTrue(deduper.exists("trace-3"))


class TestPayloadValidation(unittest.TestCase):
    def test_decode_payload_returns_error_for_malformed_json(self):
        payload, error = decode_payload(b"{bad-json")
        self.assertIsNone(payload)
        self.assertIn("malformed payload", error)

    def test_validate_payload_accepts_valid_shape(self):
        payload = {
            "schemaVersion": "1.0",
            "message": "hello",
            "eventType": "demo.message",
            "timestamp": "2026-04-30T00:00:00Z",
            "traceId": "abc12345",
        }
        self.assertTrue(validate_payload(payload))

    def test_validate_payload_rejects_missing_fields(self):
        payload = {"message": "hello"}
        self.assertFalse(validate_payload(payload))


class TestRabbitUrlBuild(unittest.TestCase):
    @patch.dict(
        "os.environ",
        {
            "RABBITMQ_URL": "",
            "RABBITMQ_SCHEME": "amqp",
            "RABBITMQ_HOST": "rabbit.internal",
            "RABBITMQ_PORT": "5672",
            "RABBITMQ_USERNAME": "user",
            "RABBITMQ_PASSWORD": "pass",
            "RABBITMQ_VHOST": "myvhost",
            "RABBITMQ_TLS_ENABLED": "false",
        },
        clear=False,
    )
    def test_build_rabbitmq_url_from_components(self):
        self.assertEqual(
            build_rabbitmq_url(),
            "amqp://user:pass@rabbit.internal:5672/myvhost",
        )

    @patch.dict("os.environ", {"RABBITMQ_URL": "amqps://from-secret-manager"}, clear=False)
    def test_build_rabbitmq_url_prefers_direct_secret_url(self):
        self.assertEqual(build_rabbitmq_url(), "amqps://from-secret-manager")


class TestConsumedEvent(unittest.TestCase):
    def test_build_consumed_event_includes_optional_source_fields(self):
        rabbit = {
            "schemaVersion": "1.0",
            "message": "hello",
            "eventType": "demo.message",
            "timestamp": "2026-04-30T00:00:00Z",
            "traceId": "abc12345",
        }
        event = build_consumed_event(rabbit, "python")
        self.assertEqual(event["schemaVersion"], "1.0")
        self.assertEqual(event["traceId"], "abc12345")
        self.assertEqual(event["eventType"], "demo.message")
        self.assertEqual(event["message"], "hello")
        self.assertEqual(event["consumerRuntime"], "python")
        self.assertEqual(event["sourceSchemaVersion"], "1.0")
        self.assertEqual(event["sourceTimestamp"], "2026-04-30T00:00:00Z")
        raw = serialize_consumed_event(event)
        self.assertIn(b"consumedAt", raw)


if __name__ == "__main__":
    unittest.main()
