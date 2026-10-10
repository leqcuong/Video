# Research and Decision Record

Inspected: 2026-10-10. This file distinguishes observed facts from proposed choices.

## Source Register

| ID | Source | Use |
| --- | --- | --- |
| S1 | ../../web/customer request/Web.md and constitution.md, spec.md, plan.md, tasks.md | Customer stack, architecture, UI, and learning objectives. |
| S2 | ../../legacy-pipeline/docs/paper/final_paper.docx | Primary thesis: chapters 3, 4, 5 and appendices. DOCX paragraphs and tables inspected through XML. |
| S3 | ../../legacy-pipeline/docs/paper/metric_lock_full_rerun_wave100_v1.md | Recorded wave100 experiment profile and metric values. |
| S4 | ../../legacy-pipeline/docs/evaluation_protocol.md and labeling_notes.md | Evaluation inputs, label provenance, interpretation. |
| S5 | ../../legacy-pipeline/app/schema.py, process.py, index.py, caption.py, segment.py | Actual processing and retrieval contracts. |
| S6 | ../../legacy-pipeline/app/evaluate_reranker.py, train_reranker.py, compare_models.py, analyze_retrieval_significance.py | Ranking, training, comparison, significance implementations. |
| S7 | ../../legacy-pipeline/outputs/full_rerun/ | Existing index, media mappings, model, and report evidence. |
| S8 | ../../legacy-pipeline/BACKLOG_Timeline_Video_Search.md | Older production backlog, explicitly dispositioned in traceability.md. |
| S9 | ../../web/customer request/PROJECT_OVERVIEW.md and WEB_PART_SPEC.md | Earlier product overview and integration requirements. |

The customer draft's [cite: N] tokens have no source bibliography. They are not used
as evidence identifiers here. S2/S3/S7 take precedence over historical paper-edit
notes about earlier 32-query runs. Current implementation facts come from S5/S6.
Features discussed as future research or literature comparisons are labeled accordingly.

## Observations That Change the Draft

1. Corpus discrepancy: 895 segment folders and 895 distinct index asset IDs are
   currently present; the paper and metric lock say 896. Import must count actual
   artifacts and report the historical discrepancy. No root cause was established.
2. Raw benchmark has 3,500 rows; the cleaned artifact has 3,486. The raw docs file
   must not be silently substituted in the thesis replay preset.
3. Track A has 3,000 event labels. Track B has 4,000 labels; labeling_notes identifies
   heuristic caption scoring and benchmark-derived event spans. They are not fully
   independently human-validated ground truth.
4. Saved reranker metadata reports 3,486 training queries and the same cleaned
   benchmark path referenced by evaluation. No held-out evaluation is established by
   these artifacts. Replay remains useful, but new research runs must record splits.
5. Reranking preserves candidate timestamps but can choose a different top result.
   Mean Top-1 IoU can therefore change in general. Equality in the stored report is
   an observation of that run.
6. Current ASR returns []; OCR returns null. Captions may silently fall back to
   generic text. Missing embedding dependencies can produce 256-dimensional hash
   vectors. A demo preflight must reject such fallbacks for learned semantic search.
7. Keyframes are extracted in memory by the caption path. Persistent thumbnail
   artifacts are a new web adapter responsibility, not an existing guarantee.
8. process_directory catches per-asset errors and can exit successfully with failures.
   Interpret its summary/manifest and validate files; exit code alone is insufficient.
9. combined_index.json is 203,767,556 bytes in this checkout. Cache one selected
   snapshot in the Python search worker; measure decoded memory and loading time.
10. Live search is a Python function, not an existing search CLI. The proposed
    bridge must supply a structured request/response entrypoint.
11. The only discovered .NET SDK is 9.0.102. The default Python is 32-bit 3.13.
    .NET 10 and a selected compatible 64-bit ML interpreter are implementation prerequisites.

## Technical Decisions

| Decision | Choice and reason | Alternative considered |
| --- | --- | --- |
| D01 | One ASP.NET Core 10 host with Blazor Interactive Server and API controllers; maximizes C# practice and local simplicity. | Separate React/Next frontend adds another toolchain. |
| D02 | SQL Server for engineering practice; SQLite for portable demo, explicitly selected. | Automatic database fallback risks silently using different data. |
| D03 | EF Core write/state operations; Dapper evaluation projection; explicit handlers/mapping. | MediatR/AutoMapper conflict with customer constraints. |
| D04 | DB-backed job state plus bounded Channel<Guid> and BackgroundService. | Channel alone loses queued work on restart. |
| D05 | Supervised Python child processes; a warm JSONL search process plus one heavy-job process. | Repeated query process/model startup hurts latency; rewriting ML changes the experiment. |
| D06 | Immutable artifact/index snapshots and DB activation pointer. | In-place overwriting can mix search and evaluation versions. |
| D07 | Notebook-operated Colab bundle exchange, manual transfer first. | Always-on remote worker and tunnel hosting are unnecessary for the confirmed setup. |
| D08 | Import original reports before running expensive evaluations. | Recomputing the entire corpus during the presentation is unreliable. |
| D09 | SQL rowversion and SQLite application token; UTC DateTime and double seconds. | SQLite does not provide SQL Server rowversion semantics. |

Blazor server interactivity depends on its local host connection; offline here means
no internet dependency while the laptop server stays running. See
[Microsoft render modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0).

SQL Server and SQLite need different concurrency/migration treatment; see
[SQLite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations).
The hosting pattern uses scoped work within a BackgroundService; see
[hosted services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0).

.NET 10 is the selected supported LTS target; pin the SDK and dependency versions at
implementation kickoff using the [official support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

Colab resources and sessions can terminate, and free managed runtimes restrict some
web-UI and distributed-worker usage. The confirmed design uses interactive notebook
computation and local playback. See [Google Colab FAQ](https://research.google.com/colaboratory/faq.html).
Paid Colab is not assumed.

## Spec Kit Application

Official [GitHub Spec Kit v1.1.3](https://github.com/github/spec-kit/tree/v1.1.3)
templates and prerequisite scripts were copied into .specify. The standard sequence
was applied through specification, research/design, tasks, and document analysis.
The tooling is retained with the archived project and excluded from this web
repository. There is no claim that agent slash commands ran or that the new
application has been built.

