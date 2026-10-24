#!/usr/bin/env bash
# Checks message text and catch blocks under src/ and samples/. See check-message-text.py.
set -euo pipefail
exec python3 -I "$(dirname "$0")/check-message-text.py" "$@"
