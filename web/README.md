# Thesis Demo Web Development Pack

Status: Phase 1 foundation implemented. Import, search, processing, research and
Colab workflows are scheduled in later phases.

Run the foundation host on Windows:

~~~powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
dotnet restore web/ThesisDemo.sln --locked-mode
dotnet run --project web/src/ThesisDemo.Api
~~~

Open http://127.0.0.1:5074/ and check `/api/v1/system/health`. The host applies
the SQLite migrations on startup. Set `Database__Provider=SqlServer` and a trusted
`ConnectionStrings__SqlServer` value to exercise the SQL Server profile.
Run `web/scripts/check-environment.ps1` or `.sh` before enabling pipeline work.

Start with [the product specification](../specs/001-thesis-demo/spec.md), then
[the implementation plan](../specs/001-thesis-demo/plan.md) and
[the task backlog](../specs/001-thesis-demo/tasks.md).

The agreed setup is a .NET website on the laptop, with optional GPU processing in
a user-operated Google Colab notebook. Python remains the ML and evaluation engine.
The proposed UI is Blazor Interactive Server; the host also exposes versioned APIs.
SQL Server supports .NET practice, and SQLite supports portable demonstration.

## Documents

| Document | Purpose |
| --- | --- |
| [Spec](../specs/001-thesis-demo/spec.md) | Personas, scope, user stories, requirements, success criteria. |
| [Plan](../specs/001-thesis-demo/plan.md) | Stack, architecture, ownership, deployment, delivery phases. |
| [Research](../specs/001-thesis-demo/research.md) | Verified inputs, conflicts, and technical decisions with sources. |
| [Data model](../specs/001-thesis-demo/data-model.md) | Entities, keys, versions, states, and database portability. |
| [API contract](../specs/001-thesis-demo/contracts/api.md) | Endpoints, requests, responses, errors, and concurrency. |
| [Python contract](../specs/001-thesis-demo/contracts/pipeline-bridge.md) | Legacy calls, search protocol, artifacts, and publication. |
| [Colab contract](../specs/001-thesis-demo/contracts/colab-handoff.md) | Portable job bundles, notebook execution, result import. |
| [UI specification](../specs/001-thesis-demo/ux-spec.md) | Screens, controls, content, states, and accessibility. |
| [Traceability](../specs/001-thesis-demo/traceability.md) | Thesis/customer/backlog features mapped to work and verification. |
| [Verification](../specs/001-thesis-demo/verification-plan.md) | Contract, integration, parity, and acceptance checks. |
| [Quickstart](../specs/001-thesis-demo/quickstart.md) | Implementation prerequisites and future local run procedure. |
| [Defense runbook](../specs/001-thesis-demo/demo-runbook.md) | Rehearsal, presentation sequence, and recovery. |
| [Tasks](../specs/001-thesis-demo/tasks.md) | Ordered, reviewable development tasks with file ownership. |
| [Risks and decisions](../specs/001-thesis-demo/risks-and-decisions.md) | Assumptions, technical risks, and release decisions. |

The research document records the source material and design corrections. Source
paths for the archived pipeline refer to data outside this web-only repository.

## Current evidence

The paper records 896 assets. The current index and asset folders contain 895
distinct assets, verified on 2026-10-10. The raw benchmark contains 3,500 queries;
the cleaned benchmark contains 3,486. The application must report actual imported
counts while retaining the historical paper context.

The foundation is pinned to .NET SDK 10.0.401, installed locally under
`%LOCALAPPDATA%\Microsoft\dotnet` on this laptop. `C:\Python313\python.exe` is
64-bit; the default `python` command still resolves to a 32-bit interpreter, so
select the 64-bit path explicitly. FFmpeg and the expected local model directory
are not ready on this laptop as of 2026-10-11. See
[Phase 1 evidence](../specs/001-thesis-demo/phase1-evidence.md).

