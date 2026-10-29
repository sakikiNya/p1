package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
)

func request(h http.Handler, method, path, body string) *httptest.ResponseRecorder {
	r := httptest.NewRequest(method, path, strings.NewReader(body))
	r.Header.Set("Origin", "http://localhost:5173")
	w := httptest.NewRecorder()
	h.ServeHTTP(w, r)
	return w
}

const payload = `{"title":" 墙面破损 ","description":"裂缝","priority":"high","position":{"x":0,"y":0.03,"z":1.26}}`

func TestLifecycleAndReload(t *testing.T) {
	path := filepath.Join(t.TempDir(), "data", "issues.json")
	s, err := openStore(path)
	if err != nil {
		t.Fatal(err)
	}
	h := handler(s, "http://localhost:5173")
	if w := request(h, "GET", "/issues", ""); strings.TrimSpace(w.Body.String()) != "[]" {
		t.Fatal(w.Body.String())
	}
	w := request(h, "POST", "/issues", payload)
	if w.Code != 201 {
		t.Fatal(w.Code, w.Body.String())
	}
	var issue Issue
	if err := json.Unmarshal(w.Body.Bytes(), &issue); err != nil {
		t.Fatal(err)
	}
	if issue.ID == "" || issue.Title != "墙面破损" || issue.Status != "open" || issue.CreatedAt == "" || issue.UpdatedAt != issue.CreatedAt || issue.Position.X != 0 {
		t.Fatalf("%+v", issue)
	}
	for _, status := range []string{"in_progress", "resolved", "open", "resolved"} {
		w = request(h, "PATCH", "/issues/"+issue.ID+"/status", `{"status":"`+status+`"}`)
		if w.Code != 200 {
			t.Fatal(w.Body.String())
		}
		var updated Issue
		json.Unmarshal(w.Body.Bytes(), &updated)
		if updated.Status != status || updated.CreatedAt != issue.CreatedAt || updated.Title != issue.Title || updated.Position != issue.Position {
			t.Fatalf("%+v", updated)
		}
	}
	reloaded, err := openStore(path)
	if err != nil {
		t.Fatal(err)
	}
	if len(reloaded.issues) != 1 || reloaded.issues[0].Status != "resolved" || reloaded.issues[0].ID != issue.ID {
		t.Fatal(reloaded.issues)
	}
}
func TestInvalidInputsAndCORS(t *testing.T) {
	s, _ := openStore(filepath.Join(t.TempDir(), "issues.json"))
	h := handler(s, "http://localhost:5173")
	for _, body := range []string{
		`{}`, `null`, `{`, payload + `{}`, strings.Replace(payload, "墙面破损", "   ", 1), strings.Replace(payload, `"high"`, `"urgent"`, 1),
		strings.Replace(payload, `"x":0,`, "", 1), strings.Replace(payload, `"x":0`, `"x":null`, 1), strings.Replace(payload, `"x":0`, `"x":"0"`, 1), strings.Replace(payload, `"x":0`, `"x":1e999`, 1),
		strings.Replace(payload, `"title"`, `"id":"injected","title"`, 1),
	} {
		w := request(h, "POST", "/issues", body)
		if w.Code != 400 {
			t.Errorf("%s: %d %s", body, w.Code, w.Body.String())
		}
		if w.Header().Get("Access-Control-Allow-Origin") != "http://localhost:5173" {
			t.Fatal("error missing CORS")
		}
	}
	if w := request(h, "PATCH", "/issues/missing/status", `{"status":"done"}`); w.Code != 400 {
		t.Fatal(w.Code)
	}
	if w := request(h, "PATCH", "/issues/missing/status", `{"status":"open"}`); w.Code != 404 {
		t.Fatal(w.Code)
	}
	if w := request(h, "OPTIONS", "/issues/a/status", ""); w.Code != 204 || !strings.Contains(w.Header().Get("Access-Control-Allow-Methods"), "PATCH") {
		t.Fatal(w)
	}
	r := httptest.NewRequest("OPTIONS", "/issues", nil)
	r.Header.Set("Origin", "https://unknown.example")
	w := httptest.NewRecorder()
	h.ServeHTTP(w, r)
	if w.Code != 403 || w.Header().Get("Access-Control-Allow-Origin") != "" {
		t.Fatal(w)
	}
	if w := request(h, "GET", "/health", ""); w.Code != 200 {
		t.Fatal(w.Code)
	}
}
func TestSaveFailureKeepsMemoryAndFile(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "issues.json")
	s, _ := openStore(path)
	h := handler(s, "")
	if w := request(h, "POST", "/issues", payload); w.Code != 201 {
		t.Fatal(w.Body.String())
	}
	original, _ := os.ReadFile(path)
	id := s.issues[0].ID
	// A regular file used as the parent directory produces a deterministic write failure, even as root.
	s.path = filepath.Join(path, "blocked.json")
	for _, op := range []struct{ method, path, body string }{{"POST", "/issues", payload}, {"PATCH", "/issues/" + id + "/status", `{"status":"resolved"}`}} {
		if w := request(h, op.method, op.path, op.body); w.Code != 500 {
			t.Fatal(w.Code)
		}
	}
	after, _ := os.ReadFile(path)
	if string(original) != string(after) || len(s.issues) != 1 || s.issues[0].Status != "open" {
		t.Fatal("failed write altered data")
	}
}
func TestCorruptFileRejected(t *testing.T) {
	path := filepath.Join(t.TempDir(), "issues.json")
	for _, data := range []string{"{", "null", `[{"id":"bad"}]`} {
		os.WriteFile(path, []byte(data), 0600)
		if _, err := openStore(path); err == nil {
			t.Fatal("accepted", data)
		}
		b, _ := os.ReadFile(path)
		if string(b) != data {
			t.Fatal("overwritten")
		}
	}
}
func TestConcurrentCreates(t *testing.T) {
	path := filepath.Join(t.TempDir(), "issues.json")
	s, _ := openStore(path)
	h := handler(s, "")
	var wg sync.WaitGroup
	for i := 0; i < 20; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if w := request(h, "POST", "/issues", payload); w.Code != 201 {
				t.Errorf("%d", w.Code)
			}
		}()
	}
	wg.Wait()
	loaded, err := openStore(path)
	if err != nil || len(loaded.issues) != 20 {
		t.Fatal(err, len(s.issues))
	}
}

