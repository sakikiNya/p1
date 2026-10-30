package main

import (
	"crypto/rand"
	"database/sql"
	"encoding/hex"
	"encoding/json"
	"errors"
	"io"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"
)

type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}
type Issue struct {
	ID          string   `json:"id"`
	Title       string   `json:"title"`
	Description string   `json:"description"`
	Priority    string   `json:"priority"`
	Status      string   `json:"status"`
	Position    Position `json:"position"`
	CreatedAt   string   `json:"createdAt"`
	UpdatedAt   string   `json:"updatedAt"`
}

func validPriority(v string) bool { return v == "low" || v == "medium" || v == "high" }
func validStatus(v string) bool   { return v == "open" || v == "in_progress" || v == "resolved" }
func newID() string {
	var raw [32]byte
	if _, err := rand.Read(raw[:]); err != nil {
		panic(err)
	}
	return hex.EncodeToString(raw[:])
}

func respond(w http.ResponseWriter, status int, data any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(data)
}
func fail(w http.ResponseWriter, status int, code, message string) {
	respond(w, status, map[string]any{"error": map[string]string{"code": code, "message": message}})
}
func decode(w http.ResponseWriter, r *http.Request, out any) bool {
	r.Body = http.MaxBytesReader(w, r.Body, 64*1024)
	d := json.NewDecoder(r.Body)
	d.DisallowUnknownFields()
	if err := d.Decode(out); err != nil {
		fail(w, 400, "INVALID_ARGUMENT", "请求必须是有效 JSON，字段类型须正确，且不得包含未知字段（最大 64 KiB）")
		return false
	}
	if err := d.Decode(new(any)); err != io.EOF {
		fail(w, 400, "INVALID_ARGUMENT", "请求只能包含一个 JSON 对象")
		return false
	}
	return true
}

