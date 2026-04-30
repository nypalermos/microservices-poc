import unittest

from consumer_logic import Deduper, decode_payload, validate_payload


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


if __name__ == "__main__":
    unittest.main()
