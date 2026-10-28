package httpapi

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"sync"
	"testing"

	"github.com/google/uuid"

	"unity-inspection/backend/internal/issue"
	"unity-inspection/backend/internal/store"
)

const allowedOrigin = "http://localhost:5173"

func TestIssueWorkflow(t *testing.T) {
	t.Parallel()

	handler := newTestHandler(t)
	key := uuid.NewString()

	createResponse := performJSONRequest(
		t,
		handler,
		http.MethodPost,
		"/api/issues",
		key,
		createPayload("cabinet temperature warning", "high", 1.25, 0.8, 2.4),
	)
	if createResponse.Code != http.StatusCreated {
		t.Fatalf("create status = %d, body = %s", createResponse.Code, createResponse.Body.String())
	}

	var created issue.Issue
	decodeResponse(t, createResponse, &created)
	if created.Status != issue.StatusOpen {
		t.Fatalf("expected open issue, got %s", created.Status)
	}
	if created.Priority != issue.PriorityHigh {
		t.Fatalf("expected high priority, got %s", created.Priority)
	}
	if created.DeviceCode != "CAB-A" || created.Zone != "配电区" {
		t.Fatalf("unexpected location metadata: %#v", created)
	}

	listResponse := performRequest(t, handler, http.MethodGet, "/api/issues", "", nil, "")
	if listResponse.Code != http.StatusOK {
		t.Fatalf("list status = %d", listResponse.Code)
	}
	var listBody struct {
		Items []issue.Issue `json:"items"`
	}
	decodeResponse(t, listResponse, &listBody)
	if len(listBody.Items) != 1 || listBody.Items[0].ID != created.ID {
		t.Fatalf("unexpected list body: %#v", listBody)
	}

	for _, status := range []issue.Status{
		issue.StatusInProgress,
		issue.StatusResolved,
		issue.StatusOpen,
	} {
		statusResponse := performRequest(
			t,
			handler,
			http.MethodPatch,
			"/api/issues/"+created.ID+"/status",
			"",
			[]byte(fmt.Sprintf(`{"status":%q}`, status)),
			"application/json",
		)
		if statusResponse.Code != http.StatusOK {
			t.Fatalf("patch %s status = %d, body = %s", status, statusResponse.Code, statusResponse.Body.String())
		}
		var updated issue.Issue
		decodeResponse(t, statusResponse, &updated)
		if updated.Status != status {
			t.Fatalf("expected %s, got %s", status, updated.Status)
		}
	}

	deleteResponse := performRequest(
		t,
		handler,
		http.MethodDelete,
		"/api/issues/"+created.ID,
		"",
		nil,
		"",
	)
	if deleteResponse.Code != http.StatusNoContent {
		t.Fatalf("delete status = %d, body = %s", deleteResponse.Code, deleteResponse.Body.String())
	}

	missingResponse := performRequest(
		t,
		handler,
		http.MethodDelete,
		"/api/issues/"+created.ID,
		"",
		nil,
		"",
	)
	if missingResponse.Code != http.StatusNotFound {
		t.Fatalf("second delete status = %d, body = %s", missingResponse.Code, missingResponse.Body.String())
	}
}

func TestIdempotentCreate(t *testing.T) {
	t.Parallel()

	handler := newTestHandler(t)
	key := uuid.NewString()
	payload := createPayload("temperature warning", "medium", 1, 2, 3)

	first := performJSONRequest(t, handler, http.MethodPost, "/api/issues", key, payload)
	second := performJSONRequest(t, handler, http.MethodPost, "/api/issues", key, payload)
	if first.Code != http.StatusCreated || second.Code != http.StatusOK {
		t.Fatalf("unexpected statuses: first=%d second=%d", first.Code, second.Code)
	}

	var firstIssue, secondIssue issue.Issue
	decodeResponse(t, first, &firstIssue)
	decodeResponse(t, second, &secondIssue)
	if firstIssue.ID != secondIssue.ID {
		t.Fatalf("idempotency returned different IDs: %s and %s", firstIssue.ID, secondIssue.ID)
	}

	conflict := performJSONRequest(
		t,
		handler,
		http.MethodPost,
		"/api/issues",
		key,
		createPayload("different payload", "medium", 1, 2, 3),
	)
	if conflict.Code != http.StatusConflict {
		t.Fatalf("expected conflict, got %d: %s", conflict.Code, conflict.Body.String())
	}
}

func TestNullDescriptionDefaultsToEmpty(t *testing.T) {
	t.Parallel()

	handler := newTestHandler(t)
	response := performJSONRequest(
		t,
		handler,
		http.MethodPost,
		"/api/issues",
		uuid.NewString(),
		[]byte(`{"title":"warning","description":null,"priority":"low","position":{"x":1,"y":2,"z":3}}`),
	)
	if response.Code != http.StatusCreated {
		t.Fatalf("create status = %d, body = %s", response.Code, response.Body.String())
	}

	var item issue.Issue
	decodeResponse(t, response, &item)
	if item.Description != "" {
		t.Fatalf("expected empty description, got %q", item.Description)
	}
}

