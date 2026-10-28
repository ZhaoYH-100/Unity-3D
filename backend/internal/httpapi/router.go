package httpapi

import (
	"log/slog"
	"net/http"

	"unity-inspection/backend/internal/issue"
)

type API struct {
	service *issue.Service
	logger  *slog.Logger
}

func New(service *issue.Service, allowedOrigins []string, logger *slog.Logger) http.Handler {
	api := &API{service: service, logger: logger}

	mux := http.NewServeMux()
	mux.HandleFunc("GET /healthz", api.health)
	mux.HandleFunc("GET /api/issues", api.listIssues)
	mux.HandleFunc("POST /api/issues", api.createIssue)
	mux.HandleFunc("PATCH /api/issues/{id}/status", api.updateIssueStatus)
	mux.HandleFunc("DELETE /api/issues/{id}", api.deleteIssue)

	handler := api.withCORS(allowedOrigins, mux)
	handler = api.logRequests(handler)
	return api.recoverPanics(handler)
}
