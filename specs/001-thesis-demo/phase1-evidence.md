# Phase 1 Verification Evidence

Date: 2026-10-11 (Asia/Bangkok). Scope: T001-T008, M0 foundation only.

- SDK `10.0.401` pinned by `web/global.json`; four .NET layers and four test
  projects are in `web/ThesisDemo.sln`. Central NuGet versions and per-project
  `packages.lock.json` files restore in locked mode. Python selected:
  `C:\Python313\python.exe`, confirmed 64-bit.
- `dotnet build web/ThesisDemo.sln --no-restore`: passed, zero warnings/errors.
- `THESIS_TEST_LOCALDB=1` with `dotnet test web/ThesisDemo.sln --no-restore`:
  architecture, unit and integration tests passed, including real SQLite and SQL
  Server LocalDB migrations and stale-write rejection. Browser suite is a scaffold
  for a later phase and contains no tests yet.
- `dotnet-ef database update --context SqliteThesisDemoDbContext`: applied both
  SQLite migrations. Both provider contexts report no pending model changes.
- `web/scripts/check-environment.ps1` and Git Bash `.sh` report .NET 10 and x64
  Python ready, pipeline source present, disk about 93 GB free, CPU mode. FFmpeg
  and the local model directory report missing. No claim of ML readiness is made.
- The local host served `GET /` and `GET /api/v1/system/health` with HTTP 200 at
  `http://127.0.0.1:5074/`. API tests cover antiforgery bootstrap, rejected
  tokenless and foreign-origin POSTs, validation and ProblemDetails.

The foundation does not yet expose collection/import/search/processing commands.
Later phases implement the Python bridge and usable thesis demo workflows.