func TestValidationFailures(t *testing.T) {
	t.Parallel()

	handler := newTestHandler(t)
	key := uuid.NewString()
	validPosition := `{"x":1,"y":2,"z":3}`

	tests := []struct {
		name        string
		key         string
		body        string
		contentType string
		wantStatus  int
	}{
		{
			name:        "missing title",
			key:         key,
			body:        fmt.Sprintf(`{"priority":"high","position":%s}`, validPosition),
			contentType: "application/json",
			wantStatus:  http.StatusBadRequest,
		},
		{
			name:        "null position",
			key:         key,
			body:        `{"title":"warning","priority":"high","position":null}`,
			contentType: "application/json",
			wantStatus:  http.StatusBadRequest,
		},
		{
			name:        "incomplete position",
			key:         key,
			body:        `{"title":"warning","priority":"high","position":{"x":1,"y":2}}`,
			contentType: "application/json",
			wantStatus:  http.StatusBadRequest,
		},
		{
			name:        "invalid priority",
			key:         key,
			body:        fmt.Sprintf(`{"title":"warning","priority":"urgent","position":%s}`, validPosition),
			contentType: "application/json",
			wantStatus:  http.StatusBadRequest,
		},
		{
			name:        "server owned field",
			key:         key,
			body:        fmt.Sprintf(`{"title":"warning","priority":"high","position":%s,"status":"resolved"}`, validPosition),
			contentType: "application/json",
			wantStatus:  http.StatusBadRequest,
		},
		{
			name:        "malformed json",
			key:         key,
			body:        `{"title":`,
			contentType: "application/json",
			wantStatus:  http.StatusBadRequest,
		},
		{
			name:        "missing idempotency key",
			key:         "",
			body:        fmt.Sprintf(`{"title":"warning","priority":"high","position":%s}`, validPosition),
			contentType: "application/json",
			wantStatus:  http.StatusBadRequest,
		},
		{
			name:        "wrong content type",
			key:         key,
			body:        fmt.Sprintf(`{"title":"warning","priority":"high","position":%s}`, validPosition),
			contentType: "text/plain",
			wantStatus:  http.StatusUnsupportedMediaType,
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			response := performRequest(
				t,
				handler,
				http.MethodPost,
				"/api/issues",
				test.key,
				[]byte(test.body),
				test.contentType,
			)
			if response.Code != test.wantStatus {
				t.Fatalf("status = %d, want %d, body = %s", response.Code, test.wantStatus, response.Body.String())
			}
		})
	}
}

func TestRequestTooLarge(t *testing.T) {
	t.Parallel()

	handler := newTestHandler(t)
	body := []byte(`{"title":"` + strings.Repeat("a", 1<<20) + `","priority":"high","position":{"x":1,"y":2,"z":3}}`)
	response := performRequest(
		t,
		handler,
		http.MethodPost,
		"/api/issues",
		uuid.NewString(),
		body,
		"application/json",
	)
	if response.Code != http.StatusRequestEntityTooLarge {
		t.Fatalf("status = %d, want %d", response.Code, http.StatusRequestEntityTooLarge)
	}
}

func TestStatusValidationAndNotFound(t *testing.T) {
	t.Parallel()

	handler := newTestHandler(t)
	tests := []struct {
		name   string
		id     string
		body   string
		status int
	}{
		{
			name:   "missing status",
			id:     uuid.NewString(),
			body:   `{}`,
			status: http.StatusBadRequest,
		},
		{
			name:   "invalid status",
			id:     uuid.NewString(),
			body:   `{"status":"done"}`,
			status: http.StatusBadRequest,
		},
		{
			name:   "unknown field",
			id:     uuid.NewString(),
			body:   `{"status":"open","title":"changed"}`,
			status: http.StatusBadRequest,
		},
		{
			name:   "not found",
			id:     uuid.NewString(),
			body:   `{"status":"open"}`,
			status: http.StatusNotFound,
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			response := performRequest(
				t,
				handler,
				http.MethodPatch,
				"/api/issues/"+test.id+"/status",
				"",
				[]byte(test.body),
				"application/json",
			)
			if response.Code != test.status {
				t.Fatalf("status = %d, want %d, body = %s", response.Code, test.status, response.Body.String())
			}
		})
	}
}

