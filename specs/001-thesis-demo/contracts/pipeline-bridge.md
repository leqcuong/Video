# Python Bridge Contract v1

Status: new adapter to implement in web/bridge; legacy entrypoints remain existing.
Run legacy modules with working directory legacy-pipeline and its path on sys.path.
Use an explicitly configured interpreter, not whichever python happens to be on PATH.

## Existing Entry Points

| Operation | Existing entrypoint | Adapter responsibility |
| --- | --- | --- |
| Processing | python -m app.process --input DIR --output DIR --run-manifest FILE | Isolate input/output, set env settings, collect manifest, parse final summary. |
| Baseline search | app.index.search_index | Provide a machine protocol; preserve its ranking and row order. |
| Reranked search | app.evaluate_reranker._load_model and _rerank_query | Encapsulate these private functions behind one compatibility adapter and parity fixtures. |
| Track A | python -m app.evaluate_segmentation | Supply labels, segments-dir, variant-name and report/table paths. |
| Track B | python -m app.evaluate_caption_quality | Supply caption labels and matching segments-dir. |
| Track C baseline | python -m app.evaluate | Supply cleaned benchmark, index and explicit retrieval parameters. |
| Track C comparison | python -m app.evaluate_reranker | Supply model, benchmark, index, top-k/candidate-k/alpha/IoU. |
| Significance | python -m app.analyze_retrieval_significance | Read comparison report; bootstrap-iters=5000 and seed=42 for preset. |
| Error diagnostics | python -m app.build_error_analysis_pack | Preserve per-query diagnostics and category definitions. |
| Benchmarks | app.sanitize_benchmark, app.split_benchmark | Version outputs and record exclusion/split provenance. |
| Reranker training | python -m app.train_reranker | Separate training and evaluation inputs for new experiments. |
| Ablations/comparison | app.ablate, app.compare_models | Verify optional adapters; preserve native/proxy/skipped status. |
| Integrated/export | app.integrated_report, app.export_comparison_pack | Link compatible source reports and preserve raw outputs. |

Exact CLI flags are resolved from each module's parser during implementation.
Do not invent a legacy search CLI or launch process.py directly with broken imports.

## Machine Protocol

The .NET parent starts web/bridge/search_worker.py once and writes UTF-8 JSONL to stdin.
Only one outstanding request is required initially; queue with timeouts. stdout is
protocol-only; model/library logs are redirected to stderr and bounded on the parent.
An EOF/crash fails the active request and permits a controlled restart.

Request example:

~~~json
{
  "protocolVersion": 1,
  "requestId": "search-17",
  "operation": "search",
  "indexId": "registered-snapshot-id",
  "query": "a spacecraft launch",
  "topK": 10,
  "candidateK": 200,
  "hybridAlpha": 0.6,
  "mode": "compare",
  "modelId": "registered-reranker-id",
  "assetIds": []
}
~~~

The trusted parent supplies resolved file/model paths during load/register messages;
the browser never supplies those paths. Responses contain protocolVersion, requestId,
status, indexId, modelId, effectiveConfig, elapsedMs, baseline and/or reranked arrays.
Each result preserves asset_id, segment_id, start_sec, end_sec, caption,
transcript_text, ocr_text and score. .NET maps these to web resource IDs and URLs.
Never send embeddings or raw source_video paths to the browser.

A structured error contains code, message, retryable, and requestId. Codes include
MODEL_UNAVAILABLE, INDEX_INCOMPATIBLE, INDEX_NOT_LOADED, INVALID_REQUEST, and
EXECUTION_FAILED. A canceled request cannot later replace newer UI results.

Asset scope is applied to index rows before scoring. The filtered corpus changes
BM25 statistics and normalization; compare both retrieval modes on exactly that
scope. An empty scope result returns no matches. Scores are query/scope dependent.

## Processing Wrapper

New web/bridge/job_runner.py receives a versioned request JSON file. It emits
job_started, asset_completed, asset_failed, validation_started, and job_finished.
The legacy manifest only guarantees per-asset completion information. Show elapsed
time and completed/total assets; do not fabricate per-stage percentage progress.
Add finer instrumentation later only when actual stage boundaries are available.

ProcessStartInfo uses ArgumentList, UseShellExecute=false, redirected streams,
and a validated working directory. Set settings per child process, not global
environment shared by concurrent web requests. Await exit with cancellation and
terminate the child process tree on deadline/cancel; await and drain its exit.

The legacy CLI may exit zero despite failed assets. Classify success using its
videos_failed/videos_succeeded fields, JSONL manifest, and validated artifacts.
The videos_processed field counts discovered videos and must not be used as a
successful count. A run with no valid rows cannot replace a working index.

## Artifact Contract

Per asset: segments.json array, segments.csv, asset_metadata.json; optional
asr_segments.json. Index: JSON array at index/combined_index.json.

Required segment fields: asset_id, segment_id, start_sec, end_sec, caption.
Nullable fields: transcript_text, ocr_text, summary. Index rows additionally have
search_text, embedding, source_video. Preserve legacy spelling on disk.

Validate finite timestamps, end>start, row uniqueness, consistent asset references,
caption presence, vector numeric finiteness/dimension, and index-to-segment
correspondence. Reject unknown or mismatched embedding spaces for live semantic use.
Imported artifacts with unknown provenance can be browsed while semantic search
awaits model verification; dimension alone is not evidence of model identity.

New snapshot metadata includes schemaVersion, sourceCodeRevision/contentHash,
sourceFileHashes, settings, captionBackend, embeddingModel/revision/dimension,
actual ASR/OCR status, generatedUtc, and row/asset counts. Preserve old JSON formats
by adding a separate provenance.json, not renaming legacy fields.

Thumbnails are derived, optional assets keyed by immutable segment-set ID. The
legacy caption function samples frames in memory and does not guarantee JPEG files.

## Publication and Parity

New processing runs use isolated staging and filenames derived from UUIDs. Map
legacy IDs back to web assets in a manifest. Import older IDs unchanged within a
collection. Do not rewrite the canonical index merely to relocate media.

Before activation, construct complete IndexMember membership, validate all rows,
then commit snapshot metadata and active pointer. Keep old versions for comparison.
Resume requires matching input/config/model hashes, not file existence alone.

Parity checks use identical row order, model bytes, query strings, topK, candidateK,
alpha and scope. Compare IDs/order exactly and float scores within 1e-6 on a
deterministic fixed environment; document any device-induced differences. No
optimization of BM25/normalization is accepted without these checks.

