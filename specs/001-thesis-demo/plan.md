# Implementation Plan: Thesis Defense Demo

Feature: 001-thesis-demo | Date: 2026-10-10 | [Spec](spec.md)
Status: proposed implementation design, no application code delivered.

## Summary

Build a local modular monolith in .NET 10. Use Blazor Interactive Server for the
browser UI and ASP.NET Core controllers for HTTP contracts and media. Python
continues to perform ML, retrieval, and evaluation. Support direct laptop execution
and portable job bundles executed interactively in Colab.

## Technical Context

| Area | Decision |
| --- | --- |
| Language | C#/.NET 10; Python in an explicitly selected 64-bit environment. |
| UI | Blazor Interactive Server, self-hosted fonts/assets, HTML video and minimal JS interop. |
| Backend | ASP.NET Core controllers, explicit CQRS handlers, native DI, centralized IExceptionHandler/ProblemDetails. |
| Persistence | EF Core 10, SQL Server and SQLite provider profiles; Dapper evaluation reads. |
| Job execution | DB durable queue; bounded Channel<Guid>; BackgroundService; per-job DI scope. |
| ML | Existing BLIP, sentence-transformers, PySceneDetect, BM25 and NumPy reranker. |
| Storage | Local files; versioned snapshots; optional portable Colab bundles. |
| Tests | xUnit, WebApplicationFactory, NetArchTest, Playwright; SQL Server Testcontainers when Docker is available. |
| Platforms | Laptop Windows first; Linux-compatible paths and Python handoff; Colab notebook compute. |
| Scale | One operator; two tabs; current 895 indexed assets; one active heavy job. |
| Targets | Warm search <=3s p95, job acceptance <=1s excluding upload, measured on target hardware. |

Select exact NuGet/Python package versions during T001/T004 and commit locks.
Do not mix Python 32-bit environments with GPU/ML dependencies.

## Constitution Check

| Principle | Design compliance |
| --- | --- |
| Defense/evidence | Import existing corpus; label historical reports and heuristic labels; offline rehearsal. |
| Architecture | Four .NET layers; no forbidden frameworks; Python remains computation authority. |
| Durability | Database owns jobs, manifests and immutable attempt directories own artifacts. |
| Portability | Explicit provider choice; separate migrations and concurrency tokens. |
| Resource discipline | Loopback host; one heavy job; warm search process with serialized requests initially. |
| Verification | Parity, media, recovery, provider and browser acceptance tasks are explicit. |

No constitution exception is currently required.

## Component Structure

~~~text
web/
  src/
    ThesisDemo.Domain/           # entities, value objects, transitions
    ThesisDemo.Application/      # use cases, ports, DTOs, validation
    ThesisDemo.Infrastructure/   # EF/Dapper, files, jobs, Python supervision
    ThesisDemo.Api/              # controllers, middleware, Blazor Components, wwwroot
  bridge/                       # Python JSONL adapter and job wrapper
  notebooks/                    # interactive Colab runner
  scripts/                      # environment check, prepare, launch, export/import
  tests/
    ThesisDemo.UnitTests/
    ThesisDemo.ArchitectureTests/
    ThesisDemo.IntegrationTests/
    ThesisDemo.BrowserTests/
    bridge/
  config/                       # development settings and profile templates
  ThesisDemo.sln
specs/001-thesis-demo/            # implementation authority
~~~

These source directories and commands are planned deliverables. Only documentation
exists in this web repository at this stage. The original customer inputs and
Spec Kit tooling are archived outside it.

~~~mermaid
flowchart LR
  Browser[Browser: Blazor and video] --> Host[ASP.NET host]
  Host --> App[Application handlers]
  App --> Db[(SQL Server or SQLite)]
  App --> Files[Artifact and media store]
  Worker[BackgroundService] --> Db
  Worker --> Python[Python job process]
  App --> Search[Warm Python search process]
  Python --> Legacy[Legacy modules]
  Search --> Legacy
  Worker --> Bundle[Colab job bundle]
  Bundle --> Notebook[Interactive Colab notebook]
  Notebook --> Result[Verified result bundle]
  Result --> Worker
~~~

Api references Application and Infrastructure for composition. Infrastructure
references Application/Domain. Application references Domain. Domain references
only platform libraries. Feature folders inside layers group Library, Processing,
Search, Research, and Demo responsibilities.

