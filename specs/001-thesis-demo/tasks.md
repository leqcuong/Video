# Tasks: Local Thesis Defense Web Demo

Input: spec.md, plan.md, research.md, data-model.md, contracts, ux-spec.md and
verification-plan.md. All tasks below are unimplemented. The documentation task
does not mark application work complete.

Format: Tnnn [P if independently parallel] [USn] action and target path.
All source paths are relative to repository root and describe future files.
Tests are included because the customer requested them. Each story's verification
cases must pass before its task group is complete.

## Phase 1: Setup and Foundation (M0)

- [ ] T001 Create web/ThesisDemo.sln and the four projects under web/src/ThesisDemo.{Domain,Application,Infrastructure,Api}; pin .NET 10 in web/global.json and packages in web/Directory.Packages.props. Confirm x64 Python choice; record package locks.
- [ ] T002 [P] Create web/tests/ThesisDemo.ArchitectureTests/DependencyRulesTests.cs and test project scaffolds; enforce Domain/Application boundaries and forbidden dependencies.
- [ ] T003 Implement provider selection, EF mappings and separate migrations under web/src/ThesisDemo.Infrastructure/Persistence/{SqlServer,Sqlite}; verify both concurrency-token strategies.
- [ ] T004 [P] Add web/scripts/check-environment.ps1 and check-environment.sh for SDK, Python path/bitness, FFmpeg, models, device, disk and source-root readiness.
- [ ] T005 Implement collection/asset/run/snapshot entities and state transitions in web/src/ThesisDemo.Domain/{Library,Processing,Search}; add focused invariant tests in web/tests/ThesisDemo.UnitTests.
- [ ] T006 Implement immutable Artifact/Provenance models and hash/path validation in web/src/ThesisDemo.Infrastructure/Storage and Application/Artifacts.
- [ ] T007 Implement durable job repository, bounded Channel<Guid>, dispatcher and scoped BackgroundService in web/src/ThesisDemo.Infrastructure/Jobs; include claim tokens and recovery behavior.
- [ ] T008 Implement API v1 routing, DTO validation, ProblemDetails, trace IDs, loopback config and antiforgery bootstrap in web/src/ThesisDemo.Api/{Controllers,Middleware}; configure Blazor per-operation handler scopes.

Dependencies: T001 first; T002/T004 may proceed after solution paths exist.
T005 precedes final T003 mappings; T006/T007 follow entity/persistence foundation.
Checkpoint: compile, architecture checks, migrations, diagnostic output.

## Phase 2: US1 - Import and Browse Prepared Corpus (P1)

- [ ] T009 [US1] Implement collection handlers/controllers in web/src/ThesisDemo.Application/Collections and web/src/ThesisDemo.Api/Controllers/CollectionsController.cs.
- [ ] T010 [US1] Implement local media storage and legacy-ID/source-root mapping in web/src/ThesisDemo.Infrastructure/Storage/LocalMediaStore.cs.
- [ ] T011 [US1] Implement preview/commit/idempotent import and snapshot provenance in web/src/ThesisDemo.Infrastructure/Legacy/LegacyCorpusImporter.cs; use raw artifact hashes and actual counts.
- [ ] T012 [US1] Build import preview, collection/library tables and diagnostics in web/src/ThesisDemo.Api/Components/Pages/Library; add V01/V02 fixtures in web/tests/ThesisDemo.IntegrationTests/Imports.

Dependencies: foundation; T009/T010 before T011, importer contract before T012.
Independent verification: V01/V02 on a small corpus, then current full corpus import.

## Phase 3: US2 - Search, Comparison and Playback (P1)

- [ ] T013 [US2] Implement web/bridge/search_worker.py and Infrastructure/Legacy/PythonSearchProcess.cs for versioned JSONL, warm models/index, bounds, deadlines and controlled restart.
- [ ] T014 [US2] Implement web/src/ThesisDemo.Api/Controllers/MediaController.cs and SegmentsController.cs with ranges, HEAD, registered thumbnails and version-pinned segment browsing.
- [ ] T015 [US2] Build reusable VideoPlayer.razor, SegmentList.razor and wwwroot/js/video-player.js under ThesisDemo.Api for fractional seek, interval highlight and missing/unsupported media.
- [ ] T016 [US2] Implement query validation, scope, selected snapshot and Python ID/URL mapping in web/src/ThesisDemo.Application/Search and Api/Controllers/SearchController.cs.
- [ ] T017 [US2] Build web/src/ThesisDemo.Api/Components/Pages/Search.razor with alpha/K controls, request cancellation, states, result list and responsive layout.
- [ ] T018 [US2] Implement model registry/import validation and legacy private-function compatibility wrapper in web/bridge/reranker_adapter.py and Infrastructure/Legacy/RerankerRegistry.cs.
- [ ] T019 [US2] Add baseline/reranked comparison and model selection in web/src/ThesisDemo.Api/Components/Search/ComparisonView.razor; preserve source intervals and score semantics.