func handler(s *store, origins string) http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /health", func(w http.ResponseWriter, r *http.Request) {
		if err := s.db.PingContext(r.Context()); err != nil {
			fail(w, 503, "UNAVAILABLE", "数据库不可用")
			return
		}
		respond(w, 200, map[string]string{"status": "ok"})
	})
	authRoutes(mux, s)
	mux.HandleFunc("GET /issues", func(w http.ResponseWriter, r *http.Request) {
		issues, err := s.listIssues()
		if err != nil {
			fail(w, 500, "PERSISTENCE_ERROR", "读取问题失败")
			return
		}
		respond(w, 200, issues)
	})
	mux.HandleFunc("POST /issues", func(w http.ResponseWriter, r *http.Request) {
		var input struct {
			Title       string `json:"title"`
			Description string `json:"description"`
			Priority    string `json:"priority"`
			Position    *struct {
				X *float64 `json:"x"`
				Y *float64 `json:"y"`
				Z *float64 `json:"z"`
			} `json:"position"`
		}
		if !decode(w, r, &input) {
			return
		}
		input.Title = strings.TrimSpace(input.Title)
		message := ""
		switch {
		case input.Title == "":
			message = "title 不能为空"
		case len(input.Title) > 600:
			message = "title 不能超过 600 字节"
		case len(input.Description) > 12000:
			message = "description 不能超过 12000 字节"
		case !validPriority(input.Priority):
			message = "priority 必须是 low、medium 或 high"
		case input.Position == nil:
			message = "position 必须包含数值 x、y、z"
		case input.Position.X == nil || input.Position.Y == nil || input.Position.Z == nil:
			message = "position 必须包含数值 x、y、z"
		}
		if message != "" {
			fail(w, 400, "INVALID_ARGUMENT", message)
			return
		}
		now := time.Now().UTC().Format(time.RFC3339Nano)
		issue := Issue{ID: newID(), Title: input.Title, Description: input.Description, Priority: input.Priority, Status: "open", Position: Position{*input.Position.X, *input.Position.Y, *input.Position.Z}, CreatedAt: now, UpdatedAt: now}
		if err := s.createIssue(issue); err != nil {
			log.Printf("create issue: %v", err)
			fail(w, 500, "PERSISTENCE_ERROR", "保存失败，请检查服务端日志")
			return
		}
		respond(w, 201, issue)
	})
	mux.HandleFunc("PATCH /issues/{id}/status", func(w http.ResponseWriter, r *http.Request) {
		var input struct {
			Status string `json:"status"`
		}
		if !decode(w, r, &input) {
			return
		}
		if !validStatus(input.Status) {
			fail(w, 400, "INVALID_ARGUMENT", "status 必须是 open、in_progress 或 resolved")
			return
		}
		issue, err := s.updateStatus(r.PathValue("id"), input.Status)
		if errors.Is(err, sql.ErrNoRows) {
			fail(w, 404, "NOT_FOUND", "问题不存在")
			return
		}
		if err != nil {
			log.Printf("update issue: %v", err)
			fail(w, 500, "PERSISTENCE_ERROR", "保存失败，请检查服务端日志")
			return
		}
		respond(w, 200, issue)
	})
	mux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) { fail(w, 404, "NOT_FOUND", "接口不存在") })
	allowed := map[string]bool{}
	for _, origin := range strings.Split(origins, ",") {
		allowed[strings.TrimSpace(origin)] = true
	}
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Add("Vary", "Origin")
		w.Header().Set("Cache-Control", "no-store")
		origin := r.Header.Get("Origin")
		if origin != "" && allowed[origin] {
			w.Header().Set("Access-Control-Allow-Origin", origin)
			w.Header().Set("Access-Control-Allow-Credentials", "true")
			w.Header().Set("Access-Control-Allow-Methods", "GET, POST, PATCH, OPTIONS")
			w.Header().Set("Access-Control-Allow-Headers", "Content-Type")
		}
		// Cookie-authenticated operations reject untrusted browser origins.
		protected := strings.HasPrefix(r.URL.Path, "/auth/") || r.URL.Path == "/users"
		if protected && r.Method != "GET" && r.Method != "HEAD" && r.Method != "OPTIONS" && origin != "" {
			sameHost := origin == "http://"+r.Host || origin == "https://"+r.Host
			if !sameHost && !allowed[origin] {
				fail(w, 403, "ORIGIN_DENIED", "来源未获允许")
				return
			}
		}
		if r.Method == http.MethodOptions {
			if origin != "" && !allowed[origin] {
				fail(w, 403, "ORIGIN_DENIED", "来源未获允许")
				return
			}
			w.WriteHeader(http.StatusNoContent)
			return
		}
		mux.ServeHTTP(w, r)
	})
}
func env(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

// Serve the built admin and API from one HTTP port when deployed without a proxy.
func siteHandler(api http.Handler, frontendDir string) http.Handler {
	mux := http.NewServeMux()
	mux.Handle("/api/", http.StripPrefix("/api", api))
	if frontendDir == "" {
		mux.Handle("/", api)
	} else {
		mux.Handle("/", http.FileServer(http.Dir(frontendDir)))
	}
	return mux
}

func main() {
	path, err := filepath.Abs(env("DB_FILE", "data/inspection.db"))
	if err != nil {
		log.Fatal(err)
	}
	s, err := openStore(path)
	if err != nil {
		log.Fatal(err)
	}
	defer s.db.Close()
	if err = s.importJSON(os.Getenv("LEGACY_JSON_FILE")); err != nil {
		log.Fatal(err)
	}
	if err = s.bootstrapAdmin(os.Getenv("ADMIN_PASSWORD")); err != nil {
		log.Fatal(err)
	}
	addr := env("LISTEN_ADDR", "0.0.0.0:8080")
	frontendDir := os.Getenv("FRONTEND_DIR")
	if frontendDir != "" {
		frontendDir, err = filepath.Abs(frontendDir)
		if err != nil {
			log.Fatal(err)
		}
		if info, err := os.Stat(filepath.Join(frontendDir, "index.html")); err != nil || info.IsDir() {
			log.Fatal("FRONTEND_DIR 必须包含构建后的 index.html")
		}
		log.Printf("frontend=%s; API prefix=/api", frontendDir)
	}
	log.Printf("listening %s; data=%s", addr, path)
	server := &http.Server{Addr: addr, Handler: siteHandler(handler(s, env("CORS_ORIGINS", "http://localhost:5173,http://127.0.0.1:5173")), frontendDir), ReadHeaderTimeout: 5 * time.Second, ReadTimeout: 15 * time.Second, WriteTimeout: 15 * time.Second, IdleTimeout: 60 * time.Second}
	log.Fatal(server.ListenAndServe())
}
