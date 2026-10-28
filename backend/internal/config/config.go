package config

import (
	"fmt"
	"os"
	"strconv"
	"strings"
)

type Config struct {
	Port        int
	DBPath      string
	CORSOrigins []string
}

func Load() (Config, error) {
	portValue := getEnv("PORT", "8080")
	port, err := strconv.Atoi(portValue)
	if err != nil || port < 1 || port > 65535 {
		return Config{}, fmt.Errorf("PORT must be an integer between 1 and 65535")
	}

	dbPath := getEnv("DB_PATH", "./data/issues.db")
	if strings.TrimSpace(dbPath) == "" {
		return Config{}, fmt.Errorf("DB_PATH must not be empty")
	}

	rawOrigins := getEnv(
		"CORS_ORIGINS",
		"http://localhost:5173,http://127.0.0.1:5173",
	)
	origins := make([]string, 0)
	for _, origin := range strings.Split(rawOrigins, ",") {
		trimmed := strings.TrimSpace(origin)
		if trimmed != "" {
			origins = append(origins, trimmed)
		}
	}
	if len(origins) == 0 {
		return Config{}, fmt.Errorf("CORS_ORIGINS must contain at least one origin")
	}

	return Config{
		Port:        port,
		DBPath:      dbPath,
		CORSOrigins: origins,
	}, nil
}

func getEnv(name, fallback string) string {
	if value, ok := os.LookupEnv(name); ok && value != "" {
		return value
	}
	return fallback
}
