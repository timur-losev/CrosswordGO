#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VENV_DIR="$ROOT_DIR/.venv"
SCRIPT_PATH="$ROOT_DIR/enrich_word_lists.py"
REQUIREMENTS_PATH="$ROOT_DIR/requirements.txt"

resolve_bootstrap_python() {
  if command -v python3 >/dev/null 2>&1; then
    echo "python3"
    return 0
  fi
  if command -v python >/dev/null 2>&1; then
    echo "python"
    return 0
  fi
  if command -v py >/dev/null 2>&1; then
    echo "py"
    return 0
  fi
  return 1
}

resolve_venv_python() {
  if [ -x "$VENV_DIR/bin/python" ]; then
    echo "$VENV_DIR/bin/python"
    return 0
  fi
  if [ -x "$VENV_DIR/Scripts/python.exe" ]; then
    echo "$VENV_DIR/Scripts/python.exe"
    return 0
  fi
  if [ -x "$VENV_DIR/Scripts/python" ]; then
    echo "$VENV_DIR/Scripts/python"
    return 0
  fi
  return 1
}

if [ ! -f "$SCRIPT_PATH" ]; then
  echo "Python script not found: $SCRIPT_PATH" >&2
  exit 1
fi

if ! VENV_PYTHON="$(resolve_venv_python)"; then
  BOOTSTRAP_PYTHON="$(resolve_bootstrap_python || true)"
  if [ -z "$BOOTSTRAP_PYTHON" ]; then
    echo "Python is not available. Install Python 3 and retry." >&2
    exit 1
  fi

  echo "Creating virtual environment in $VENV_DIR ..."
  "$BOOTSTRAP_PYTHON" -m venv "$VENV_DIR"
  VENV_PYTHON="$(resolve_venv_python)"
fi

"$VENV_PYTHON" -m pip install --upgrade pip
if [ -f "$REQUIREMENTS_PATH" ]; then
  "$VENV_PYTHON" -m pip install -r "$REQUIREMENTS_PATH"
fi

exec "$VENV_PYTHON" "$SCRIPT_PATH" "$@"
