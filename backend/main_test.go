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
	path := filepath.Join(t.TempDir(), "data", "inspection.db")
	s, err := openStore(path)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { s.db.Close() })
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
	defer reloaded.db.Close()
	issues, err := reloaded.listIssues()
	if err != nil || len(issues) != 1 || issues[0].Status != "resolved" || issues[0].ID != issue.ID {
		t.Fatal(issues, err)
	}
}
func TestInvalidInputsAndCORS(t *testing.T) {
	s, _ := openStore(filepath.Join(t.TempDir(), "inspection.db"))
	t.Cleanup(func() { s.db.Close() })
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
func TestWriteFailureDoesNotReportSuccess(t *testing.T) {
	s, err := openStore(filepath.Join(t.TempDir(), "inspection.db"))
	if err != nil {
		t.Fatal(err)
	}
	defer s.db.Close()
	h := handler(s, "")
	w := request(h, "POST", "/issues", payload)
	if w.Code != 201 {
		t.Fatal(w.Body.String())
	}
	var issue Issue
	json.Unmarshal(w.Body.Bytes(), &issue)
	if _, err = s.db.Exec(`PRAGMA query_only=ON`); err != nil {
		t.Fatal(err)
	}
	if w = request(h, "POST", "/issues", payload); w.Code != 500 {
		t.Fatal(w.Code)
	}
	if w = request(h, "PATCH", "/issues/"+issue.ID+"/status", `{"status":"resolved"}`); w.Code != 500 {
		t.Fatal(w.Code)
	}
	issues, err := s.listIssues()
	if err != nil || len(issues) != 1 || issues[0].Status != "open" {
		t.Fatal(issues, err)
	}
}
func TestJSONImportAtomicAndIdempotent(t *testing.T) {
	s, _ := openStore(filepath.Join(t.TempDir(), "inspection.db"))
	defer s.db.Close()
	h := handler(s, "")
	w := request(h, "POST", "/issues", payload)
	var issue Issue
	json.Unmarshal(w.Body.Bytes(), &issue)
	legacy, _ := json.Marshal([]Issue{issue})
	path := filepath.Join(t.TempDir(), "issues.json")
	os.WriteFile(path, legacy, 0600)
	target, _ := openStore(filepath.Join(t.TempDir(), "inspection.db"))
	defer target.db.Close()
	if err := target.importJSON(path); err != nil {
		t.Fatal(err)
	}
	if _, err := target.updateStatus(issue.ID, "resolved"); err != nil {
		t.Fatal(err)
	}
	if err := target.importJSON(path); err != nil {
		t.Fatal(err)
	}
	issues, _ := target.listIssues()
	if len(issues) != 1 || issues[0].Status != "resolved" {
		t.Fatal(issues)
	}
	b, _ := os.ReadFile(path)
	if string(b) != string(legacy) {
		t.Fatal("modified legacy file")
	}
	duplicate := "[" + strings.TrimSpace(w.Body.String()) + "," + strings.TrimSpace(w.Body.String()) + "]"
	for _, bad := range []string{"{", "null", `[{"id":"invalid"}]`, duplicate} {
		temp, _ := openStore(filepath.Join(t.TempDir(), "inspection.db"))
		os.WriteFile(path, []byte(bad), 0600)
		if err := temp.importJSON(path); err == nil {
			t.Fatal("accepted invalid import")
		}
		got, _ := temp.listIssues()
		if len(got) != 0 {
			t.Fatal("partial import")
		}
		temp.db.Close()
	}
}
func TestConcurrentCreates(t *testing.T) {
	path := filepath.Join(t.TempDir(), "inspection.db")
	s, _ := openStore(path)
	t.Cleanup(func() { s.db.Close() })
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
	if err != nil {
		t.Fatal(err)
	}
	defer loaded.db.Close()
	issues, err := loaded.listIssues()
	if err != nil || len(issues) != 20 {
		t.Fatal(err, len(issues))
	}
}

func TestSiteHandler(t *testing.T) {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "index.html"), []byte("<html>inspection admin</html>"), 0600); err != nil {
		t.Fatal(err)
	}
	s, _ := openStore(filepath.Join(t.TempDir(), "inspection.db"))
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
