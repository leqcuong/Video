# Feature Specification: Local Thesis Defense Web Demo

**Feature ID**: 001-thesis-demo  
**Created**: 2026-10-10  
**Status**: Ready for implementation planning; runtime acceptance pending.  
**Input**: Customer drafts in web/customer request, final_paper.docx, legacy docs,
and customer confirmation that the website runs on the laptop and Colab handles computation.

## Product Goal and Scope

Enable the presenter to demonstrate natural-language retrieval of precise video
moments and explain the thesis's three evaluation tracks with inspectable evidence.
A secondary goal is practicing maintainable .NET engineering.

Primary personas: the presenter operating the demo, the thesis committee examining
results, and the developer extending the system. One local operator and up to two
browser tabs are the release workload. The website remains usable without internet
after dependencies, models, and demo data have been prepared.

Required coverage includes import, media inspection, baseline and reranked search,
processing, Tracks A/B/C, significance, error analysis, reproducibility, exports,
benchmark/label review, reranker lifecycle, experiment comparisons, and Colab handoff.
Full corpus processing is a preparation workflow; a short selected clip demonstrates
live processing. Precomputed results may be shown with their provenance.

Production multi-user identity, SSO, billing, S3 hosting, public deployment, and
distributed workers are future work. The old backlog's individual dispositions
are in traceability.md. Real ASR/OCR engines are future extensions: the current
contract and empty modality states must work, and imported real text must display.

## User Scenarios & Testing

### US1 - Open the existing corpus (P1)

As presenter, I can import the prepared corpus and return to it after restarting.

**Independent test**: Import a fixture collection twice, restart, and inspect it.

1. Given a registered corpus root, when I preview import, then counts, unresolved
   media, duplicate IDs, malformed artifacts, and model provenance are reported.
2. Given a valid import, when I commit it, then assets, segments, index version,
   and report references persist without running caption generation.
3. Given the same artifact hashes, when I import again, then no duplicates appear.
4. Given historical reports say 896 but 895 assets are present, then both facts
   appear in their respective contexts and no missing asset is invented.

### US2 - Search, compare, and play (P1)

As presenter, I search moments, compare baseline and reranked order, and play them.

**Independent test**: Use an imported fixture with a known index/model and media.

1. Given a ready index, when I search, then results preserve legacy order, IDs,
   timestamps, and scores for the same parameters.
2. Given a compatible reranker, when I compare modes, then both lists use the same
   index, query, and scope, and show rank changes without changing segment bounds.
3. Given a result at 75.25 seconds, when I play it, then the player seeks after
   metadata loads and highlights the selected interval.
4. Given ASR/OCR are unavailable, then evidence is labeled unavailable, not a
   successful multimodal match; scores are not displayed as calibrated confidence.
5. Given a missing source file, search still shows the segment and playback states
   that media is unavailable.

### US3 - Present research evidence (P1)

As presenter, I inspect Tracks A/B/C, significance, failures, and source reports.

**Independent test**: Import the canonical report pack without running ML.

1. Given historical reports, then their benchmark, model, settings, timestamp,
   label provenance, and recorded status accompany metrics and exports.
2. Given comparable evaluation runs, then I can compare aggregate metrics and
   query outcomes; incompatible benchmark/index/label versions are identified.
3. Given bootstrapped caption labels, then their heuristic origin remains visible.
4. Given a bootstrap p-value serialized as zero, then the raw value is preserved
   and identified as a finite-resampling estimate, not proof of zero probability.

### US4 - Upload and process a new clip (P1)

As presenter, I upload a small clip and monitor processing without freezing search.

**Independent test**: Process a small supported clip; repeat with one invalid asset.

1. Given a completed upload, then metadata is saved and I can explicitly start a job.
2. Given a committed job, the request returns an accepted job ID before ML finishes.
3. Given partial asset failure, successful artifacts remain staged for inspection;
   publishing available results requires an explicit action.
4. Given cancellation or application restart, the old active index remains usable,
   incomplete results are not published, and job recovery is explained.
5. Given reprocessing, the previous snapshot remains available for replay.

### US5 - Run and review experiments (P2, required before complete release)

As researcher, I run Tracks A/B/C, inspect labels, train a reranker, and compare variants.

**Independent test**: Run each evaluator on a small labeled fixture and inspect its artifacts.

1. Given a selected benchmark, I can validate/sanitize it and preserve raw and cleaned
   versions, excluded counts, and the sanitizer configuration.
2. Given labels, I can review event spans and caption rubric scores, save a new
   label version, and evaluate it without overwriting historical labels.
3. Given a train/evaluation split, model training records its input hashes and seed;
   evaluation distinguishes held-out results from same-benchmark replay.
4. Given ablation/segmentation variants, their outputs and modality capabilities
   remain isolated. Disabled stubs cannot produce claims of ASR/OCR benefit.
5. Given optional CLIP4Clip/X-CLIP/MMT results, the comparison marks native,
   adapter, proxy, skipped, or incompatible results explicitly.

### US6 - Use Colab computation (P2, required handoff capability)

As presenter, I export a job, run it in an interactive Colab notebook, and import results.

**Independent test**: Round-trip a small job bundle with altered path roots.

1. Given a job bundle, notebook execution verifies its inputs before processing.
2. Given a completed result bundle, laptop import verifies job/attempt/config hashes,
   checksums, and asset mappings before making a candidate index available.
3. Given disconnect, stale attempt, or incomplete files, the existing index remains
   active and the user can resume or retry the notebook workflow.
4. Website hosting and playback continue locally without a live Colab connection.

### US7 - Rehearse and recover the defense (P1)

As presenter, I launch the prepared demo, select known queries, and recover from faults.

**Independent test**: Follow demo-runbook.md with networking disabled after preparation.

