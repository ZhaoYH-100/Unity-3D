package store

import (
	"context"
	"database/sql"
	"path/filepath"
	"sync"
	"testing"

	"github.com/google/uuid"

	"unity-inspection/backend/internal/issue"
)

func TestPersistenceAcrossReopen(t *testing.T) {
	t.Parallel()

	ctx := context.Background()
	dbPath := filepath.Join(t.TempDir(), "issues.db")

	first, err := Open(ctx, dbPath)
	if err != nil {
		t.Fatalf("open first store: %v", err)
	}
	service := issue.NewService(first)
	item, created, err := service.Create(
		ctx,
		"0f2c7b23-7dff-4f1e-a6ab-52ef441b25c8",
		validCreateInput(),
	)
	if err != nil {
		t.Fatalf("create issue: %v", err)
	}
	if !created || item.Status != issue.StatusOpen {
		t.Fatalf("unexpected create result: created=%v status=%s", created, item.Status)
	}
	if err := first.Close(); err != nil {
		t.Fatalf("close first store: %v", err)
	}

	second, err := Open(ctx, dbPath)
	if err != nil {
		t.Fatalf("reopen store: %v", err)
	}
	t.Cleanup(func() { _ = second.Close() })

	items, err := issue.NewService(second).List(ctx)
	if err != nil {
		t.Fatalf("list persisted issues: %v", err)
	}
	if len(items) != 1 || items[0].ID != item.ID {
		t.Fatalf("persistence mismatch: %#v", items)
	}
}

func TestIdempotentCreateReturnsExistingIssue(t *testing.T) {
	t.Parallel()

	ctx := context.Background()
	repository, err := Open(ctx, filepath.Join(t.TempDir(), "issues.db"))
	if err != nil {
		t.Fatalf("open store: %v", err)
	}
	t.Cleanup(func() { _ = repository.Close() })

	service := issue.NewService(repository)
	key := "0f2c7b23-7dff-4f1e-a6ab-52ef441b25c8"
	input := validCreateInput()

	first, firstCreated, err := service.Create(ctx, key, input)
	if err != nil {
		t.Fatalf("create first issue: %v", err)
	}
	second, secondCreated, err := service.Create(ctx, key, input)
	if err != nil {
		t.Fatalf("repeat create: %v", err)
	}
	if !firstCreated || secondCreated {
		t.Fatalf("unexpected created flags: first=%v second=%v", firstCreated, secondCreated)
	}
	if first.ID != second.ID {
		t.Fatalf("expected same ID, got %s and %s", first.ID, second.ID)
	}

	items, err := service.List(ctx)
	if err != nil {
		t.Fatalf("list issues: %v", err)
	}
	if len(items) != 1 {
		t.Fatalf("expected one issue, got %d", len(items))
	}
}

func TestConcurrentDistinctCreates(t *testing.T) {
	t.Parallel()

	ctx := context.Background()
	repository, err := Open(ctx, filepath.Join(t.TempDir(), "issues.db"))
	if err != nil {
		t.Fatalf("open store: %v", err)
	}
	t.Cleanup(func() { _ = repository.Close() })

	service := issue.NewService(repository)
	const requestCount = 20

	ids := make(chan string, requestCount)
	errorsChannel := make(chan error, requestCount)
	var waitGroup sync.WaitGroup
	for range requestCount {
		waitGroup.Add(1)
		go func() {
			defer waitGroup.Done()
			item, created, err := service.Create(ctx, uuid.NewString(), validCreateInput())
			if err != nil {
				errorsChannel <- err
				return
			}
			if !created {
				errorsChannel <- issue.ErrIdempotencyConflict
				return
			}
			ids <- item.ID
		}()
	}
	waitGroup.Wait()
	close(errorsChannel)
	close(ids)

	for err := range errorsChannel {
		t.Fatalf("concurrent create failed: %v", err)
	}

	uniqueIDs := make(map[string]struct{}, requestCount)
	for id := range ids {
		uniqueIDs[id] = struct{}{}
	}
	if len(uniqueIDs) != requestCount {
		t.Fatalf("expected %d unique IDs, got %d", requestCount, len(uniqueIDs))
	}

	items, err := service.List(ctx)
	if err != nil {
		t.Fatalf("list issues: %v", err)
	}
	if len(items) != requestCount {
		t.Fatalf("expected %d persisted issues, got %d", requestCount, len(items))
	}
}

