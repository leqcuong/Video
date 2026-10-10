# Risks, Assumptions and Decisions

## Confirmed and Proposed

Confirmed by customer: website on laptop, Colab for processing; .NET preferred;
all thesis capabilities and relevant docs must be covered; apply GitHub Spec Kit.
Customer drafts explicitly request Clean Architecture, SQL Server/SQLite, EF/Dapper,
background jobs and architecture/integration tests.

Proposed defaults: Blazor Interactive Server, English text, local single operator,
SQL Server engineering profile, SQLite portable profile, manual Colab bundle transfer.
These can change by editing the related specification and decision record.

## Risk Register

| Risk | Impact | Mitigation and closure evidence |
| --- | --- | --- |
| Only .NET 9 discovered | .NET 10 solution cannot build yet. | Install/pin SDK; M0 build evidence. |
| Default Python is 32-bit | ML packages may not install/run. | Select compatible x64 environment; model smoke test. |
| 895 current vs 896 historical assets | Misleading inventory and failed reproduction assumptions. | Count actual imports, preserve recorded context, investigate missing asset separately. |
| Same benchmark referenced in training/evaluation | Overstatement of generalization. | Label replay; new split/version metadata and overlap diagnostics. |
| Heuristic Track B/derived Track A labels | Overstatement of human validation. | Provenance badges and review/versioning workflow. |
| Model/code silent fallbacks | Fake-looking captions/invalid semantic space. | Capability preflight and provenance checks; reject learned-search fallback vectors. |
| ~204 MB JSON index expansion | Memory pressure or slow search. | Cache one snapshot, measure decoded RAM and latency, avoid browser payloads of vectors. |
| Search/processing CPU contention | Presentation freezes. | Separate supervised processes, one heavy job, pause heavy work in rehearsal as needed. |
| Colab device not explicitly used by current caption path | GPU availability without acceleration. | Adapter device configuration and actual GPU utilization verification. |
| Interrupted sync/process publication | Corrupt or incomplete active index. | Checksums, attempt identity, staging and atomic activation. |
| SQLite vs SQL Server differences | Concurrency and migration bugs. | Separate migrations/tokens; shared integration scenarios on both providers. |
| Existing CI targets removed web | New documentation does not repair build pipeline. | Audit and update web-specific CI in T051, preserving legacy checks. |
| Optional comparator checkpoints/evaluation domains | Invalid comparisons or long setup. | Capability status, native/proxy distinction, separate datasets when incomparable. |
| Delivery slot/hardware unknown | Unreliable timeline/performance promises. | Milestone exit gates; measure on laptop; adjust demo sequence after rehearsal. |

No user decision blocks preparation of these development documents. SDK installation,
model environment selection, measured performance, true Colab execution, and runtime
acceptance remain implementation work. Public multi-user deployment requires a
separate feature specification.

