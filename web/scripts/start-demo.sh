#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export Database__Provider="${DATABASE_PROVIDER:-Sqlite}"
if [[ "$Database__Provider" != Sqlite && "$Database__Provider" != SqlServer ]]; then echo 'Unsupported database profile' >&2; exit 2; fi
if [[ "$Database__Provider" == SqlServer && -z "${ConnectionStrings__SqlServer:-}" ]]; then echo 'Set ConnectionStrings__SqlServer for SQL Server.' >&2; exit 2; fi
if [[ "$Database__Provider" == Sqlite && -n "${SQLITE_DATA_FILE:-}" ]]; then export ConnectionStrings__Sqlite="Data Source=$SQLITE_DATA_FILE"; fi
if [[ -n "${THESIS_MANAGED_ROOT:-}" ]]; then export Storage__ManagedRoot="$THESIS_MANAGED_ROOT"; fi
port="${DEMO_PORT:-0}"
if ! [[ "$port" =~ ^[0-9]+$ ]] || (( port > 65535 )); then echo 'Invalid DEMO_PORT' >&2; exit 2; fi
port="$(python3 - "$port" <<'PY'
import socket, sys
with socket.socket() as server:
    server.bind(('127.0.0.1', int(sys.argv[1])))
    print(server.getsockname()[1])
PY
)"
export Host__Url="http://127.0.0.1:$port"
echo "Opening $Host__Url (database: $Database__Provider). Ctrl+C stops this host."
cd "$repo"
exec dotnet run --project web/src/ThesisDemo.Api --no-build --no-launch-profile
