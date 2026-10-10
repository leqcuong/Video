# Verification and Acceptance Plan

This is planned verification for implementation. No application runtime tests have
run during documentation preparation. The customer specifically requested
architecture and integration testing in the input tasks.

## Verification Matrix

| ID | Scenario and expected evidence |
| --- | --- |
| V01 | Import a two-asset fixture twice and restart; counts and IDs unchanged. Import a filename collision and show an explicit conflict/mapping. |
| V02 | Validate malformed rows, missing files, hashes and provenance. Preserve historical artifacts byte-for-byte. Present 895 current versus 896 recorded counts honestly. |
| V03 | Golden parity queries across alpha 0/0.6/1, topK 1/10, video filters and baseline/reranked modes. Same order/IDs/times and scores within tolerance. Check candidateK>=topK. |
| V04 | Stream valid/invalid/oversized files; abort upload; verify no ready asset references incomplete data and filenames cannot escape roots. |
| V05 | GET/HEAD media, full/partial/invalid byte ranges, If-Range, supported/unsupported codecs, fractional seek, end-of-segment behavior. Capture browser evidence. |
| V06 | Reject model feature/dimension/provenance mismatch; comparison uses same snapshot. Reranker preserves timestamps without assuming unchanged top1 identity/IoU. |
| V07 | Valid clip succeeds; one asset fails; CLI exits zero with failures; no empty or partial index silently replaces active index. |
| V08 | Kill/restart host; queue records survive; running attempt becomes Interrupted. Cancel/completion race and stale attempt cannot publish. Repeat idempotency key safely. |
| V09 | Track A fixture with known boundary matches, tolerance and temporal IoU; dashboard matches raw report, including event denominator. |
| V10 | Track B known scores/category counts, missing segment labels and zero samples; heuristic/human provenance stays visible. |
| V11 | Track C aggregate/query values match existing report parser; new evaluator job uses exact benchmark/model/index inputs and returns artifacts. |
| V12 | Paired significance job records iterations/seed, CI/delta values; finite bootstrap zeros retain correct labels. Do not recalculate deltas in frontend. |
| V13 | Query error/rank categories match legacy error report, with expected/retrieved playback and paginated diagnostics. |
| V14 | JSON/Markdown/CSV and integrated exports match selected inputs; unavailable/incompatible source reports block misleading merges. |
| V15 | Sanitize preserves raw benchmark and records removals; deterministic split; rubric rejects scores outside 1-5; edits publish new versions. |
| V16 | Training produces versioned model metadata; held-out and same-benchmark roles differ; activation rejects incompatible model. |
| V17 | Segmentation/modality variants stay isolated; stub ablations identified; native/proxy/skipped comparator status is preserved. |
| V18 | Bundle checksum/traversal/stale-attempt/partial-output cases; repeated import idempotent. Then one actual Colab GPU round trip with device evidence. |
| V19 | Offline rehearsal on actual laptop; measure cold load, 20 warm query latencies, RAM, seek timing, responsiveness with processing, and recovery. |
| V20 | Old search keeps its snapshot during publication; outputs link to hashes/config/model versions; metadata unknown is never fabricated. |

## Test Layers

Domain tests target state transitions and invariants. Architecture tests ensure
Domain/Application boundaries and forbidden dependency checks. Contract tests target
Python JSONL, imports, HTTP errors and artifact schemas. Integration tests use
WebApplicationFactory with persistent temporary SQLite and a real SQL Server
Testcontainer or installed SQL instance. Do not replace SQL Server concurrency checks
with the EF InMemory provider.

Python fixtures use deterministic embeddings for fast protocol/parity checks;
a smaller real-model suite verifies actual installed model behavior. The complete
historical corpus is a manual/integration dataset, not copied into unit tests.
Fixed report fixtures can validate dashboard parsing without running expensive ML.

Playwright covers search/playback, upload/jobs, comparisons, label editing and
navigation. Use real frame/thumbnail assets, desktop screenshots and a narrow
viewport; inspect loading, error states and text overflow. No decorative 3D is needed.

## Exit Gates

- M0: build and architecture/provider migration checks pass.
- M1: V01-V06, report import portions of V09-V14, and full-corpus warm-query measurement.
- M2: V07-V08 and publication portion of V20.
- M3: live evaluator, label, training, ablation and comparison cases V09-V17.
- M4: V18-V20, SQL Server and SQLite acceptance, accessibility and offline rehearsal.

Record commands, environment, revision, outcomes and limitations under
web/artifacts/verification (generated and ignored by Git). Mark tasks complete only
with corresponding evidence. A document prerequisite check cannot close these gates.

