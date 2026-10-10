# HTTP API Contract v1

Base: /api/v1. Single local operator; bind loopback by default. JSON uses camelCase.
Timestamps are UTC ISO 8601; timeline values are finite double seconds. Resource
IDs are UUIDs. Raw filesystem paths and embedding vectors never appear in browser
responses. This is the normative design contract; implementation must generate
matching OpenAPI and verify it before release.

## Conventions

Collections/resources return an ETag when mutable. PATCH and activation require
If-Match; missing precondition returns 428, stale version 412. Job creation accepts
Idempotency-Key: repeated key plus identical payload returns the same job; same
key with a changed payload returns 409.

Long operations return 202 with Location: /api/v1/jobs/{id} and a Job object.
File upload returns 201 after bytes are stored; it does not start processing
automatically. Upload time is excluded from the job-acceptance latency target.

Lists use page (>=1), pageSize (1-100, default 25) and documented sort values.
Responses: {items:[],page:1,pageSize:25,total:0}. Segments default startSec ascending;
assets originalFileName ascending; jobs createdUtc descending. Poll events using
afterSequence (>=0), limit (1-500). Stop polling hidden/terminal views.

## Endpoints

| Method and path | Request | Successful response |
| --- | --- | --- |
| GET /health | None | 200 host status. |
| GET /capabilities | None | 200 provider, Python/model readiness, modality/backend status, import/search/processing availability. |
| GET, POST /collections | POST {name} | 200 page / 201 Collection. |
| GET, PATCH /collections/{id} | PATCH {name,archived} + If-Match | 200 Collection. |
| POST /collections/{id}/imports | {registeredSourceId,action:"preview" or "commit",previewId?} | 202 Import job. |
| GET /collections/{id}/assets | page, pageSize, state, name | 200 Asset page. |
| POST /collections/{id}/assets | multipart file, one file per request | 201 Asset after storage/probe; UI batches requests. |
| GET /assets/{id} | None | 200 Asset with metadata, availability, active segment set. |
| GET /assets/{id}/segments | indexId optional, pagination | 200 Segment page pinned to selected snapshot. |
| GET, HEAD /assets/{id}/media | Optional Range / If-Range | 200 or 206 media; 416 invalid/unsatisfiable range. |
| GET /segments/{id}/thumbnail | None | 200 image or 404 thumbnail unavailable. |
| POST /collections/{id}/processing-runs | ProcessingRequest | 202 Job. |
| GET /collections/{id}/indexes | Pagination | 200 IndexSnapshot page. |
| PUT /collections/{id}/active-index | {indexId} + collection If-Match | 200 Collection with active pointer. |
| POST /collections/{id}/search | SearchRequest | 200 SearchResponse. |
| GET /jobs | collectionId, kind, state, pagination | 200 Job page. |
| GET /jobs/{id} | None | 200 Job with counts and outcomes. |
| GET /jobs/{id}/events | afterSequence, limit | 200 {items,nextSequence,isTerminal}. |
| POST /jobs/{id}/cancel | {reason?} | 202 cancellation requested or 200 already canceled; 409 incompatible terminal state. |
| POST /jobs/{id}/retry | {failedOnly:true,resume:true} | 202 new linked Job/attempt. |
| GET, POST /benchmarks | POST multipart JSON + name | 200 page / 201 version registration. |
| POST /benchmarks/{id}/sanitize | {profileId} | 202 new-version job. |
| POST /benchmarks/{id}/split | {trainRatio,seed} | 202 train/evaluation version job. |
| GET, POST /label-sets | POST multipart JSON + kind/provenance | 200 page / 201 LabelSetVersion. |
| POST /label-sets/{id}/drafts | None | 201 editable draft derived from immutable version. |
| GET /label-sets/{id}/entries | Pagination | 200 review entry page. |
| PUT /label-sets/{id}/entries/{entryId} | BoundaryEdit or CaptionEdit + If-Match | 200 updated draft entry. |
| POST /label-sets/{id}/publish | {reviewerNote} + If-Match | 201 immutable LabelSetVersion. |
| GET, POST /collections/{id}/rerankers | POST multipart model + provenance | 200 page / 201 validated RerankerVersion. |
| POST /collections/{id}/training-runs | {indexId,trainingBenchmarkId,config} | 202 training Job. |
| PUT /collections/{id}/active-reranker | {modelId} + If-Match | 200 selection; fails if incompatible. |
| POST /collections/{id}/evaluation-runs | EvaluationRequest | 202 Job linked to EvaluationRun. |
| GET /collections/{id}/evaluation-runs | kind, origin, pagination | 200 EvaluationRun page. |
| GET /evaluation-runs/{id} | None | 200 metadata, metrics, artifacts, comparability status. |
| GET /evaluation-runs/{id}/queries | outcome, pagination | 200 QueryOutcome page. |
| POST /evaluation-runs/{id}/derived-runs | {kind:"significance"/"errors"/"integrated",sourceRunIds,config} | 202 derived job. |
| GET /artifacts/{id}/download | None | 200 registered JSON/CSV/Markdown/ZIP with safe filename. |
| POST /jobs/{id}/colab-export | None | 202 export job producing bundle artifact. |
| POST /jobs/{id}/colab-import | multipart result ZIP | 202 verification/import job. |
| GET, POST /collections/{id}/demo-queries | POST {text,purpose,expectedAssetId?,sortOrder} | 200 list / 201 DemoQuery. |

