#!/usr/bin/env python3
"""Manage only this workspace's temporary HTTP server. No SSH or system service."""
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


def config():
    env = os.environ.copy()
    for line in (ROOT / 'deploy/.env').read_text().splitlines():
        line = line.strip()
        if line and not line.startswith('#'):
            key, value = line.split('=', 1)
            env[key.strip()] = value.strip().strip('"').strip("'")
    return env


def running():
    try:
        pid = int(PID_FILE.read_text())
        executable = os.readlink(f'/proc/{pid}/exe').removesuffix(' (deleted)')
        if executable == str(BINARY):
            return pid
    except (FileNotFoundError, ValueError, ProcessLookupError, PermissionError):
        pass
    return None


def stop():
    pid = running()
    if not pid:
        print('Server is not running.')
        return
    os.kill(pid, signal.SIGTERM)
    for _ in range(100):
        if not running():
            PID_FILE.unlink(missing_ok=True)
            print('Server stopped.')
            return
        time.sleep(.1)
    raise RuntimeError('Server did not stop; no other processes were signalled.')


def start():
    if pid := running():
        print(f'Already running: PID {pid}')
        return
    env = config()
    source = ROOT / '.cache/release/inspection'
    if not source.exists():
        raise RuntimeError('Run bash scripts/build-release.sh first.')
    frontend = Path(env['FRONTEND_DIR'])
    if not (frontend / 'index.html').is_file():
        raise RuntimeError('FRONTEND_DIR must contain built index.html')
    RUNTIME.mkdir(parents=True, exist_ok=True)
    staging = RUNTIME / 'inspection.next'
    shutil.copy2(source, staging)
    staging.replace(BINARY)
    with LOG.open('ab', buffering=0) as log:
        process = subprocess.Popen([str(BINARY)], cwd=ROOT, env=env,
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
            with opener.open(f'http://127.0.0.1:{port}/api/health', timeout=1) as response:
                if json.load(response).get('status') == 'ok':
                    print(f'Running: {env["LISTEN_ADDR"]}, PID {process.pid}')
                    print(f'Log: {LOG}\nData: {env["DATA_FILE"]}')
                    return
        except (OSError, ValueError):
            time.sleep(.1)
    process.terminate()
    raise RuntimeError(f'Health check failed, see {LOG}')


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
        print(f'Running: PID {pid}' if pid else 'Server is not running.')
        if not pid:
            raise SystemExit(1)


if __name__ == '__main__':
    main()