func TestSiteHandler(t *testing.T) {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "index.html"), []byte("<html>inspection admin</html>"), 0600); err != nil {
		t.Fatal(err)
	}
	s, _ := openStore(filepath.Join(t.TempDir(), "issues.json"))
	h := siteHandler(handler(s, "http://localhost:5173"), dir)
	if w := request(h, "GET", "/", ""); w.Code != 200 || !strings.Contains(w.Body.String(), "inspection admin") {
		t.Fatal(w.Code, w.Body.String())
	}
	if w := request(h, "GET", "/api/health", ""); w.Code != 200 || !strings.Contains(w.Body.String(), `"status":"ok"`) {
		t.Fatal(w.Code, w.Body.String())
	}
	if w := request(h, "POST", "/api/issues", payload); w.Code != 201 {
		t.Fatal(w.Code, w.Body.String())
	}
	if w := request(h, "GET", "/api/issues", ""); w.Code != 200 || !strings.Contains(w.Body.String(), "墙面破损") {
		t.Fatal(w.Code, w.Body.String())
	}
	if w := request(h, "GET", "/api/missing", ""); w.Code != 404 {
		t.Fatal(w.Code)
	}
	if w := request(h, "GET", "/.env", ""); w.Code != 404 {
		t.Fatal("static files escaped dist", w.Code)
	}
	if w := request(siteHandler(handler(s, ""), ""), "GET", "/health", ""); w.Code != 200 {
		t.Fatal("legacy health route changed", w.Code)
	}
}
