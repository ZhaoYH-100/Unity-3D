package httpapi

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"mime"
	"net/http"

	"unity-inspection/backend/internal/issue"
)

const maxRequestBodyBytes int64 = 1 << 20

func (a *API) health(w http.ResponseWriter, r *http.Request) {
	if err := a.service.Health(r.Context()); err != nil {
		a.logger.Error("database health check failed", "error", err)
		writeAPIError(w, http.StatusServiceUnavailable, "DATABASE_UNAVAILABLE", "database is unavailable")
		return
	}
	writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
}

func (a *API) listIssues(w http.ResponseWriter, r *http.Request) {
	items, err := a.service.List(r.Context())
	if err != nil {
		a.logger.Error("list issues failed", "error", err)
		writeAPIError(w, http.StatusInternalServerError, "INTERNAL", "internal server error")
		return
	}
	writeJSON(w, http.StatusOK, struct {
		Items []issue.Issue `json:"items"`
	}{Items: items})
}

func (a *API) createIssue(w http.ResponseWriter, r *http.Request) {
	var input issue.CreateInput
	if apiErr := decodeJSON(w, r, &input); apiErr != nil {
		writeAPIError(w, apiErr.Status, apiErr.Code, apiErr.Message)
		return
	}

	item, created, err := a.service.Create(r.Context(), r.Header.Get("Idempotency-Key"), input)
	if err != nil {
		a.writeServiceError(w, err)
		return
	}

	status := http.StatusOK
	if created {
		status = http.StatusCreated
	}
	writeJSON(w, status, item)
}

func (a *API) updateIssueStatus(w http.ResponseWriter, r *http.Request) {
	var input struct {
		Status *string `json:"status"`
	}
	if apiErr := decodeJSON(w, r, &input); apiErr != nil {
		writeAPIError(w, apiErr.Status, apiErr.Code, apiErr.Message)
		return
	}
	if input.Status == nil {
		writeAPIError(w, http.StatusBadRequest, "INVALID_ARGUMENT", "status is required")
		return
	}

	item, err := a.service.UpdateStatus(r.Context(), r.PathValue("id"), *input.Status)
	if err != nil {
		a.writeServiceError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, item)
}

func (a *API) writeServiceError(w http.ResponseWriter, err error) {
	var validationError *issue.ValidationError
	switch {
	case errors.As(err, &validationError):
		writeAPIError(w, http.StatusBadRequest, "INVALID_ARGUMENT", validationError.Message)
	case errors.Is(err, issue.ErrIdempotencyConflict):
		writeAPIError(w, http.StatusConflict, "IDEMPOTENCY_CONFLICT", err.Error())
	case errors.Is(err, issue.ErrNotFound):
		writeAPIError(w, http.StatusNotFound, "NOT_FOUND", "issue was not found")
	default:
		a.logger.Error("issue operation failed", "error", err)
		writeAPIError(w, http.StatusInternalServerError, "INTERNAL", "internal server error")
	}
}

type apiError struct {
	Status  int
	Code    string
	Message string
}

func decodeJSON(w http.ResponseWriter, r *http.Request, destination any) *apiError {
	mediaType, _, err := mime.ParseMediaType(r.Header.Get("Content-Type"))
	if err != nil || mediaType != "application/json" {
		return &apiError{
			Status:  http.StatusUnsupportedMediaType,
			Code:    "UNSUPPORTED_MEDIA_TYPE",
			Message: "Content-Type must be application/json",
		}
	}

	r.Body = http.MaxBytesReader(w, r.Body, maxRequestBodyBytes)
	decoder := json.NewDecoder(r.Body)
	decoder.DisallowUnknownFields()

	if err := decoder.Decode(destination); err != nil {
		var maxBytesError *http.MaxBytesError
		if errors.As(err, &maxBytesError) {
			return &apiError{
				Status:  http.StatusRequestEntityTooLarge,
				Code:    "PAYLOAD_TOO_LARGE",
				Message: "request body must not exceed 1 MiB",
			}
		}
		return &apiError{
			Status:  http.StatusBadRequest,
			Code:    "INVALID_ARGUMENT",
			Message: fmt.Sprintf("invalid JSON: %s", err.Error()),
		}
	}

	if err := decoder.Decode(&struct{}{}); err != io.EOF {
		return &apiError{
			Status:  http.StatusBadRequest,
			Code:    "INVALID_ARGUMENT",
			Message: "request body must contain exactly one JSON object",
		}
	}
	return nil
}

func writeAPIError(w http.ResponseWriter, status int, code, message string) {
	writeJSON(w, status, struct {
		Error struct {
			Code    string `json:"code"`
			Message string `json:"message"`
		} `json:"error"`
	}{
		Error: struct {
			Code    string `json:"code"`
			Message string `json:"message"`
		}{
			Code:    code,
			Message: message,
		},
	})
}

func writeJSON(w http.ResponseWriter, status int, payload any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.Header().Set("Cache-Control", "no-store")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(payload)
}