func TestListOrderAtEqualTimestamp(t *testing.T) {
	t.Parallel()

	ctx := context.Background()
	repository, err := Open(ctx, filepath.Join(t.TempDir(), "issues.db"))
	if err != nil {
		t.Fatalf("open store: %v", err)
	}
	t.Cleanup(func() { _ = repository.Close() })

	const createdAt = "2026-10-27T12:00:00.000000000Z"
	rows := []struct {
		id         string
		requestKey string
	}{
		{id: "00000000-0000-4000-8000-000000000001", requestKey: "10000000-0000-4000-8000-000000000001"},
		{id: "00000000-0000-4000-8000-000000000002", requestKey: "10000000-0000-4000-8000-000000000002"},
	}
	for _, row := range rows {
		_, err := repository.db.ExecContext(ctx, `
			INSERT INTO issues (
				id, title, description, priority, status,
				x, y, z, created_at, updated_at, request_key, request_hash
			) VALUES (?, 'order test', '', 'medium', 'open', 0, 0, 0, ?, ?, ?, 'hash')`,
			row.id,
			createdAt,
			createdAt,
			row.requestKey,
		)
		if err != nil {
			t.Fatalf("insert fixture: %v", err)
		}
	}

	items, err := repository.List(ctx)
	if err != nil {
		t.Fatalf("list issues: %v", err)
	}
	if len(items) != 2 || items[0].ID != rows[1].id || items[1].ID != rows[0].id {
		t.Fatalf("unexpected equal-timestamp order: %#v", items)
	}
}

func TestOpenMigratesLegacyIssueTable(t *testing.T) {
	t.Parallel()

	ctx := context.Background()
	dbPath := filepath.Join(t.TempDir(), "legacy.db")
	db, err := sql.Open("sqlite", dbPath)
	if err != nil {
		t.Fatalf("open legacy database: %v", err)
	}
	_, err = db.ExecContext(ctx, `
		CREATE TABLE issues (
			id TEXT PRIMARY KEY,
			title TEXT NOT NULL,
			description TEXT NOT NULL DEFAULT '',
			priority TEXT NOT NULL,
			status TEXT NOT NULL,
			x REAL NOT NULL,
			y REAL NOT NULL,
			z REAL NOT NULL,
			created_at TEXT NOT NULL,
			updated_at TEXT NOT NULL,
			request_key TEXT NOT NULL UNIQUE,
			request_hash TEXT NOT NULL
		)`)
	if err != nil {
		_ = db.Close()
		t.Fatalf("create legacy table: %v", err)
	}
	if err := db.Close(); err != nil {
		t.Fatalf("close legacy database: %v", err)
	}

	repository, err := Open(ctx, dbPath)
	if err != nil {
		t.Fatalf("open migrated store: %v", err)
	}
	t.Cleanup(func() { _ = repository.Close() })

	items, err := repository.List(ctx)
	if err != nil {
		t.Fatalf("list migrated issues: %v", err)
	}
	if len(items) != 0 {
		t.Fatalf("expected no legacy rows, got %#v", items)
	}
}

func validCreateInput() issue.CreateInput {
	title := "cabinet temperature warning"
	description := "right side is hot"
	priority := "high"
	x, y, z := 1.25, 0.8, 2.4
	return issue.CreateInput{
		Title:       &title,
		Description: &description,
		Priority:    &priority,
		Position: &issue.PositionInput{
			X: &x,
			Y: &y,
			Z: &z,
		},
	}
}
