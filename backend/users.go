package main

import (
	"crypto/sha256"
	"database/sql"
	"encoding/hex"
	"errors"
	"net/http"
	"os"
	"regexp"
	"strings"
	"time"

	"golang.org/x/crypto/bcrypt"
)

type User struct {
	ID        string `json:"id"`
	Username  string `json:"username"`
	Role      string `json:"role"`
	CreatedAt string `json:"createdAt"`
}

var usernamePattern = regexp.MustCompile(`^[A-Za-z][A-Za-z0-9_.-]{2,31}$`)

func validPassword(password string) bool { return len(password) >= 12 && len(password) <= 72 }
func (s *store) bootstrapAdmin(password string) error {
	var role string
	err := s.db.QueryRow(`SELECT role FROM users WHERE username='Admin'`).Scan(&role)
	if err == nil {
		if role != "super_admin" {
			return errors.New("Admin 已存在但不是 super_admin")
		}
		return nil
	}
	if !errors.Is(err, sql.ErrNoRows) {
		return err
	}
	if !validPassword(password) {
		return errors.New("首次启动须设置 ADMIN_PASSWORD（12～72 字节），用于初始化 Admin")
	}
	_, err = s.addUser("Admin", password, "super_admin")
	return err
}
func (s *store) addUser(username, password, role string) (User, error) {
	hash, err := bcrypt.GenerateFromPassword([]byte(password), bcrypt.DefaultCost)
	if err != nil {
		return User{}, err
	}
	user := User{newID(), username, role, time.Now().UTC().Format(time.RFC3339Nano)}
	_, err = s.db.Exec(`INSERT INTO users(id,username,password_hash,role,created_at) VALUES(?,?,?,?,?)`, user.ID, user.Username, hash, user.Role, user.CreatedAt)
	return user, err
}
func tokenHash(token string) string {
	hash := sha256.Sum256([]byte(token))
	return hex.EncodeToString(hash[:])
}

const sessionCookie = "inspection_session"

