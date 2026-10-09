#!/usr/bin/env python3
"""
JSON bridge between Chitra Studio (.NET) and the official Kaggle API client (pip package "kaggle").

Usage:  python bridge.py whoami
        python bridge.py push <folder> <timeoutSeconds> <accelerator>
        python bridge.py status <owner/slug>
        python bridge.py output <owner/slug[/version]> <destFolder>
        python bridge.py logs <owner/slug>

Always prints exactly one JSON object on stdout. Credentials are read by the Kaggle client from
~/.kaggle/access_token, ~/.kaggle/kaggle.json or the KAGGLE_API_TOKEN / KAGGLE_USERNAME+KAGGLE_KEY environment
variables. They are never printed or passed on.
"""

import contextlib
import io
import json
import sys

# Everything the Kaggle client prints goes to stderr; stdout carries only our single JSON object.
REAL_STDOUT = sys.stdout
sys.stdout = sys.stderr


def emit(obj: dict, code: int = 0) -> None:
    REAL_STDOUT.write(json.dumps(obj))
    REAL_STDOUT.flush()
    sys.exit(code)


def quietly(fn, *args, **kwargs):
    """Runs a Kaggle API call with its console output captured, so only our JSON reaches stdout."""
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer), contextlib.redirect_stderr(buffer):
        return fn(*args, **kwargs)


def client():
    try:
        from kaggle.api.kaggle_api_extended import KaggleApi
    except ImportError:
        emit({"ok": False, "auth": False, "error": "The 'kaggle' Python package is not installed for this Python (pip install kaggle)."}, 2)
    api = KaggleApi()
    try:
        quietly(api.authenticate)
    except (SystemExit, Exception):
        emit({"ok": False, "auth": True, "error": "Kaggle credentials not found or invalid. Create an API token at "
              "https://www.kaggle.com/settings and save it to ~/.kaggle/access_token (or set KAGGLE_API_TOKEN)."}, 3)
    return api


def main() -> None:
    if len(sys.argv) < 2:
        emit({"ok": False, "error": "missing command"}, 1)
    command, args = sys.argv[1], sys.argv[2:]
    api = client()
    try:
        if command == "whoami":
            emit({"ok": True, "username": api.get_config_value("username")})
        elif command == "push":
            folder, timeout, accelerator = args[0], args[1], args[2] if len(args) > 2 and args[2] else None
            result = quietly(api.kernels_push, folder, timeout, accelerator)
            if result is None:
                emit({"ok": False, "error": "Kaggle returned no response to the push."}, 1)
            error = getattr(result, "error", None)
            emit({"ok": not error, "error": error or None,
                  "version": getattr(result, "versionNumber", None) or getattr(result, "version_number", None),
                  "url": getattr(result, "url", None)}, 0 if not error else 1)
        elif command == "status":
            result = quietly(api.kernels_status, args[0])
            status = getattr(result, "status", None)
            name = getattr(status, "name", None) or str(status).split(".")[-1]
            emit({"ok": True, "status": name.upper() if name else "UNKNOWN",
                  "failureMessage": getattr(result, "failure_message", None) or None})
        elif command == "output":
            kernel, dest = args[0], args[1]
            files, token = [], None
            while True:
                page, token = quietly(api.kernels_output, kernel, dest, file_pattern=None, force=True, quiet=True,
                                      page_token=token, page_size=200)
                files.extend(page or [])
                if not token:
                    break
            emit({"ok": True, "files": files})
        elif command == "logs":
            text = quietly(api.kernels_logs, args[0]) or ""
            emit({"ok": True, "log": text[-20000:]})
        else:
            emit({"ok": False, "error": f"unknown command {command}"}, 1)
    except SystemExit:
        raise
    except Exception as e:  # report API errors (404, quota, network) without stack traces or secrets
        emit({"ok": False, "error": str(e)[:2000]}, 1)


if __name__ == "__main__":
    main()
