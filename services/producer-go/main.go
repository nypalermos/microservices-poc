package main

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"net/http"
	"os"
	"strconv"
	"sync/atomic"
	"time"

	"producer-go/internal/rabbit"
)

type PublishRequest struct {
	Message   string `json:"message"`
	EventType string `json:"eventType"`
}

type EventMessage struct {
	SchemaVersion string `json:"schemaVersion"`
	Message       string `json:"message"`
	EventType     string `json:"eventType"`
	Timestamp     string `json:"timestamp"`
	TraceID       string `json:"traceId"`
}

func buildEventFromRequest(p PublishRequest, traceID string, now time.Time) (EventMessage, error) {
	if p.Message == "" {
		return EventMessage{}, errors.New("message is required")
	}
	eventType := p.EventType
	if eventType == "" {
		eventType = "demo.message"
	}

	return EventMessage{
		SchemaVersion: "1.0",
		Message:       p.Message,
		EventType:     eventType,
		Timestamp:     now.UTC().Format(time.RFC3339),
		TraceID:       traceID,
	}, nil
}

type MessageBus interface {
	Publish(ctx context.Context, event rabbit.EventMessage) error
	Close() error
}

func randomTraceID() string {
	b := make([]byte, 8)
	if _, err := rand.Read(b); err != nil {
		return strconv.FormatInt(time.Now().UnixNano(), 10)
	}
	return hex.EncodeToString(b)
}

func getenv(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

func main() {
	rabbitURL := getenv("RABBITMQ_URL", "amqp://guest:guest@rabbitmq:5672/")
	exchange := getenv("EXCHANGE_NAME", "poc.events")
	queue := getenv("QUEUE_NAME", "poc.queue")
	dlx := getenv("DLX_NAME", "poc.dlx")
	routingKey := getenv("ROUTING_KEY", "poc.message")
	port := getenv("PRODUCER_PORT", "8080")
	retryDelayMS, _ := strconv.Atoi(getenv("RETRY_DELAY_MS", "5000"))

	bus, err := rabbit.NewBus(rabbitURL, exchange, queue, dlx, routingKey, retryDelayMS)
	if err != nil {
		log.Fatalf("startup error: %v", err)
	}
	defer bus.Close()

	var publishedCount int64
	http.HandleFunc("/healthz", func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write([]byte("ok"))
	})
	http.HandleFunc("/metrics", func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "text/plain; version=0.0.4")
		_, _ = fmt.Fprintf(w, "producer_published_total %d\n", atomic.LoadInt64(&publishedCount))
	})
	http.HandleFunc("/publish", publishHandler(bus, &publishedCount, randomTraceID, time.Now))

	log.Printf("producer listening on :%s", port)
	if err := http.ListenAndServe(":"+port, nil); err != nil {
		log.Fatalf("server error: %v", err)
	}
}

func publishHandler(bus MessageBus, publishedCount *int64, traceIDFn func() string, nowFn func() time.Time) http.HandlerFunc {
	return func(w http.ResponseWriter, req *http.Request) {
		if req.Method != http.MethodPost {
			http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
			return
		}

		var p PublishRequest
		if err := json.NewDecoder(req.Body).Decode(&p); err != nil {
			http.Error(w, "invalid json payload", http.StatusBadRequest)
			return
		}
		event, err := buildEventFromRequest(p, traceIDFn(), nowFn())
		if err != nil {
			http.Error(w, err.Error(), http.StatusBadRequest)
			return
		}

		ctx, cancel := context.WithTimeout(req.Context(), 8*time.Second)
		defer cancel()
		if err := bus.Publish(ctx, rabbit.EventMessage(event)); err != nil {
			http.Error(w, "publish failed", http.StatusBadGateway)
			log.Printf("{\"level\":\"error\",\"msg\":\"publish failed\",\"error\":%q}", err.Error())
			return
		}

		atomic.AddInt64(publishedCount, 1)
		w.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(w).Encode(map[string]string{
			"status":  "published",
			"traceId": event.TraceID,
		})
		log.Printf("{\"level\":\"info\",\"msg\":\"published\",\"traceId\":%q,\"eventType\":%q}", event.TraceID, event.EventType)
	}
}