func (s *store) currentUser(r *http.Request) (User, error) {
	cookie, err := r.Cookie(sessionCookie)
	if err != nil {
		return User{}, sql.ErrNoRows
	}
	var user User
	err = s.db.QueryRow(`SELECT u.id,u.username,u.role,u.created_at FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=? AND s.expires_at>?`, tokenHash(cookie.Value), time.Now().Unix()).Scan(&user.ID, &user.Username, &user.Role, &user.CreatedAt)
	return user, err
}
func requireUser(s *store, w http.ResponseWriter, r *http.Request) (User, bool) {
	user, err := s.currentUser(r)
	if errors.Is(err, sql.ErrNoRows) {
		fail(w, 401, "UNAUTHENTICATED", "请先登录")
		return User{}, false
	}
	if err != nil {
		fail(w, 500, "PERSISTENCE_ERROR", "读取登录状态失败")
		return User{}, false
	}
	return user, true
}
func authRoutes(mux *http.ServeMux, s *store) {
	setCookie := func(w http.ResponseWriter, value string, maxAge int) {
		http.SetCookie(w, &http.Cookie{Name: sessionCookie, Value: value, Path: "/", HttpOnly: true, Secure: os.Getenv("COOKIE_SECURE") == "true", SameSite: http.SameSiteLaxMode, MaxAge: maxAge})
	}
	mux.HandleFunc("POST /auth/login", func(w http.ResponseWriter, r *http.Request) {
		var in struct {
			Username string `json:"username"`
			Password string `json:"password"`
		}
		if !decode(w, r, &in) {
			return
		}
		var user User
		var hash []byte
		err := s.db.QueryRow(`SELECT id,username,password_hash,role,created_at FROM users WHERE username=?`, strings.TrimSpace(in.Username)).Scan(&user.ID, &user.Username, &hash, &user.Role, &user.CreatedAt)
		if err != nil && !errors.Is(err, sql.ErrNoRows) {
			fail(w, 500, "PERSISTENCE_ERROR", "登录暂不可用")
			return
		}
		if err != nil || bcrypt.CompareHashAndPassword(hash, []byte(in.Password)) != nil {
			fail(w, 401, "INVALID_CREDENTIALS", "用户名或密码错误")
			return
		}
		token := newID()
		if _, err = s.db.Exec(`DELETE FROM sessions WHERE expires_at<=?`, time.Now().Unix()); err != nil {
			fail(w, 500, "PERSISTENCE_ERROR", "清理会话失败")
			return
		}
		// Rotate the session when logging in from an already authenticated browser.
		if old, err := r.Cookie(sessionCookie); err == nil {
			s.db.Exec(`DELETE FROM sessions WHERE token_hash=?`, tokenHash(old.Value))
		}
		if _, err = s.db.Exec(`INSERT INTO sessions(token_hash,user_id,expires_at) VALUES(?,?,?)`, tokenHash(token), user.ID, time.Now().Add(24*time.Hour).Unix()); err != nil {
			fail(w, 500, "PERSISTENCE_ERROR", "保存登录状态失败")
			return
		}
		setCookie(w, token, 86400)
		respond(w, 200, user)
	})
	mux.HandleFunc("GET /auth/me", func(w http.ResponseWriter, r *http.Request) {
		user, ok := requireUser(s, w, r)
		if ok {
			respond(w, 200, user)
		}
	})
	mux.HandleFunc("POST /auth/logout", func(w http.ResponseWriter, r *http.Request) {
		if cookie, err := r.Cookie(sessionCookie); err == nil {
			if _, err = s.db.Exec(`DELETE FROM sessions WHERE token_hash=?`, tokenHash(cookie.Value)); err != nil {
				fail(w, 500, "PERSISTENCE_ERROR", "退出失败，请重试")
				return
			}
		}
		setCookie(w, "", -1)
		respond(w, 200, map[string]bool{"ok": true})
	})
	mux.HandleFunc("PATCH /auth/password", func(w http.ResponseWriter, r *http.Request) {
		user, ok := requireUser(s, w, r)
		if !ok {
			return
		}
		var in struct {
			CurrentPassword string `json:"currentPassword"`
			NewPassword     string `json:"newPassword"`
		}
		if !decode(w, r, &in) {
			return
		}
		if !validPassword(in.NewPassword) {
			fail(w, 400, "INVALID_ARGUMENT", "新密码须为 12～72 字节")
			return
		}
		var hash []byte
		if err := s.db.QueryRow(`SELECT password_hash FROM users WHERE id=?`, user.ID).Scan(&hash); err != nil {
			fail(w, 500, "PERSISTENCE_ERROR", "读取账号失败")
			return
		}
		if bcrypt.CompareHashAndPassword(hash, []byte(in.CurrentPassword)) != nil {
			fail(w, 400, "INVALID_ARGUMENT", "当前密码错误")
			return
		}
		hash, err := bcrypt.GenerateFromPassword([]byte(in.NewPassword), bcrypt.DefaultCost)
		if err != nil {
			fail(w, 500, "INTERNAL", "更新密码失败")
			return
		}
		tx, err := s.db.Begin()
		if err != nil {
			fail(w, 500, "PERSISTENCE_ERROR", "更新密码失败")
			return
		}
		defer tx.Rollback()
		if _, err = tx.Exec(`UPDATE users SET password_hash=? WHERE id=?`, hash, user.ID); err == nil {
			_, err = tx.Exec(`DELETE FROM sessions WHERE user_id=?`, user.ID)
		}
		if err == nil {
			err = tx.Commit()
		}
		if err != nil {
			fail(w, 500, "PERSISTENCE_ERROR", "更新密码失败")
			return
		}
		setCookie(w, "", -1)
		respond(w, 200, map[string]bool{"ok": true})
	})
	mux.HandleFunc("GET /users", func(w http.ResponseWriter, r *http.Request) {
		user, ok := requireUser(s, w, r)
		if !ok {
			return
		}
		if user.Role != "super_admin" {
			fail(w, 403, "FORBIDDEN", "仅 super admin 可管理用户")
			return
		}
		rows, err := s.db.Query(`SELECT id,username,role,created_at FROM users ORDER BY created_at,id`)
		if err != nil {
			fail(w, 500, "PERSISTENCE_ERROR", "读取用户失败")
			return
		}
		defer rows.Close()
		users := []User{}
		for rows.Next() {
			var u User
			if err = rows.Scan(&u.ID, &u.Username, &u.Role, &u.CreatedAt); err != nil {
				fail(w, 500, "PERSISTENCE_ERROR", "读取用户失败")
				return
			}
			users = append(users, u)
		}
		if rows.Err() != nil {
			fail(w, 500, "PERSISTENCE_ERROR", "读取用户失败")
			return
		}
		respond(w, 200, users)
	})
	mux.HandleFunc("POST /users", func(w http.ResponseWriter, r *http.Request) {
		actor, ok := requireUser(s, w, r)
		if !ok {
			return
		}
		if actor.Role != "super_admin" {
			fail(w, 403, "FORBIDDEN", "仅 super admin 可添加用户")
			return
		}
		var in struct {
			Username string `json:"username"`
			Password string `json:"password"`
			Role     string `json:"role"`
		}
		if !decode(w, r, &in) {
			return
		}
		in.Username = strings.TrimSpace(in.Username)
		if !usernamePattern.MatchString(in.Username) {
			fail(w, 400, "INVALID_ARGUMENT", "用户名须以英文字母开头，3～32 位，可包含数字、下划线、点和横线")
			return
		}
		if !validPassword(in.Password) {
			fail(w, 400, "INVALID_ARGUMENT", "密码须为 12～72 字节")
			return
		}
		if in.Role != "admin" && in.Role != "user" {
			fail(w, 400, "INVALID_ARGUMENT", "仅可创建 admin 或 user")
			return
		}
		user, err := s.addUser(in.Username, in.Password, in.Role)
		if err != nil {
			var exists int
			if e := s.db.QueryRow(`SELECT count(*) FROM users WHERE username=?`, in.Username).Scan(&exists); e == nil && exists > 0 {
				fail(w, 409, "CONFLICT", "用户名已存在")
				return
			}
			fail(w, 500, "PERSISTENCE_ERROR", "创建用户失败")
			return
		}
		respond(w, 201, user)
	})
}