Blazor components call Application handlers in-process with per-operation scopes;
external API controllers call the same handlers. No component holds a long-lived
DbContext across a Blazor circuit. IDbContextFactory/scoped handlers create and
dispose a context per operation. Browser video uses the media HTTP route.

## Execution and Publication

1. Upload bytes into a temporary file under managed storage; validate and atomically
   rename after completion. Create an asset only for completed storage.
2. Create a run and input/config snapshot transactionally. A dispatcher polls
   Queued rows and signals IDs through a bounded channel.
3. The worker claims the run with optimistic concurrency and an attempt token.
   Spawn Python with ArgumentList, a controlled working directory and environment.
4. Read stdout/stderr concurrently. Parse only versioned bridge JSONL as machine
   events; persist bounded logs separately.
5. Validate outputs, counts, checksums, embedding identity and asset mappings.
   Build the complete candidate collection index in a staging location.
6. Store an immutable index snapshot and its asset/segment-set membership. Update
   Collection.ActiveIndexId in a short database transaction only when permitted.
7. Search pins a snapshot for each request. A failed activation retains the old
   pointer. New requests see the new version; in-flight requests retain theirs.

Default heavy concurrency: one process across processing/evaluation/training.
A dedicated search process remains warm. Do not load BLIP in the search worker.
Pause heavy work for defense mode if measured CPU/RAM contention harms interaction.

After host crash, retain Queued jobs, mark lost Running attempts Interrupted, and
offer resume as a new attempt. A canceled run is terminal; late outputs remain
unpublished. Do not automatically rerun expensive failed work at every startup.

## Index and Import Strategy

Use Python's index as retrieval authority. SQL stores segments for browsing and
relationships for versions, not a replacement scoring engine. Retain row order
and double-precision timestamps through import. Store source JSON unchanged.

For new videos, stage filenames using stable asset UUIDs to prevent legacy basename
collisions. Maintain mappings between collection-scoped legacy IDs and web IDs.
For existing data, preserve original legacy IDs so benchmark spans still resolve.

Each snapshot specifies exactly one SegmentSet per included asset. Adding an asset
creates a new full snapshot that reuses unchanged sets and embeddings. Changing
caption text, segmentation, or embedding model requires compatible reindexing.
Model/dimension/config mismatch blocks publication; never blend embedding spaces.

The original process_directory overwrites its output index. Invoke it only within
a job-owned staging directory. Its resume logic checks file presence, so the web
wrapper must additionally validate hashes/config and completeness before reuse.

## Storage Layout

~~~text
data/
  catalog.db                           # SQLite profile only
  media/{collectionId}/{assetId}/source.ext
  thumbnails/{segmentSetId}/{segmentId}.jpg
  jobs/{runId}/attempts/{attemptId}/     # input, output, logs, summary
  snapshots/{snapshotId}/               # index, membership, provenance
  reports/{evaluationId}/
  benchmarks/{versionId}/
  labels/{versionId}/
  models/{modelVersionId}/
  handoffs/{handoffId}/
~~~

Data is outside source control. Register legacy roots explicitly and resolve
relocated source_video paths through a mapping; never let browser input specify
an arbitrary root. A missing media file may coexist with a valid searchable segment.

## Delivery Milestones

| Milestone | Exit evidence |
| --- | --- |
| M0 Foundation | Compiling solution, provider migrations, diagnostics, adapter fixtures. |
| M1 Defense core | Imported corpus, parity-checked search, playback, recorded Track A/B/C views. |
| M2 Processing | Real short-clip run, cancellation/recovery, version-safe activation. |
| M3 Research completeness | Benchmark/label review, all evaluators, training, ablations, error/significance exports. |
| M4 Colab and release | One real Colab round trip, SQL/SQLite checks, offline defense rehearsal. |

M1 is a useful early demo, not completion of the user's full requested feature set.
No calendar estimate is asserted before hardware and model readiness are measured.

## Complexity Tracking

Blazor avoids a second frontend runtime. Four projects enforce requested boundaries.
Dapper has one named use case: evaluation comparison/query-outcome projections.
A persistent search subprocess is justified by a ~204 MB JSON index and model
startup costs; it is a local adapter process, not a separately deployed microservice.