Sources are registered through trusted local configuration/CLI; browser import
requests select opaque registeredSourceId. No endpoint accepts arbitrary Python,
shell arguments, raw model download URLs, or server paths.

## Request Shapes and Validation

ProcessingRequest:
{assetIds:[uuid], mode:"local"|"colab", presetId?, settings?, publishPolicy:"manual"|"onSuccess"}.
assetIds must be nonempty and belong to the collection. Settings allow sceneThreshold
(>0), minSceneLenFrames (>=1), fixedWindowSec (>0), forceFixedWindow, requestedAsr,
requestedOcr, captionModelId and embeddingModelId selected from registered capabilities.
Thesis defaults: threshold 27, minimum 15 frames, window 10 seconds. New snapshot
cannot mix different embedding models. Partial runs always require manual publication.

SearchRequest:
{query, indexId?, topK:10, candidateK:200, hybridAlpha:0.6,
 mode:"baseline"|"reranked"|"compare", modelId?, assetIds:[]}.
query trimmed length 1-2000; topK 1-100; candidateK topK-1000; alpha 0-1.
modelId must be compatible for reranked/compare. Empty assetIds means whole snapshot.
A nonempty filter must be a subset of snapshot assets. No active index returns 409
INDEX_NOT_READY; missing learned model returns 503 MODEL_UNAVAILABLE.

EvaluationRequest:
{kind, indexId, benchmarkId?, labelSetId?, modelId?, presetId?, config}.
kind segmentation/caption/retrieval/ablation/comparison.
A requires boundary labels and snapshot segment sets; B requires caption labels;
C requires benchmark and optionally a reranker. Config includes topK, candidateK,
hybridAlpha, iouThreshold (0-1), toleranceSec (>0), variants, and comparatorIds as
appropriate. Reject irrelevant/unknown keys. Use explicitly selected model IDs.
Advanced comparators are allowed only when preflight lists them as supported.

CaptionEdit:
{reference,correctness,specificity,grounding,category,reviewerNote}, integer scores 1-5.
BoundaryEdit:
{assetId,eventId,startSec,endSec,note}, start>=0, end>start, media-bound validation.
A draft revision token protects concurrent changes.

## Response Shapes

Collection: id, name, archived, activeIndexId, activeRerankerId, assetCount, createdUtc.
Asset: id, collectionId, originalFileName, legacyAssetId, state, mediaAvailability,
durationSec, fps, width, height, processingRunId, activeSegmentSetId.
Segment: id, assetId, segmentSetId, legacySegmentId, startSec, endSec, caption,
transcriptText, ocrText, modalityAvailability, thumbnailUrl, mediaUrl.

SearchResponse:
requestId, indexId, modelId, effectiveConfig, elapsedMs, warnings,
baselineResults and/or rerankedResults.
Result: rank, segmentId, assetId, startSec, endSec, caption, transcriptText, ocrText,
score, scoreKind ("hybrid" or "reranker"), mediaUrl, thumbnailUrl, baselineRank?.
Only render evidence sources present in the data; there is no modality-attribution
probability in the current retrieval contract.

Job: id, kind, state, mode, collectionId, attempt, createdUtc, startedUtc, endedUtc,
completedAssets, totalAssets, failedAssets, skippedAssets, stage, progressPercent?,
eventsUrl, resultIds, error?.
progressPercent is null when no trustworthy denominator exists.

EvaluationRun: id, kind, origin, state, inputIds/hashes, effectiveConfig,
metrics:[{method,name,value,denominator,unit}], labelProvenance, validationWarnings,
artifactIds, createdUtc, completedUtc.
Raw reports are the source of truth; projection tables support filtering/export.

## Error and Media Semantics

All errors use application/problem+json with type, title, status, detail, instance,
code and traceId; validation errors add fieldErrors. Never include stack traces or
private paths in browser-facing detail. Use 400 malformed request, 404 scoped ID not
found, 409 incompatible state, 413 upload too large, 415 unsupported type, 422 invalid
artifact semantics, 503 dependency unavailable, and 504 bounded execution timeout.

Media route uses framework range processing: full GET 200, satisfiable byte range
206 with Content-Range, length and Accept-Ranges, unsatisfiable range 416. HEAD sends
headers without body. Do not promise every media request returns 206. Preserve
ETag/Last-Modified for If-Range behavior. Unsupported codec gets a visible playback
state; a separately generated playback proxy may be offered without altering originals.

Host allows only its configured local origin. Protect browser mutation endpoints
with antiforgery tokens and origin checks, including multipart requests. A local
curl/automation client obtains a session/token through the documented host bootstrap.
No accounts are needed for this single-operator profile.

