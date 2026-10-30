#!/usr/bin/env python3
"""Manage this workspace's Go API and optional domain-free Caddy process."""
import argparse
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
RUNTIME = ROOT / '.cache/server'
PID_FILE = RUNTIME / 'server.pid'
BINARY = RUNTIME / 'inspection'
LOG = RUNTIME / 'server.log'
CADDY_PID = RUNTIME / 'caddy.pid'
CADDY_LOG = RUNTIME / 'caddy.log'


def config():
    env = os.environ.copy()
    for line in (ROOT / 'deploy/.env').read_text().splitlines():
        line = line.strip()
        if line and not line.startswith('#'):
            key, value = line.split('=', 1)
            env[key.strip()] = value.strip().strip('"').strip("'")
    return env


def running(pid_file=PID_FILE, binary=BINARY):
    try:
        pid = int(pid_file.read_text())
        executable = os.readlink(f'/proc/{pid}/exe').removesuffix(' (deleted)')
        if executable == str(binary):
            if pid_file == CADDY_PID:
                command = Path(f'/proc/{pid}/cmdline').read_bytes().split(b'\0')
                if str(ROOT / 'deploy/Caddyfile').encode() not in command:
                    return None
            return pid
    except (FileNotFoundError, ValueError, ProcessLookupError, PermissionError):
        pass
    return None


def stop_process(pid_file, binary):
    pid = running(pid_file, binary)
    if not pid:
        print('Server is not running.')
        return
    os.kill(pid, signal.SIGTERM)
    for _ in range(100):
        if not running(pid_file, binary):
            pid_file.unlink(missing_ok=True)
            print('Server stopped.')
            return
        time.sleep(.1)
    raise RuntimeError('Server did not stop; no other processes were signalled.')


def stop():
    caddy = shutil.which('caddy')
    if caddy:
        stop_process(CADDY_PID, Path(caddy).resolve())
    stop_process(PID_FILE, BINARY)


def start():
    if pid := running():
        print(f'Already running: PID {pid}')
        return
    env = config()
    source = ROOT / '.cache/release/inspection'
    if not source.exists():
        raise RuntimeError('Run bash scripts/build-release.sh first.')
    frontend = Path(env['FRONTEND_DIR'])
    if not frontend.is_absolute():
        frontend = ROOT / frontend
    env['FRONTEND_DIR'] = str(frontend)
    env['XDG_CONFIG_HOME'] = str(RUNTIME / 'config')
    env['XDG_DATA_HOME'] = str(RUNTIME / 'data')
    caddy = shutil.which('caddy')
    if env.get('USE_CADDY') == 'true':
        if not caddy:
            raise RuntimeError('Install Caddy or set USE_CADDY=false.')
        if env['API_UPSTREAM'] != env['LISTEN_ADDR']:
            raise RuntimeError('API_UPSTREAM must equal LISTEN_ADDR.')
        subprocess.run([caddy, 'validate', '--config', str(ROOT/'deploy/Caddyfile')], env=env, cwd=ROOT, check=True)
    if not (frontend / 'index.html').is_file():
        raise RuntimeError('FRONTEND_DIR must contain built index.html')
    RUNTIME.mkdir(parents=True, exist_ok=True)
    staging = RUNTIME / 'inspection.next'
    shutil.copy2(source, staging)
    staging.replace(BINARY)
    with LOG.open('ab', buffering=0) as log:
        api_env = env.copy()
        if env.get('USE_CADDY') == 'true':
            api_env.pop('FRONTEND_DIR', None)
        process = subprocess.Popen([str(BINARY)], cwd=ROOT, env=api_env,
                                   stdin=subprocess.DEVNULL, stdout=log,
                                   stderr=subprocess.STDOUT, start_new_session=True)
    PID_FILE.write_text(str(process.pid))
    port = env['LISTEN_ADDR'].rsplit(':', 1)[1]
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    for _ in range(100):
        if process.poll() is not None:
            PID_FILE.unlink(missing_ok=True)
            raise RuntimeError(f'Start failed, see {LOG}')
        try:
            with opener.open(f'http://127.0.0.1:{port}' + ('/health' if env.get('USE_CADDY') == 'true' else '/api/health'), timeout=1) as response:
                if json.load(response).get('status') == 'ok':
                    if env.get('USE_CADDY') == 'true':
                        try:
                            start_caddy(caddy, env)
                        except Exception:
                            process.terminate()
                            raise
                    print(f'API running: {env["LISTEN_ADDR"]}, PID {process.pid}')
                    print(f'Log: {LOG}\nDatabase: {env.get("DB_FILE", "backend/data/inspection.db")}')
                    return
        except (OSError, ValueError):
            time.sleep(.1)
    process.terminate()
    raise RuntimeError(f'Health check failed, see {LOG}')


def start_caddy(caddy, env):
    if running(CADDY_PID, Path(caddy).resolve()):
        raise RuntimeError('An existing project Caddy is still running; use restart.')
    with CADDY_LOG.open('ab', buffering=0) as log:
        child = subprocess.Popen([caddy, 'run', '--config', str(ROOT/'deploy/Caddyfile')], cwd=ROOT,
                                 env=env, stdin=subprocess.DEVNULL, stdout=log,
                                 stderr=subprocess.STDOUT, start_new_session=True)
    CADDY_PID.write_text(str(child.pid))
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    for _ in range(50):
        if child.poll() is not None:
            raise RuntimeError(f'Caddy failed, see {CADDY_LOG}')
        try:
            with opener.open(f'http://127.0.0.1:{env["PORT"]}/api/health', timeout=1) as response:
                if json.load(response).get('status') == 'ok':
                    print('Caddy HTTP frontend is ready.')
                    return
        except (OSError, ValueError):
            time.sleep(.1)
    child.terminate()
    raise RuntimeError('Caddy health check failed.')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=['start', 'stop', 'restart', 'status'])
    action = parser.parse_args().action
    if action in ('stop', 'restart'):
        stop()
    if action in ('start', 'restart'):
        start()
    if action == 'status':
        pid = running()
        print(f'API running: PID {pid}' if pid else 'API is not running.')
        if config().get('USE_CADDY') == 'true':
            caddy = shutil.which('caddy')
            proxy_pid = running(CADDY_PID, Path(caddy).resolve()) if caddy else None
            print(f'Caddy running: PID {proxy_pid}' if proxy_pid else 'Caddy is not running.')
            if not proxy_pid:
                raise SystemExit(1)
        if not pid:
            raise SystemExit(1)


if __name__ == '__main__':
    main()
