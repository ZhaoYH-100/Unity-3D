package issue

import (
	"errors"
	"fmt"
	"math"
	"strings"
	"unicode/utf8"

	"github.com/google/uuid"
)

type Priority string

const (
	PriorityLow    Priority = "low"
	PriorityMedium Priority = "medium"
	PriorityHigh   Priority = "high"
)

func (p Priority) Valid() bool {
	return p == PriorityLow || p == PriorityMedium || p == PriorityHigh
}

type Status string

const (
	StatusOpen       Status = "open"
	StatusInProgress Status = "in_progress"
	StatusResolved   Status = "resolved"
)

func (s Status) Valid() bool {
	return s == StatusOpen || s == StatusInProgress || s == StatusResolved
}

type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

type Issue struct {
	ID          string   `json:"id"`
	Title       string   `json:"title"`
	Description string   `json:"description"`
	Priority    Priority `json:"priority"`
	Status      Status   `json:"status"`
	DeviceCode  string   `json:"deviceCode"`
	Zone        string   `json:"zone"`
	Position    Position `json:"position"`
	CreatedAt   string   `json:"createdAt"`
	UpdatedAt   string   `json:"updatedAt"`
}

type PositionInput struct {
	X *float64 `json:"x"`
	Y *float64 `json:"y"`
	Z *float64 `json:"z"`
}

type CreateInput struct {
	Title       *string        `json:"title"`
	Description *string        `json:"description"`
	Priority    *string        `json:"priority"`
	DeviceCode  *string        `json:"deviceCode"`
	Zone        *string        `json:"zone"`
	Position    *PositionInput `json:"position"`
}

type NormalizedCreate struct {
	Title       string
	Description string
	Priority    Priority
	DeviceCode  string
	Zone        string
	Position    Position
}

type ValidationError struct {
	Field   string
	Message string
}

func (e *ValidationError) Error() string {
	return e.Message
}

var (
	ErrNotFound            = errors.New("issue not found")
	ErrIdempotencyConflict = errors.New("idempotency key reused with different payload")
)

func NormalizeCreate(input CreateInput) (NormalizedCreate, error) {
	if input.Title == nil {
		return NormalizedCreate{}, &ValidationError{Field: "title", Message: "title is required"}
	}

	title := strings.TrimSpace(*input.Title)
	if title == "" {
		return NormalizedCreate{}, &ValidationError{Field: "title", Message: "title is required"}
	}
	if utf8.RuneCountInString(title) > 200 {
		return NormalizedCreate{}, &ValidationError{Field: "title", Message: "title must be at most 200 characters"}
	}

	description := ""
	if input.Description != nil {
		description = *input.Description
	}
	if utf8.RuneCountInString(description) > 4000 {
		return NormalizedCreate{}, &ValidationError{Field: "description", Message: "description must be at most 4000 characters"}
	}

	if input.Priority == nil {
		return NormalizedCreate{}, &ValidationError{Field: "priority", Message: "priority is required"}
	}
	priority := Priority(*input.Priority)
	if !priority.Valid() {
		return NormalizedCreate{}, &ValidationError{Field: "priority", Message: "priority must be low, medium, or high"}
	}

	deviceCode, err := normalizeOptionalLabel(input.DeviceCode, "deviceCode", 64)
	if err != nil {
		return NormalizedCreate{}, err
	}
	zone, err := normalizeOptionalLabel(input.Zone, "zone", 64)
	if err != nil {
		return NormalizedCreate{}, err
	}

	if input.Position == nil {
		return NormalizedCreate{}, &ValidationError{Field: "position", Message: "position is required"}
	}
	if input.Position.X == nil || input.Position.Y == nil || input.Position.Z == nil {
		return NormalizedCreate{}, &ValidationError{Field: "position", Message: "position.x, position.y, and position.z are required"}
	}

	position := Position{X: *input.Position.X, Y: *input.Position.Y, Z: *input.Position.Z}
	if !finite(position.X) || !finite(position.Y) || !finite(position.Z) {
		return NormalizedCreate{}, &ValidationError{Field: "position", Message: "position coordinates must be finite numbers"}
	}

	return NormalizedCreate{
		Title:       title,
		Description: description,
		Priority:    priority,
		DeviceCode:  deviceCode,
		Zone:        zone,
		Position:    position,
	}, nil
}

func normalizeOptionalLabel(value *string, field string, maxLength int) (string, error) {
	if value == nil {
		return "", nil
	}

	normalized := strings.TrimSpace(*value)
	if utf8.RuneCountInString(normalized) > maxLength {
		return "", &ValidationError{
			Field:   field,
			Message: fmt.Sprintf("%s must be at most %d characters", field, maxLength),
		}
	}
	return normalized, nil
}

func NormalizeIdempotencyKey(value string) (string, error) {
	if strings.TrimSpace(value) == "" {
		return "", &ValidationError{Field: "Idempotency-Key", Message: "Idempotency-Key is required"}
	}

	key, err := uuid.Parse(value)
	if err != nil {
		return "", &ValidationError{Field: "Idempotency-Key", Message: "Idempotency-Key must be a UUID"}
	}
	return key.String(), nil
}

func ValidateStatus(value string) (Status, error) {
	status := Status(value)
	if !status.Valid() {
		return "", &ValidationError{
			Field:   "status",
			Message: fmt.Sprintf("status must be %s, %s, or %s", StatusOpen, StatusInProgress, StatusResolved),
		}
	}
	return status, nil
}

func finite(value float64) bool {
	return !math.IsNaN(value) && !math.IsInf(value, 0)
}
