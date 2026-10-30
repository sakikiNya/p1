package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

const adminPassword = "Testing-Admin-2026!"

func authRequest(h http.Handler, method, path, body string, cookie *http.Cookie) *httptest.ResponseRecorder {
	r := httptest.NewRequest(method, path, strings.NewReader(body))
	if cookie != nil {
		r.AddCookie(cookie)
	}
	w := httptest.NewRecorder()
	h.ServeHTTP(w, r)
	return w
}
func login(t *testing.T, h http.Handler, username, password string) *http.Cookie {
	t.Helper()
	body, _ := json.Marshal(map[string]string{"username": username, "password": password})
	w := authRequest(h, "POST", "/auth/login", string(body), nil)
	if w.Code != 200 {
		t.Fatal(w.Code, w.Body.String())
	}
	cookies := w.Result().Cookies()
	if len(cookies) != 1 || !cookies[0].HttpOnly || cookies[0].SameSite != http.SameSiteLaxMode {
		t.Fatal(cookies)
	}
	return cookies[0]
}
func TestAccountsRolesAndSessions(t *testing.T) {
	path := filepath.Join(t.TempDir(), "inspection.db")
	s, err := openStore(path)
	if err != nil {
		t.Fatal(err)
	}
	defer s.db.Close()
	if err = s.bootstrapAdmin(""); err == nil {
		t.Fatal("accepted missing initial password")
	}
	if err = s.bootstrapAdmin(adminPassword); err != nil {
		t.Fatal(err)
	}
	if err = s.bootstrapAdmin("Ignored-New-Password"); err != nil {
		t.Fatal(err)
	}
	h := handler(s, "")
	cookie := login(t, h, "Admin", adminPassword)
	w := authRequest(h, "GET", "/auth/me", "", cookie)
	if w.Code != 200 || !strings.Contains(w.Body.String(), `"role":"super_admin"`) {
		t.Fatal(w.Body.String())
	}
	if w = authRequest(h, "GET", "/users", "", nil); w.Code != 401 {
		t.Fatal(w.Code)
	}
	for _, role := range []string{"user", "admin"} {
		body := `{"username":"Test_` + role + `","password":"Testing-User-2026!","role":"` + role + `"}`
		w = authRequest(h, "POST", "/users", body, cookie)
		if w.Code != 201 {
			t.Fatal(w.Code, w.Body.String())
		}
		if strings.Contains(w.Body.String(), "password") || strings.Contains(w.Body.String(), "hash") {
			t.Fatal("password exposed")
		}
		w = authRequest(h, "POST", "/users", body, cookie)
		if w.Code != 409 {
			t.Fatal(w.Code)
		}
		lowCookie := login(t, h, "Test_"+role, "Testing-User-2026!")
		if w = authRequest(h, "GET", "/users", "", lowCookie); w.Code != 403 {
			t.Fatal(w.Code)
		}
		if w = authRequest(h, "POST", "/users", body, lowCookie); w.Code != 403 {
			t.Fatal(w.Code)
		}
	}
	if w = authRequest(h, "POST", "/users", `{"username":"OtherRoot","password":"Testing-User-2026!","role":"super_admin"}`, cookie); w.Code != 400 {
		t.Fatal(w.Code)
	}
	if w = authRequest(h, "POST", "/users", `{"username":"Abc","password":"short","role":"user"}`, cookie); w.Code != 400 {
		t.Fatal(w.Code)
	}
	if w = authRequest(h, "POST", "/users", `{"username":"AdMin","password":"Testing-User-2026!","role":"user"}`, cookie); w.Code != 409 {
		t.Fatal(w.Code)
	}
	if w = authRequest(h, "POST", "/auth/login", `{"username":"Admin","password":"wrong"}`, nil); w.Code != 401 {
		t.Fatal(w.Code)
	}
	if w = authRequest(h, "GET", "/users", "", cookie); w.Code != 200 {
		t.Fatal(w.Code)
	}
	// Sessions and hashes survive a DB reconnect without reseeding or changing Admin's password.
	other, err := openStore(path)
	if err != nil {
		t.Fatal(err)
	}
	defer other.db.Close()
	if w = authRequest(handler(other, ""), "GET", "/auth/me", "", cookie); w.Code != 200 {
		t.Fatal(w.Code)
	}
	if w = authRequest(h, "POST", "/auth/logout", "", cookie); w.Code != 200 {
		t.Fatal(w.Code)
	}
	if w = authRequest(h, "GET", "/auth/me", "", cookie); w.Code != 401 {
		t.Fatal(w.Code)
	}
	expired := login(t, h, "Admin", adminPassword)
	s.db.Exec(`UPDATE sessions SET expires_at=?`, time.Now().Unix()-1)
	if w = authRequest(h, "GET", "/auth/me", "", expired); w.Code != 401 {
		t.Fatal(w.Code)
	}
	if w = authRequest(h, "POST", "/issues", payload, nil); w.Code != 201 {
		t.Fatal("Unity API requires auth", w.Code)
	}
}
func TestPasswordChangeRevokesSessions(t *testing.T) {
	s, _ := openStore(filepath.Join(t.TempDir(), "inspection.db"))
	defer s.db.Close()
	s.bootstrapAdmin(adminPassword)
	h := handler(s, "")
	first := login(t, h, "Admin", adminPassword)
	second := login(t, h, "Admin", adminPassword)
	w := authRequest(h, "PATCH", "/auth/password", `{"currentPassword":"wrong","newPassword":"Replacement-Password"}`, first)
	if w.Code != 400 {
		t.Fatal(w.Code)
	}
	w = authRequest(h, "PATCH", "/auth/password", `{"currentPassword":"`+adminPassword+`","newPassword":"Replacement-Password"}`, first)
	if w.Code != 200 {
		t.Fatal(w.Body.String())
	}
	for _, cookie := range []*http.Cookie{first, second} {
		if w = authRequest(h, "GET", "/auth/me", "", cookie); w.Code != 401 {
			t.Fatal(w.Code)
		}
	}
	login(t, h, "Admin", "Replacement-Password")
	if w = authRequest(h, "POST", "/auth/login", `{"username":"Admin","password":"`+adminPassword+`"}`, nil); w.Code != 401 {
		t.Fatal(w.Code)
	}
}

func TestAccountOriginProtection(t *testing.T) {
	s, _ := openStore(filepath.Join(t.TempDir(), "inspection.db"))
	defer s.db.Close()
	s.bootstrapAdmin(adminPassword)
	h := handler(s, "")
	cookie := login(t, h, "Admin", adminPassword)
	r := httptest.NewRequest("POST", "/users", strings.NewReader(`{"username":"Injected","password":"Testing-User-2026!","role":"admin"}`))
	r.AddCookie(cookie)
	r.Header.Set("Origin", "http://untrusted.example")
	w := httptest.NewRecorder()
	h.ServeHTTP(w, r)
	if w.Code != 403 {
		t.Fatal(w.Code)
	}
}
