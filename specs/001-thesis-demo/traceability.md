# Requirements and Source Traceability

S1-S9 refer to research.md. All FR requirements are mandatory for the full release.
P1/P2 describe build order, not permission to silently drop P2 functionality.

| Requirement | Evidence/source | Delivery and UI | Task IDs | Verification |
| --- | --- | --- | --- | --- |
| FR-001 | S1/S9 collection management | Library, isolated IDs/storage | T005,T009,T010 | V01,V04 |
| FR-002 | S1/S7/S9 existing corpus | Import preview and versioned commit | T011,T012 | V01,V02 |
| FR-003 | S2 ch.3/S5 segment artifacts | Library timeline and text fields | T014,T015 | V02,V05 |
| FR-004 | S2 ch.3/S5 hybrid formula | Search controls and Python adapter | T013,T016,T017 | V03 |
| FR-005 | S2 ch.3-4/S6 reranker | Search mode comparison | T018,T019 | V03,V06 |
| FR-006 | S1/S9 timestamp playback | Player, range route, interval rail | T014,T015 | V05 |
| FR-007 | S1/S5 process entrypoint | Upload and explicit run creation | T026,T027 | V04,V07 |
| FR-008 | S1/S5 resume manifest | Job monitor, durable dispatch | T007,T028,T029,T030 | V07,V08 |
| FR-009 | S2 ch.3-4/S4 Track A | Research segmentation view/runner | T021,T032 | V09 |
| FR-010 | S2 ch.3-4/S4 Track B | Caption view/review/runner | T021,T034,T035 | V10 |
| FR-011 | S2 ch.4/S3/S6 Track C | Ranking reports and rerun | T021,T036 | V11 |
| FR-012 | S2 ch.4/S6 bootstrap | CI chart/table and rerun | T023,T037 | V12 |
| FR-013 | S2 ch.4/S7 error pack | Query diagnostic playback | T022,T038 | V13 |
| FR-014 | S4 integrated reports/S8 exports | Artifact download and summary export | T024,T039 | V14 |
| FR-015 | S2 appendix/S4/S6 labels/sanitize/split | Benchmarks and Labels | T031,T033,T034 | V15 |
| FR-016 | S2 ch.3/S6/S8 model lifecycle | Model import/train/activate | T018,T040,T041 | V06,V16 |
| FR-017 | S4 variants/S6 comparisons; S2 literature/future | Experiment controls with availability flags | T042,T043 | V17 |
| FR-018 | S1 + explicit customer confirmation | Colab bundle workflow | T044,T045,T046,T047 | V18 |
| FR-019 | Customer defense goal | Preflight, Demo and rehearsal | T004,T048,T049,T052 | V19 |
| FR-020 | S2 limitations/S3/S4 reproducibility | Provenance and version display everywhere | T006,T011,T020,T050 | V02,V20 |

## Thesis Coverage and Honest Boundaries

Segmentation, dense visual descriptions, hybrid retrieval, reranking and Tracks A/B/C
are core capabilities. Significance, qualitative errors and integrated reporting
are included. The thesis's literature discussion of CLIP4Clip/X-CLIP/MMT does not
establish an equivalent evaluated model in every local environment; the web includes
the comparison surface and uses the legacy adapters only when available.

ASR/OCR contracts and imported text are supported, with current stubs visible.
Implementing new speech/OCR models would change the research baseline and is an
explicit later extension. Full human relabeling and expanded future datasets are
research work; the web supplies review/versioning tools but cannot claim the labels
have been reviewed merely by showing them.

The historical same-benchmark reranker result is a replay. New training and
evaluation can use a documented split, but a split alone does not eliminate duplicate
asset/clone leakage; grouping and overlap diagnostics remain necessary.

## Older Backlog Disposition

Every BL item belongs to exactly one range or explicit list below. This translates
the old production architecture to the confirmed local .NET scope.

| Old backlog IDs | Disposition for this release |
| --- | --- |
| BL-001..006 | Replace Python web/Postgres foundations with .NET layers, EF Core and SQL Server/SQLite. |
| BL-007..009 | OAuth/JWT/RBAC deferred; local single operator, collection boundaries retained. |
| BL-010..011 | Keep storage interface and local storage implementation. |
| BL-012 | S3 adapter deferred. |
| BL-013..015 | Replace chunk protocol with bounded streamed uploads and explicit retry; resumable upload later. |
| BL-016..017 | Replace Celery/Redis chains with durable DB jobs and BackgroundService. |
| BL-018..025 | Keep Python bridge, processing stages, artifact persistence and index publication; expose modality stubs. |
| BL-026..032 | Keep idempotent jobs, status/events, search/rerank, segments and local byte-range media. |
| BL-033..034 | Replace Next.js/client tooling with Blazor and shared application/API DTOs. |
| BL-035 | Login/OAuth UI deferred with identity scope. |
| BL-036..040 | Keep collection selection, upload, job monitor, search, playback; single local workspace. |
| BL-041..050 | Keep evaluations, reranker registry/versioning, imports and ranking parity. |
| BL-051..054 | Docker/NGINX deployment optional later; local scripts and two DB profiles now. |
| BL-055..056 | Keep structured logs and per-run events. |
| BL-057 | Prometheus deferred; local readiness and timing diagnostics now. |
| BL-058..059 | Keep bounded retry, timeout, resume validation and recovery. |
| BL-060..061 | Keep file validation and collection-scoped access checks; no multi-user roles. |
| BL-062 | Signed public URLs deferred; controlled local media route now. |
| BL-063 | Keep build, format, architecture and contract CI checks for .NET/bridge. |
| BL-064 | Container-build gate applies only if optional container deployment is added. |
| BL-065..070 | Keep vertical slice/evaluation/rerank checks and docs; VM deployment becomes local/Colab runbooks. |

These are explicit scope translations, not claims that the old backlog is implemented.

