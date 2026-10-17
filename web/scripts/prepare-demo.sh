#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
provider="${DATABASE_PROVIDER:-Sqlite}"
if [[ "$provider" != Sqlite && "$provider" != SqlServer ]]; then echo 'DATABASE_PROVIDER must be Sqlite or SqlServer' >&2; exit 2; fi
if [[ "$provider" == SqlServer && -z "${ConnectionStrings__SqlServer:-}" ]]; then echo 'Set ConnectionStrings__SqlServer for SQL Server.' >&2; exit 2; fi
cd "$repo/web"
sdk="$(dotnet --version)"
if [[ "$sdk" != 10.* ]]; then echo "Expected .NET 10, got $sdk" >&2; exit 2; fi
dotnet restore ThesisDemo.sln --locked-mode
dotnet build ThesisDemo.sln --no-restore
if [[ "${SKIP_TESTS:-0}" != 1 ]]; then dotnet test ThesisDemo.sln --no-build --no-restore --verbosity quiet; fi
echo 'Prepared. Check model, media, FFmpeg, providers, browser, and offline rehearsal separately.'
