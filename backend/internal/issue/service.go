package issue

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"time"

	"github.com/google/uuid"
)

const apiTimeFormat = "2006-01-02T15:04:05.000000000Z"

type Store interface {
	Ping(ctx context.Context) error
	CreateOrGet(ctx context.Context, item Issue, requestKey, requestHash string) (Issue, bool, error)
	List(ctx context.Context) ([]Issue, error)
	UpdateStatus(ctx context.Context, id string, status Status, updatedAt string) (Issue, error)
	Delete(ctx context.Context, id string) error
}

type Service struct {
	store Store
	now   func() time.Time
	newID func() string
}

func NewService(store Store) *Service {
	return &Service{
		store: store,
		now:   time.Now,
		newID: uuid.NewString,
	}
}

func (s *Service) Create(ctx context.Context, rawKey string, input CreateInput) (Issue, bool, error) {
	requestKey, err := NormalizeIdempotencyKey(rawKey)
	if err != nil {
		return Issue{}, false, err
	}

	normalized, err := NormalizeCreate(input)
	if err != nil {
		return Issue{}, false, err
	}

	now := s.now().UTC().Format(apiTimeFormat)
	item := Issue{
		ID:          s.newID(),
		Title:       normalized.Title,
		Description: normalized.Description,
		Priority:    normalized.Priority,
		Status:      StatusOpen,
		DeviceCode:  normalized.DeviceCode,
		Zone:        normalized.Zone,
		Position:    normalized.Position,
		CreatedAt:   now,
		UpdatedAt:   now,
	}

	return s.store.CreateOrGet(ctx, item, requestKey, hashCreate(normalized))
}

func (s *Service) List(ctx context.Context) ([]Issue, error) {
	return s.store.List(ctx)
}

func (s *Service) UpdateStatus(ctx context.Context, id, rawStatus string) (Issue, error) {
	status, err := ValidateStatus(rawStatus)
	if err != nil {
		return Issue{}, err
	}

	now := s.now().UTC().Format(apiTimeFormat)
	return s.store.UpdateStatus(ctx, id, status, now)
}

func (s *Service) Delete(ctx context.Context, id string) error {
	if id == "" {
		return ErrNotFound
	}
	return s.store.Delete(ctx, id)
}

func (s *Service) Health(ctx context.Context) error {
	return s.store.Ping(ctx)
}

func hashCreate(input NormalizedCreate) string {
	payload := struct {
		Title       string   `json:"title"`
		Description string   `json:"description"`
		Priority    Priority `json:"priority"`
		DeviceCode  string   `json:"deviceCode"`
		Zone        string   `json:"zone"`
		Position    Position `json:"position"`
	}{
		Title:       input.Title,
		Description: input.Description,
		Priority:    input.Priority,
		DeviceCode:  input.DeviceCode,
		Zone:        input.Zone,
		Position:    input.Position,
	}

	encoded, err := json.Marshal(payload)
	if err != nil {
		panic("create payload contains unsupported values: " + err.Error())
	}
	sum := sha256.Sum256(encoded)
	return hex.EncodeToString(sum[:])
}
