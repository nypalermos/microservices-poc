package rabbit

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"time"

	amqp "github.com/rabbitmq/amqp091-go"
)

type EventMessage struct {
	SchemaVersion string `json:"schemaVersion"`
	Message       string `json:"message"`
	EventType     string `json:"eventType"`
	Timestamp     string `json:"timestamp"`
	TraceID       string `json:"traceId"`
}

type Bus struct {
	conn       *amqp.Connection
	ch         *amqp.Channel
	confirms   <-chan amqp.Confirmation
	exchange   string
	routingKey string
}

func NewBus(url, exchange, queue, dlx, routingKey string, retryDelayMS int) (*Bus, error) {
	conn, err := amqp.Dial(url)
	if err != nil {
		return nil, fmt.Errorf("connect rabbitmq: %w", err)
	}

	ch, err := conn.Channel()
	if err != nil {
		_ = conn.Close()
		return nil, fmt.Errorf("create channel: %w", err)
	}

	if err := ch.Confirm(false); err != nil {
		_ = ch.Close()
		_ = conn.Close()
		return nil, fmt.Errorf("confirm mode: %w", err)
	}
	confirms := ch.NotifyPublish(make(chan amqp.Confirmation, 1))

	if err := declareTopology(ch, exchange, queue, dlx, routingKey, retryDelayMS); err != nil {
		_ = ch.Close()
		_ = conn.Close()
		return nil, err
	}

	return &Bus{
		conn:       conn,
		ch:         ch,
		confirms:   confirms,
		exchange:   exchange,
		routingKey: routingKey,
	}, nil
}

func declareTopology(ch *amqp.Channel, exchange, queue, dlx, routingKey string, retryDelayMS int) error {
	if err := ch.ExchangeDeclare(exchange, "direct", true, false, false, false, nil); err != nil {
		return fmt.Errorf("declare exchange: %w", err)
	}
	if err := ch.ExchangeDeclare(dlx, "direct", true, false, false, false, nil); err != nil {
		return fmt.Errorf("declare dlx: %w", err)
	}

	queueArgs := amqp.Table{
		"x-dead-letter-exchange":    dlx,
		"x-dead-letter-routing-key": routingKey + ".dlq",
	}
	if _, err := ch.QueueDeclare(queue, true, false, false, false, queueArgs); err != nil {
		return fmt.Errorf("declare queue: %w", err)
	}
	if err := ch.QueueBind(queue, routingKey, exchange, false, nil); err != nil {
		return fmt.Errorf("bind queue: %w", err)
	}

	retryQueue := queue + ".retry"
	retryArgs := amqp.Table{
		"x-message-ttl":             int32(retryDelayMS),
		"x-dead-letter-exchange":    exchange,
		"x-dead-letter-routing-key": routingKey,
	}
	if _, err := ch.QueueDeclare(retryQueue, true, false, false, false, retryArgs); err != nil {
		return fmt.Errorf("declare retry queue: %w", err)
	}
	if err := ch.QueueBind(retryQueue, routingKey+".retry", dlx, false, nil); err != nil {
		return fmt.Errorf("bind retry queue: %w", err)
	}

	dlqQueue := queue + ".dlq"
	if _, err := ch.QueueDeclare(dlqQueue, true, false, false, false, nil); err != nil {
		return fmt.Errorf("declare dlq queue: %w", err)
	}
	if err := ch.QueueBind(dlqQueue, routingKey+".dlq", dlx, false, nil); err != nil {
		return fmt.Errorf("bind dlq queue: %w", err)
	}

	return nil
}

func (r *Bus) Publish(ctx context.Context, event EventMessage) error {
	body, err := json.Marshal(event)
	if err != nil {
		return fmt.Errorf("marshal event: %w", err)
	}

	msg := amqp.Publishing{
		ContentType:  "application/json",
		DeliveryMode: amqp.Persistent,
		Timestamp:    time.Now().UTC(),
		Body:         body,
		Headers: amqp.Table{
			"x-schema-version": event.SchemaVersion,
			"x-trace-id":       event.TraceID,
		},
	}

	var lastErr error
	for attempt := 1; attempt <= 3; attempt++ {
		if err := r.ch.PublishWithContext(ctx, r.exchange, r.routingKey, false, false, msg); err != nil {
			lastErr = err
		} else {
			select {
			case c := <-r.confirms:
				if c.Ack {
					return nil
				}
				lastErr = errors.New("broker nack")
			case <-ctx.Done():
				return ctx.Err()
			case <-time.After(3 * time.Second):
				lastErr = errors.New("publish confirm timeout")
			}
		}
		time.Sleep(time.Duration(attempt) * 250 * time.Millisecond)
	}
	return fmt.Errorf("publish failed after retries: %w", lastErr)
}

func (r *Bus) Close() error {
	if err := r.ch.Close(); err != nil {
		return err
	}
	return r.conn.Close()
}
