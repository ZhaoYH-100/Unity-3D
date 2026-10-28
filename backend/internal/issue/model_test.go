package issue

import (
	"errors"
	"testing"
)

func TestNormalizeCreate(t *testing.T) {
	t.Parallel()

	title := "  temperature warning  "
	description := ""
	priority := "high"
	x, y, z := 1.25, 0.8, 2.4

	normalized, err := NormalizeCreate(CreateInput{
		Title:       &title,
		Description: &description,
		Priority:    &priority,
		Position: &PositionInput{
			X: &x,
			Y: &y,
			Z: &z,
		},
	})
	if err != nil {
		t.Fatalf("NormalizeCreate returned error: %v", err)
	}
	if normalized.Title != "temperature warning" {
		t.Fatalf("unexpected normalized title %q", normalized.Title)
	}
	if normalized.Priority != PriorityHigh {
		t.Fatalf("unexpected priority %q", normalized.Priority)
	}
}

func TestNormalizeCreateRejectsInvalidInput(t *testing.T) {
	t.Parallel()

	validTitle := "valid title"
	space := "   "
	validPriority := "medium"
	badPriority := "urgent"
	x := 1.0
	incompletePosition := PositionInput{X: &x}

	tests := []struct {
		name    string
		input   CreateInput
		wantErr bool
	}{
		{
			name:    "missing title",
			input:   CreateInput{},
			wantErr: true,
		},
		{
			name: "empty title",
			input: CreateInput{
				Title:    &space,
				Priority: &validPriority,
			},
			wantErr: true,
		},
		{
			name: "invalid priority",
			input: CreateInput{
				Title:    &validTitle,
				Priority: &badPriority,
			},
			wantErr: true,
		},
		{
			name: "missing position",
			input: CreateInput{
				Title:    &validTitle,
				Priority: &validPriority,
			},
			wantErr: true,
		},
		{
			name: "incomplete position",
			input: CreateInput{
				Title:    &validTitle,
				Priority: &validPriority,
				Position: &incompletePosition,
			},
			wantErr: true,
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			_, err := NormalizeCreate(test.input)
			if !test.wantErr {
				return
			}
			var validationError *ValidationError
			if !errors.As(err, &validationError) {
				t.Fatalf("expected ValidationError, got %v", err)
			}
		})
	}
}

func TestValidateStatus(t *testing.T) {
	t.Parallel()

	for _, status := range []string{"open", "in_progress", "resolved"} {
		parsed, err := ValidateStatus(status)
		if err != nil {
			t.Fatalf("ValidateStatus(%q) returned error: %v", status, err)
		}
		if string(parsed) != status {
			t.Fatalf("ValidateStatus(%q) returned %q", status, parsed)
		}
	}

	if _, err := ValidateStatus("done"); err == nil {
		t.Fatal("expected invalid status to fail")
	}
}

func TestNormalizeIdempotencyKey(t *testing.T) {
	t.Parallel()

	key := "0f2c7b23-7dff-4f1e-a6ab-52ef441b25c8"
	normalized, err := NormalizeIdempotencyKey(key)
	if err != nil {
		t.Fatalf("NormalizeIdempotencyKey returned error: %v", err)
	}
	if normalized != key {
		t.Fatalf("unexpected normalized key %q", normalized)
	}

	if _, err := NormalizeIdempotencyKey("not-a-uuid"); err == nil {
		t.Fatal("expected invalid idempotency key to fail")
	}
}
