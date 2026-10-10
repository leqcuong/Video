# Local Development and Runtime Quickstart

Status: implementation runbook specification. Commands referring to web/scripts or
ThesisDemo.Api are required future deliverables and do not exist yet. The Spec Kit
tooling used to prepare these documents is archived outside this web repository.

Read spec.md, plan.md, contracts, then tasks.md before implementation.

## Prerequisites to Prepare

Install a .NET 10 SDK for development and pin the selected patch in global.json.
The inspected laptop has 9.0.102 only. Select a 64-bit Python environment compatible
with the legacy ML stack; the default PATH interpreter is currently 32-bit 3.13.
The environment check must print interpreter path, bitness, versions and model
availability. CPU is sufficient for prepared search; verify memory and latency.

SQL Server is the engineering profile. SQLite is the portable profile, with no
external database service. Docker is optional unless using SQL Testcontainers.
Prepare FFmpeg/ffprobe or the selected media-probing tool and browser-playable media.
Cache models and UI assets before going offline. GPU processing is optional locally.

## Planned Configuration

The implementation must provide development config templates for:

| Key | Purpose |
| --- | --- |
| ASPNETCORE_URLS | Loopback URL, e.g. http://127.0.0.1:5180; choose another free port if occupied. |
| Database:Provider | SqlServer or Sqlite, selected explicitly. |
| ConnectionStrings:Catalog | Local DB connection; credentials outside Git. |
| Storage:Root | Managed media/artifact data root. |
| Legacy:Root | Existing legacy-pipeline directory. |
| Legacy:PythonExecutable | Absolute selected interpreter path. |
| Legacy:RegisteredSources | Named allowlisted import roots. |
| Jobs:MaxConcurrentHeavy | Default 1. |
| Jobs:ExecutionTimeoutMinutes | Configurable per operation, with visible timeout. |
| Uploads:MaxBytes | Default proposal 2 GiB per file; tune and show before upload. |
| Demo:Profile | ThesisReplay or LiveResearch. |

Environment overrides use ASP.NET double underscores, for example
Database__Provider. SQL credentials use user secrets or local environment settings.
No need for external login, API keys, Redis, or paid compute in the baseline setup.

## Planned Laptop Procedure

After implementing T001-T052:

1. Run web/scripts/check-environment.ps1 and resolve required diagnostics.
2. Restore/build the .NET solution and prepare the explicitly selected Python environment.
3. Run web/scripts/prepare-demo.ps1 with the configured database provider. It applies
   reviewed migrations, registers source roots, caches models, and previews imports.
   Import is idempotent and does not regenerate captions or overwrite reports.
4. Start web/scripts/start-demo.ps1. It selects a free configured port, launches
   hidden background helpers as needed, and prints the local browser URL.
5. Import/activate the prepared snapshot after provenance checks. Warm the embedder,
   index and a probe query, then run the demo preflight.
6. Keep the server running for browser interaction; disconnect internet for rehearsal.

Provide Bash equivalents for Linux. A script's status must reflect actual dependency
readiness; it must not install large models silently on every start.

## Existing Data Locations

Relative to repository root:

- Corpus: legacy-pipeline/videos/all_videos
- Artifacts: legacy-pipeline/outputs/full_rerun
- Index: legacy-pipeline/outputs/full_rerun/index/combined_index.json
- Clean benchmark: legacy-pipeline/outputs/full_rerun/docs/benchmark_queries_wave100_v1_full_rerun_wave100_v1_clean.json
- Labels: legacy-pipeline/docs/segment_boundary_labels_wave100_v1.json and caption_quality_labels_wave100_v1.json
- Reranker: legacy-pipeline/outputs/full_rerun/models/reranker_logreg_full_rerun_wave100_v1.json
- Reports: legacy-pipeline/outputs/full_rerun/eval

Source_video values are machine paths; configure root remapping when moving to
another laptop. Preserve original artifacts and register new paths in the catalog.

## Colab Procedure

Export a selected processing job, open the planned web/notebooks/colab-runner.ipynb,
verify bundle hashes, run the notebook interactively, and import its result bundle
locally. See contracts/colab-handoff.md. No web hosting or laptop database runs on Colab.

