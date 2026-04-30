package main

import (
	"context"
	"errors"
	"net/http"
	"net/http/httptest"
	"producer-go/internal/rabbit"
	"strings"
	"sync/atomic"
	"testing"
	"time"
)

type fakeBus struct {
	publishErr error
	lastEvent  rabbit.EventMessage
}

func (f *fakeBus) Publish(_ context.Context, event rabbit.EventMessage) error {
	f.lastEvent = event
	return f.publishErr
}

func (f *fakeBus) Close() error { return nil }

func TestBuildEventFromRequest_DefaultEventType(t *testing.T) {
	fixedNow := time.Date(2026, time.April, 30, 12, 0, 0, 0, time.UTC)
	event, err := buildEventFromRequest(PublishRequest{
		Message: "hello",
	}, "trace-123", fixedNow)
	if err != nil {
		t.Fatalf("expected no error, got %v", err)
	}

	if event.EventType != "demo.message" {
		t.Fatalf("expected default event type, got %s", event.EventType)
	}
	if event.TraceID != "trace-123" {
		t.Fatalf("unexpected trace id %s", event.TraceID)
	}
	if event.Timestamp != fixedNow.Format(time.RFC3339) {
		t.Fatalf("unexpected timestamp %s", event.Timestamp)
	}
}

func TestBuildEventFromRequest_RequiresMessage(t *testing.T) {
	_, err := buildEventFromRequest(PublishRequest{}, "trace-123", time.Now())
	if err == nil {
		t.Fatal("expected error for missing message")
	}
}

func TestRandomTraceID_HasHexLength(t *testing.T) {
	trace := randomTraceID()
	if len(trace) < 16 {
		t.Fatalf("expected trace id length >= 16, got %d", len(trace))
	}
	for _, ch := range trace {
		if !strings.ContainsRune("0123456789abcdef", ch) {
			t.Fatalf("trace contains non-hex character: %q", ch)
		}
	}
}

func TestPublishHandler_MethodNotAllowed(t *testing.T) {
	var count int64
	handler := publishHandler(&fakeBus{}, &count, func() string { return "trace-1" }, func() time.Time { return time.Now() })
	req := httptest.NewRequest(http.MethodGet, "/publish", nil)
	rec := httptest.NewRecorder()

	handler(rec, req)

	if rec.Code != http.StatusMethodNotAllowed {
		t.Fatalf("expected 405, got %d", rec.Code)
	}
}

func TestPublishHandler_InvalidJSON(t *testing.T) {
	var count int64
	handler := publishHandler(&fakeBus{}, &count, func() string { return "trace-1" }, func() time.Time { return time.Now() })
	req := httptest.NewRequest(http.MethodPost, "/publish", strings.NewReader("{bad-json"))
	rec := httptest.NewRecorder()

	handler(rec, req)

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("expected 400, got %d", rec.Code)
	}
}

func TestPublishHandler_PublishFailure(t *testing.T) {
	var count int64
	bus := &fakeBus{publishErr: errors.New("broker unavailable")}
	handler := publishHandler(bus, &count, func() string { return "trace-1" }, func() time.Time { return time.Now() })
	req := httptest.NewRequest(http.MethodPost, "/publish", strings.NewReader(`{"message":"hello"}`))
	rec := httptest.NewRecorder()

	handler(rec, req)

	if rec.Code != http.StatusBadGateway {
		t.Fatalf("expected 502, got %d", rec.Code)
	}
}

func TestPublishHandler_Success(t *testing.T) {
	var count int64
	bus := &fakeBus{}
	now := time.Date(2026, time.April, 30, 12, 0, 0, 0, time.UTC)
	handler := publishHandler(bus, &count, func() string { return "trace-xyz" }, func() time.Time { return now })
	req := httptest.NewRequest(http.MethodPost, "/publish", strings.NewReader(`{"message":"hello","eventType":"demo.custom"}`))
	rec := httptest.NewRecorder()

	handler(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected 200, got %d", rec.Code)
	}
	if atomic.LoadInt64(&count) != 1 {
		t.Fatalf("expected publish counter 1, got %d", atomic.LoadInt64(&count))
	}
	if bus.lastEvent.TraceID != "trace-xyz" {
		t.Fatalf("unexpected trace id %s", bus.lastEvent.TraceID)
	}
	if bus.lastEvent.Timestamp != now.Format(time.RFC3339) {
		t.Fatalf("unexpected timestamp %s", bus.lastEvent.Timestamp)
	}
}

func TestBuildRabbitURL_FromComponents(t *testing.T) {
	t.Setenv("RABBITMQ_URL", "")
	t.Setenv("RABBITMQ_SCHEME", "amqp")
	t.Setenv("RABBITMQ_HOST", "rabbit.internal")
	t.Setenv("RABBITMQ_PORT", "5672")
	t.Setenv("RABBITMQ_USERNAME", "user")
	t.Setenv("RABBITMQ_PASSWORD", "pass")
	t.Setenv("RABBITMQ_VHOST", "myvhost")
	t.Setenv("RABBITMQ_TLS_ENABLED", "false")

	got := buildRabbitURL()
	want := "amqp://user:pass@rabbit.internal:5672/myvhost"
	if got != want {
		t.Fatalf("unexpected url: got %s want %s", got, want)
	}
}

func TestBuildRabbitURL_PrefersDirectURL(t *testing.T) {
	expected := "amqps://from-secret-manager"
	t.Setenv("RABBITMQ_URL", expected)
	if got := buildRabbitURL(); got != expected {
		t.Fatalf("expected %s, got %s", expected, got)
	}
}