1. Given prepared models and media, I can open the collection, perform arbitrary
   live searches, play results, and browse recorded evaluations offline.
2. Given live model unavailability, recorded reports remain usable and are clearly
   separate from live search; cached answers are never presented as fresh search.
3. Given startup diagnostics, missing SDK/runtime/data/model requirements are actionable.

### Edge Cases

Empty query/index; duplicate upload names; colliding legacy IDs; empty modalities;
unknown embedding provenance; 256-dimensional hash fallback against learned vectors;
interrupted uploads; corrupt JSON; browser-incompatible codecs; oversized media;
failed or stale Colab results; cancellation racing with completion; concurrent
index activation; source path relocation; missing checkpoints; zero evaluated queries;
metric denominators of zero; unreviewed labels; index too large for available RAM.

## Requirements

### Functional Requirements

| ID | Requirement | Stories |
| --- | --- | --- |
| FR-001 | Manage named collections and isolated assets; preserve original filenames separately from storage names. | US1, US4 |
| FR-002 | Preview and idempotently import existing artifacts and reports, with inventory and media-resolution diagnostics. | US1 |
| FR-003 | Browse paginated assets and chronological segments with actual text availability. | US1, US2 |
| FR-004 | Preserve legacy hybrid search behavior; allow topK, alpha, explicit index, and video scope. | US2 |
| FR-005 | Support compatible reranker selection, candidateK, and same-request baseline comparison. | US2 |
| FR-006 | Provide seekable video, interval selection, replay, and missing/unsupported-media states. | US2 |
| FR-007 | Stream uploads to storage and start explicit durable processing jobs. | US4 |
| FR-008 | Support progress, per-asset outcomes, retry, cancellation, recovery, and isolated publication. | US4, US6 |
| FR-009 | Import and execute Track A boundary hit rate, temporal IoU, and segmentation summaries. | US3, US5 |
| FR-010 | Import and execute Track B correctness, specificity, grounding, category, and low-information summaries. | US3, US5 |
| FR-011 | Import and execute Track C baseline/reranked Recall@K, MRR, and Mean Top-1 IoU. | US3, US5 |
| FR-012 | Show paired bootstrap confidence intervals, iteration count, seed, and estimate semantics. | US3, US5 |
| FR-013 | Inspect query-level expected spans, first hit, rank changes, and error categories with playback. | US3, US5 |
| FR-014 | Export original JSON, generated Markdown/CSV summaries, and integrated A/B/C reports with provenance. | US3, US5 |
| FR-015 | Validate/sanitize/version benchmarks; version boundary labels and caption rubric reviews. | US5 |
| FR-016 | Import, train, validate, activate, and identify reranker models with train/evaluation provenance. | US2, US5 |
| FR-017 | Run/compare segmentation and modality ablations; retain unavailable-modality and comparison-method status. | US5 |
| FR-018 | Export/import a validated Colab processing bundle using relative paths and immutable attempt identity. | US6 |
| FR-019 | Provide preflight, offline preparation, demo query selection, and recovery procedures. | US7 |
| FR-020 | Track artifact hashes, model/config identity, source run, and historical-versus-live status throughout. | All |

### Nonfunctional Requirements

- NFR-001: Target warm live search p95 <= 3 seconds over 20 measured queries on the
  prepared laptop corpus; record hardware, corpus size, and RAM. This is a target,
  not an existing benchmark. Cold model/index loading is reported separately.
- NFR-002: Target job-acceptance p95 <= 1 second excluding upload time; no request
  waits for ML completion. Poll progress every 2 seconds while visible.
- NFR-003: On supported local media, target seek within 1 second and displayed time
  within 0.5 seconds of the chosen start after seek completion.
- NFR-004: Offline demonstration works after an explicit preparation stage.
  No CDN fonts, cloud identity, or model download is required during presentation.
- NFR-005: Keep playback/search responsive during one processing job; default heavy
  job concurrency is one. Bound worker RAM empirically and show readiness.
- NFR-006: Resource IDs constrain filesystem access; uploads are bounded and validated.
  Default binding is loopback. Network exposure requires a separately reviewed profile.
- NFR-007: Keyboard operation, labeled controls, visible focus, sufficient contrast,
  and functional 1366x768, 1920x1080, and 390px layouts are required.
- NFR-008: Support SQL Server and SQLite with the same observable job/version behavior.

### Key Entities

Collection, VideoAsset, ProcessingRun, ProcessingAssetAttempt, SegmentSet, VideoSegment,
IndexSnapshot, Artifact, BenchmarkVersion, LabelSetVersion, CaptionReview,
RerankerVersion, EvaluationRun, EvaluationMetric, QueryOutcome, JobEvent, ColabHandoff.

## Success Criteria

- SC-001: US1 import is repeatable and preserves media/segment mappings after restart.
- SC-002: US2 matches legacy ranking for fixed fixtures and meets measured playback targets.
- SC-003: Every thesis capability has a traceability row, UI surface, and verification case.
- SC-004: US3 presents the stored canonical metrics faithfully without hardcoded dashboard values.
- SC-005: US4 succeeds for a valid clip and recovers from a failed/interrupted attempt.
- SC-006: US5 produces separately versioned experiment artifacts with comparable-run checks.
- SC-007: US6 completes one actual Colab round trip before being called verified.
- SC-008: A 10-12 minute defense rehearsal succeeds offline with an honest recorded-result fallback.

## Assumptions and Explicit Decisions

Confirmed: local website plus Colab processing. Proposed defaults: Blazor UI, English
UI text, dark charcoal theme from the customer brief, SQL Server engineering profile
and SQLite portable profile. No fixed delivery date or laptop performance guarantee
was supplied. Use measured rehearsal gates rather than inventing a schedule.

