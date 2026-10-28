package store

import (
	"context"
	"database/sql"
	_ "embed"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"time"

	_ "modernc.org/sqlite"

	"unity-inspection/backend/internal/issue"
)

//go:embed schema.sql
var schemaSQL string

type SQLiteStore struct {
	db *sql.DB
}

func Open(ctx context.Context, path string) (*SQLiteStore, error) {
	if path == "" {
		return nil, errors.New("database path is required")
	}

	if path != ":memory:" {
		directory := filepath.Dir(path)
		if directory != "." {
			if err := os.MkdirAll(directory, 0o755); err != nil {
				return nil, fmt.Errorf("create database directory: %w", err)
			}
		}
	}

	db, err := sql.Open("sqlite", path)
	if err != nil {
		return nil, fmt.Errorf("open sqlite: %w", err)
	}

	// One connection makes writes deterministic for this single-process MVP.
	db.SetMaxOpenConns(1)
	db.SetMaxIdleConns(1)

	store := &SQLiteStore{db: db}
	closeOnError := func(cause error) (*SQLiteStore, error) {
		_ = db.Close()
		return nil, cause
	}

	pingCtx, cancel := context.WithTimeout(ctx, 5*time.Second)
	defer cancel()
	if err := db.PingContext(pingCtx); err != nil {
		return closeOnError(fmt.Errorf("ping sqlite: %w", err))
	}

	for _, pragma := range []string{
		"PRAGMA journal_mode=WAL;",
		"PRAGMA busy_timeout=5000;",
		"PRAGMA synchronous=NORMAL;",
		"PRAGMA foreign_keys=ON;",
	} {
		if _, err := db.ExecContext(ctx, pragma); err != nil {
			return closeOnError(fmt.Errorf("configure sqlite with %q: %w", pragma, err))
		}
	}

	if _, err := db.ExecContext(ctx, schemaSQL); err != nil {
		return closeOnError(fmt.Errorf("initialize sqlite schema: %w", err))
	}

	return store, nil
}

func (s *SQLiteStore) Close() error {
	return s.db.Close()
}

func (s *SQLiteStore) Ping(ctx context.Context) error {
	var value int
	return s.db.QueryRowContext(ctx, "SELECT 1").Scan(&value)
}

func (s *SQLiteStore) CreateOrGet(
	ctx context.Context,
	item issue.Issue,
	requestKey string,
	requestHash string,
) (issue.Issue, bool, error) {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return issue.Issue{}, false, fmt.Errorf("begin create transaction: %w", err)
	}
	defer func() { _ = tx.Rollback() }()

	existing, existingHash, err := getByRequestKey(ctx, tx, requestKey)
	switch {
	case err == nil:
		if existingHash != requestHash {
			return issue.Issue{}, false, issue.ErrIdempotencyConflict
		}
		if err := tx.Commit(); err != nil {
			return issue.Issue{}, false, fmt.Errorf("commit idempotent read: %w", err)
		}
		return existing, false, nil
	case !errors.Is(err, sql.ErrNoRows):
		return issue.Issue{}, false, fmt.Errorf("find idempotency key: %w", err)
	}

	_, err = tx.ExecContext(ctx, `
		INSERT INTO issues (
			id, title, description, priority, status,
			x, y, z, created_at, updated_at, request_key, request_hash
		) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
		item.ID,
		item.Title,
		item.Description,
		string(item.Priority),
		string(item.Status),
		item.Position.X,
		item.Position.Y,
		item.Position.Z,
		item.CreatedAt,
		item.UpdatedAt,
		requestKey,
		requestHash,
	)
	if err != nil {
		return issue.Issue{}, false, fmt.Errorf("insert issue: %w", err)
	}

	if err := tx.Commit(); err != nil {
		return issue.Issue{}, false, fmt.Errorf("commit create transaction: %w", err)
	}
	return item, true, nil
}

func (s *SQLiteStore) List(ctx context.Context) ([]issue.Issue, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT id, title, description, priority, status,
		       x, y, z, created_at, updated_at
		FROM issues
		ORDER BY created_at DESC, id DESC`)
	if err != nil {
		return nil, fmt.Errorf("list issues: %w", err)
	}
	defer rows.Close()

	items := make([]issue.Issue, 0)
	for rows.Next() {
		item, err := scanIssue(rows)
		if err != nil {
			return nil, fmt.Errorf("scan issue: %w", err)
		}
		items = append(items, item)
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("iterate issues: %w", err)
	}
	return items, nil
}

func (s *SQLiteStore) UpdateStatus(
	ctx context.Context,
	id string,
	status issue.Status,
	updatedAt string,
) (issue.Issue, error) {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return issue.Issue{}, fmt.Errorf("begin status transaction: %w", err)
	}
	defer func() { _ = tx.Rollback() }()

	result, err := tx.ExecContext(ctx,
		"UPDATE issues SET status = ?, updated_at = ? WHERE id = ?",
		string(status), updatedAt, id,
	)
	if err != nil {
		return issue.Issue{}, fmt.Errorf("update issue status: %w", err)
	}

	affected, err := result.RowsAffected()
	if err != nil {
		return issue.Issue{}, fmt.Errorf("read status update result: %w", err)
	}
	if affected == 0 {
		return issue.Issue{}, issue.ErrNotFound
	}

	item, err := getByID(ctx, tx, id)
	if err != nil {
		return issue.Issue{}, fmt.Errorf("read updated issue: %w", err)
	}

	if err := tx.Commit(); err != nil {
		return issue.Issue{}, fmt.Errorf("commit status transaction: %w", err)
	}
	return item, nil
}

type scanner interface {
	Scan(dest ...any) error
}

func scanIssue(row scanner) (issue.Issue, error) {
	var item issue.Issue
	var priority string
	var status string
	err := row.Scan(
		&item.ID,
		&item.Title,
		&item.Description,
		&priority,
		&status,
		&item.Position.X,
		&item.Position.Y,
		&item.Position.Z,
		&item.CreatedAt,
		&item.UpdatedAt,
	)
	item.Priority = issue.Priority(priority)
	item.Status = issue.Status(status)
	return item, err
}

func getByRequestKey(
	ctx context.Context,
	tx *sql.Tx,
	requestKey string,
) (issue.Issue, string, error) {
	row := tx.QueryRowContext(ctx, `
		SELECT id, title, description, priority, status,
		       x, y, z, created_at, updated_at, request_hash
		FROM issues WHERE request_key = ?`, requestKey)
	var item issue.Issue
	var priority string
	var status string
	var hash string
	err := row.Scan(
		&item.ID,
		&item.Title,
		&item.Description,
		&priority,
		&status,
		&item.Position.X,
		&item.Position.Y,
		&item.Position.Z,
		&item.CreatedAt,
		&item.UpdatedAt,
		&hash,
	)
	item.Priority = issue.Priority(priority)
	item.Status = issue.Status(status)
	return item, hash, err
}

func getByID(
	ctx context.Context,
	tx *sql.Tx,
	id string,
) (issue.Issue, error) {
	row := tx.QueryRowContext(ctx, `
		SELECT id, title, description, priority, status,
		       x, y, z, created_at, updated_at
		FROM issues WHERE id = ?`, id)
	return scanIssue(row)
}