Dependencies: US1 fixtures available; T013 before T016; T014 before browser playback;
T018 before T019. Independent verification: V03/V05/V06.

## Phase 4: US3 - Recorded Research Evidence (P1, M1)

- [ ] T020 [US3] Implement registered report loaders and historical provenance in web/src/ThesisDemo.Infrastructure/Research/ReportImporter.cs; handle thesis/current count differences and unknown metadata.
- [ ] T021 [US3] Build Track A/B/C summaries and Dapper report projections in web/src/ThesisDemo.Api/Components/Pages/Research and Infrastructure/Research/EvaluationReadRepository.cs.
- [ ] T022 [US3] Add paginated per-query error/rank views with expected/retrieved playback in web/src/ThesisDemo.Api/Components/Research/QueryDiagnostics.razor.
- [ ] T023 [US3] Add confidence-interval table/chart with finite-bootstrap labels in web/src/ThesisDemo.Api/Components/Research/SignificanceView.razor.
- [ ] T024 [US3] Implement controlled artifact download and JSON/CSV/Markdown summaries in web/src/ThesisDemo.Api/Controllers/ArtifactsController.cs and Application/Research/Exports.
- [ ] T025 [US1,US2,US3] Add and run import/search/media/report parity checks in web/tests/ThesisDemo.IntegrationTests and web/tests/bridge; measure warm full-corpus search before closing M1.

Dependencies: T020 before T021-T024; each view can use recorded fixtures.
Independent verification: recorded-report portions of V09-V14 plus provenance.

## Phase 5: US4 - Live Processing and Recovery (P1, M2)

- [ ] T026 [US4] Implement streamed upload validation/controller and batch UI in web/src/ThesisDemo.Api/Controllers/AssetsController.cs and Components/Library/UploadPanel.razor.
- [ ] T027 [US4] Implement processing command/config snapshots and registered model choices in web/src/ThesisDemo.Application/Processing.
- [ ] T028 [US4] Implement web/bridge/job_runner.py and Infrastructure/Legacy/PythonPipelineBridge.cs using isolated staging, real manifest progress and per-asset outcomes.
- [ ] T029 [US4] Implement JobsController, Jobs.razor and lifecycle tests under web/src/ThesisDemo.Api and web/tests/ThesisDemo.IntegrationTests/Jobs for cancel/retry/timeout/restart.
- [ ] T030 [US4] Implement snapshot composition/validation/activation in web/src/ThesisDemo.Infrastructure/Search/SnapshotPublisher.cs; add V07/V08/V20 partial-publication and race checks.

Dependencies: US1 and durable jobs; T027 before T028; T030 after validated output
contract. Checkpoint: real short clip, failed asset, recovery with prior index intact.

## Phase 6: US5 - Full Research Workflows (P2, Required, M3)

