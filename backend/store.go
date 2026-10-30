package main

import (
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"time"

	_ "modernc.org/sqlite"
)

type store struct{ db *sql.DB }

func openStore(path string) (*store, error) {
	if err := os.MkdirAll(filepath.Dir(path), 0750); err != nil {
		return nil, err
	}
	db, err := sql.Open("sqlite", path)
	if err != nil {
		return nil, err
	}
	db.SetMaxOpenConns(1)
	_, err = db.Exec(`PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;
 CREATE TABLE IF NOT EXISTS issues (
 id TEXT PRIMARY KEY, title TEXT NOT NULL CHECK(length(trim(title))>0), description TEXT NOT NULL,
 priority TEXT NOT NULL CHECK(priority IN ('low','medium','high')),
 status TEXT NOT NULL CHECK(status IN ('open','in_progress','resolved')),
 x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
 CREATE TABLE IF NOT EXISTS users (
 id TEXT PRIMARY KEY, username TEXT NOT NULL COLLATE NOCASE UNIQUE,
 password_hash BLOB NOT NULL, role TEXT NOT NULL CHECK(role IN ('super_admin','admin','user')), created_at TEXT NOT NULL);
 CREATE UNIQUE INDEX IF NOT EXISTS one_super_admin ON users(role) WHERE role='super_admin';
 CREATE TABLE IF NOT EXISTS sessions (
 token_hash TEXT PRIMARY KEY, user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE, expires_at INTEGER NOT NULL);
 CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);`)
	if err != nil {
		db.Close()
		return nil, err
	}
	return &store{db}, nil
}

const issueColumns = `id,title,description,priority,status,x,y,z,created_at,updated_at`

func scanIssue(row interface{ Scan(...any) error }) (Issue, error) {
	var i Issue
	err := row.Scan(&i.ID, &i.Title, &i.Description, &i.Priority, &i.Status, &i.Position.X, &i.Position.Y, &i.Position.Z, &i.CreatedAt, &i.UpdatedAt)
	return i, err
}
func (s *store) listIssues() ([]Issue, error) {
	rows, err := s.db.Query(`SELECT ` + issueColumns + ` FROM issues ORDER BY created_at DESC,id DESC`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	result := []Issue{}
	for rows.Next() {
		i, err := scanIssue(rows)
		if err != nil {
			return nil, err
		}
		result = append(result, i)
	}
	return result, rows.Err()
}
func insertIssue(tx interface {
	Exec(string, ...any) (sql.Result, error)
}, i Issue) error {
	_, err := tx.Exec(`INSERT INTO issues (`+issueColumns+`) VALUES (?,?,?,?,?,?,?,?,?,?)`, i.ID, i.Title, i.Description, i.Priority, i.Status, i.Position.X, i.Position.Y, i.Position.Z, i.CreatedAt, i.UpdatedAt)
	return err
}
func (s *store) createIssue(i Issue) error { return insertIssue(s.db, i) }
func (s *store) updateStatus(id, status string) (Issue, error) {
	tx, err := s.db.Begin()
	if err != nil {
		return Issue{}, err
	}
	defer tx.Rollback()
	result, err := tx.Exec(`UPDATE issues SET status=?,updated_at=? WHERE id=?`, status, time.Now().UTC().Format(time.RFC3339Nano), id)
	if err != nil {
		return Issue{}, err
	}
	n, err := result.RowsAffected()
	if err != nil {
		return Issue{}, err
	}
	if n == 0 {
		return Issue{}, sql.ErrNoRows
	}
	issue, err := scanIssue(tx.QueryRow(`SELECT `+issueColumns+` FROM issues WHERE id=?`, id))
	if err != nil {
		return Issue{}, err
	}
	return issue, tx.Commit()
}

// Import a legacy JSON snapshot once, in one transaction. Never modifies the source file.
func (s *store) importJSON(path string) error {
	if path == "" {
		return nil
	}
	var imported string
	err := s.db.QueryRow(`SELECT value FROM metadata WHERE key='legacy_json_imported'`).Scan(&imported)
	if err == nil {
		return nil
	}
	if !errors.Is(err, sql.ErrNoRows) {
		return err
	}
	b, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return nil
	}
	if err != nil {
		return err
	}
	var issues []Issue
	if err = json.Unmarshal(b, &issues); err != nil {
		return fmt.Errorf("旧 JSON 数据损坏: %w", err)
	}
	if issues == nil {
		return errors.New("旧 JSON 必须是数组")
	}
	for _, i := range issues {
		if i.ID == "" || i.Title == "" || !validPriority(i.Priority) || !validStatus(i.Status) {
			return errors.New("旧 JSON 含无效问题")
		}
		if _, err = time.Parse(time.RFC3339Nano, i.CreatedAt); err != nil {
			return err
		}
		if _, err = time.Parse(time.RFC3339Nano, i.UpdatedAt); err != nil {
			return err
		}
	}
	tx, err := s.db.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()
	var count int
	if err = tx.QueryRow(`SELECT count(*) FROM issues`).Scan(&count); err != nil {
		return err
	}
	if count != 0 {
		return errors.New("数据库已有问题，拒绝自动导入旧 JSON；请明确选择数据源")
	}
	for _, i := range issues {
		if err = insertIssue(tx, i); err != nil {
			return err
		}
	}
	if _, err = tx.Exec(`INSERT INTO metadata(key,value) VALUES('legacy_json_imported',?)`, path); err != nil {
		return err
	}
	return tx.Commit()
}