func TestCORSPreflight(t *testing.T) {
	t.Parallel()

	handler := newTestHandler(t)
	request := httptest.NewRequest(http.MethodOptions, "/api/issues", nil)
	request.Header.Set("Origin", allowedOrigin)
	request.Header.Set("Access-Control-Request-Method", "PATCH")
	request.Header.Set("Access-Control-Request-Headers", "content-type")
	response := httptest.NewRecorder()

	handler.ServeHTTP(response, request)
	if response.Code != http.StatusNoContent {
		t.Fatalf("preflight status = %d", response.Code)
	}
	if got := response.Header().Get("Access-Control-Allow-Origin"); got != allowedOrigin {
		t.Fatalf("allow origin = %q", got)
	}
	if got := response.Header().Get("Access-Control-Allow-Methods"); !strings.Contains(got, "PATCH") ||
		!strings.Contains(got, "DELETE") {
		t.Fatalf("allow methods = %q", got)
	}
	if got := response.Header().Get("Access-Control-Allow-Headers"); !strings.Contains(got, "Idempotency-Key") {
		t.Fatalf("allow headers = %q", got)
	}

	deniedRequest := httptest.NewRequest(http.MethodOptions, "/api/issues", nil)
	deniedRequest.Header.Set("Origin", "https://untrusted.example")
	deniedResponse := httptest.NewRecorder()
	handler.ServeHTTP(deniedResponse, deniedRequest)
	if deniedResponse.Code != http.StatusForbidden {
		t.Fatalf("denied preflight status = %d", deniedResponse.Code)
	}
	if got := deniedResponse.Header().Get("Access-Control-Allow-Origin"); got != "" {
		t.Fatalf("denied origin received allow header %q", got)
	}
}

func TestConcurrentSameIdempotencyKey(t *testing.T) {
	t.Parallel()

	server := httptest.NewServer(newTestHandler(t))
	t.Cleanup(server.Close)

	key := uuid.NewString()
	payload := createPayload("concurrent warning", "medium", 1, 2, 3)
	const requestCount = 20

	statuses := make(chan int, requestCount)
	ids := make(chan string, requestCount)
	errorsChannel := make(chan error, requestCount)

	var waitGroup sync.WaitGroup
	for range requestCount {
		waitGroup.Add(1)
		go func() {
			defer waitGroup.Done()
			request, err := http.NewRequest(http.MethodPost, server.URL+"/api/issues", bytes.NewReader(payload))
			if err != nil {
				errorsChannel <- err
				return
			}
			request.Header.Set("Content-Type", "application/json")
			request.Header.Set("Idempotency-Key", key)

			response, err := http.DefaultClient.Do(request)
			if err != nil {
				errorsChannel <- err
				return
			}
			defer response.Body.Close()

			var item issue.Issue
			if err := json.NewDecoder(response.Body).Decode(&item); err != nil {
				errorsChannel <- err
				return
			}
			statuses <- response.StatusCode
			ids <- item.ID
		}()
	}
	waitGroup.Wait()
	close(errorsChannel)
	close(statuses)
	close(ids)

	for err := range errorsChannel {
		t.Fatalf("concurrent request failed: %v", err)
	}

	statusCounts := map[int]int{}
	for status := range statuses {
		statusCounts[status]++
	}
	if statusCounts[http.StatusCreated] != 1 || statusCounts[http.StatusOK] != requestCount-1 {
		t.Fatalf("unexpected status counts: %#v", statusCounts)
	}

	uniqueIDs := make(map[string]struct{})
	for id := range ids {
		uniqueIDs[id] = struct{}{}
	}
	if len(uniqueIDs) != 1 {
		t.Fatalf("expected one issue ID, got %d", len(uniqueIDs))
	}
}

func newTestHandler(t *testing.T) http.Handler {
	t.Helper()

	ctx := context.Background()
	dbPath := filepath.Join(t.TempDir(), "issues.db")
	repository, err := store.Open(ctx, dbPath)
	if err != nil {
		t.Fatalf("open test store: %v", err)
	}
	t.Cleanup(func() { _ = repository.Close() })

	logger := slog.New(slog.NewTextHandler(io.Discard, nil))
	return New(issue.NewService(repository), []string{allowedOrigin}, logger)
}

func performJSONRequest(
	t *testing.T,
	handler http.Handler,
	method string,
	path string,
	key string,
	body []byte,
) *httptest.ResponseRecorder {
	t.Helper()
	return performRequest(t, handler, method, path, key, body, "application/json")
}

func performRequest(
	t *testing.T,
	handler http.Handler,
	method string,
	path string,
	key string,
	body []byte,
	contentType string,
) *httptest.ResponseRecorder {
	t.Helper()

	request := httptest.NewRequest(method, path, bytes.NewReader(body))
	if contentType != "" {
		request.Header.Set("Content-Type", contentType)
	}
	if key != "" {
		request.Header.Set("Idempotency-Key", key)
	}
	request.Header.Set("Origin", allowedOrigin)

	response := httptest.NewRecorder()
	handler.ServeHTTP(response, request)
	return response
}

func decodeResponse(t *testing.T, response *httptest.ResponseRecorder, destination any) {
	t.Helper()
	if err := json.NewDecoder(response.Body).Decode(destination); err != nil {
		t.Fatalf("decode response: %v, body = %s", err, response.Body.String())
	}
}

func createPayload(title, priority string, x, y, z float64) []byte {
	return []byte(fmt.Sprintf(
		`{"title":%q,"description":"test issue","priority":%q,"deviceCode":"CAB-A","zone":"配电区","position":{"x":%v,"y":%v,"z":%v}}`,
		title,
		priority,
		x,
		y,
		z,
	))
}