- [ ] T031 [US5] Implement benchmark import/sanitize/split handlers and UI in web/src/ThesisDemo.Application/Research/Benchmarks and Api/Components/Pages/Benchmarks.razor; record overlap diagnostics.
- [ ] T032 [US5] Wrap Track A runner in web/bridge/evaluations.py and Application/Research/EvaluateSegmentation.cs with boundary tolerance and snapshot segment sets.
- [ ] T033 [US5] Implement boundary label draft editing/version publication in web/src/ThesisDemo.Application/Research/Labels and Api/Components/Research/BoundaryReview.razor.
- [ ] T034 [US5] Implement caption rubric review/sample generation and label provenance in web/src/ThesisDemo.Api/Components/Research/CaptionReview.razor and Application/Research/Labels.
- [ ] T035 [US5] Add Track B execution and category summaries in web/bridge/evaluations.py and Application/Research/EvaluateCaptions.cs.
- [ ] T036 [US5] Add baseline/reranked Track C execution with exact benchmark/model/index config in web/bridge/evaluations.py and Application/Research/EvaluateRetrieval.cs.
- [ ] T037 [US5] Add paired bootstrap execution and seed/iteration provenance in web/bridge/evaluations.py and Application/Research/EvaluateSignificance.cs.
- [ ] T038 [US5] Add error-pack generation preserving legacy categories in web/bridge/evaluations.py and Application/Research/BuildErrorPack.cs.
- [ ] T039 [US5] Add compatible integrated reporting and comparison export jobs in web/bridge/evaluations.py and Application/Research/BuildIntegratedReport.cs.
- [ ] T040 [US5] Add reranker training against selected training version in web/bridge/training.py and Application/Research/TrainReranker.cs.
- [ ] T041 [US5] Build model provenance, validation, activation and training controls in web/src/ThesisDemo.Api/Components/Pages/Models.razor.
- [ ] T042 [US5] Add isolated segmentation/modality ablation jobs and truthful stub status in web/bridge/experiments.py and Api/Components/Pages/Experiments.razor.
- [ ] T043 [US5] Add optional CLIP4Clip/X-CLIP/MMT capability checks and comparisons in web/bridge/experiments.py; label native/proxy/skipped/incomparable results and verify V09-V17.

Dependencies: report views and processing bridge; benchmark versions before
training/evaluation; label publication before new A/B runs; C before significance/
errors; A/B/C before integrated report. Multiple edits to evaluations.py must be
sequential. Optional model execution is conditional on assets, but status/report
coverage is required. Independent verification uses small research fixtures.

## Phase 7: US6 - Interactive Colab Handoff (P2, Required, M4)

- [ ] T044 [US6] Implement job-bundle/manifest export in web/src/ThesisDemo.Infrastructure/Colab/BundleExporter.cs, with relative paths and input/config hashes.
- [ ] T045 [US6] Create web/notebooks/colab-runner.ipynb with pinned setup, device verification, staged names, interactive processing and complete result manifest.
- [ ] T046 [US6] Implement result-bundle verification, attempt reconciliation and receipt UI in web/src/ThesisDemo.Infrastructure/Colab/BundleImporter.cs and Api/Components/Jobs/ColabHandoff.razor.
- [ ] T047 [US6] Verify invalid/stale/partial/repeated bundle cases and one real GPU round trip; record evidence under web/artifacts/verification/colab and fixtures under web/tests/ThesisDemo.IntegrationTests/Colab.

Dependencies: processing and publication contracts; T044 before T045/T046; all before
T047. Do not mark real Colab verification complete using a mocked transfer alone.

## Phase 8: US7 - Packaging and Defense Readiness (P1, Release Gate)

- [ ] T048 [US7] Implement capabilities/preflight, demo query selection and offline asset readiness in web/src/ThesisDemo.Application/Demo and Api/Components/Pages/Demo.razor.
- [ ] T049 [US7] Implement web/scripts/prepare-demo.ps1, start-demo.ps1 and Bash equivalents; provide explicit database profiles, free-port selection and safe process shutdown.
- [ ] T050 [All] Complete web/tests/ThesisDemo.BrowserTests and provider integration coverage for V01-V20; measure actual laptop memory/latency and responsive layout/accessibility.
- [ ] T051 [All] Update .github/workflows/ci.yml for the new web solution/bridge and web configuration examples, preserving unrelated legacy checks; document reproducible setup.
- [ ] T052 [US7] Perform offline defense rehearsal using demo-runbook.md; record actual results and unresolved limitations in web/artifacts/verification/release.md; update web/README.md with the real start command and URL behavior.

Dependencies: T048 can start after M1; final T050-T052 require all mandatory stories.
No successful rehearsal substitutes for missing US5/US6 functionality.

## Parallel Work and Review Ownership

BA review: requirements, source mapping, acceptance scenarios and metric wording.
Technical lead review: boundaries, job recovery, Python parity, snapshot semantics.
UI review: player, result states, projection readability and keyboard navigation.
QA review: verification evidence and unresolved claims.

After foundation, media endpoints/components and recorded-report import can proceed
in parallel when they touch separate files. Freeze shared DTO/contracts first.
Keep edits to a common bridge module sequential. Review each milestone as a usable
vertical slice before expanding the next one.

