# Data Model

All IDs are UUIDs unless a legacy ID is named. Persist UTC DateTime values and
double seconds; JSON timestamps use ISO 8601 UTC. Legacy segment IDs are strings
such as seg_0001 and are only unique within an asset's segment set.

## Entities

| Entity | Main fields | Constraints |
| --- | --- | --- |
| Collection | Id, Name, ActiveIndexId, CreatedUtc, Version | Name 1-120 chars; active index must belong to collection. |
| VideoAsset | Id, CollectionId, LegacyAssetId, OriginalFileName, MediaArtifactId, Hash, DurationSec, Fps, Width, Height, State, Version | Unique CollectionId+LegacyAssetId; metadata validated; original name is not a storage path. |
| ProcessingRun | Id, CollectionId, Mode, State, ConfigJson, ConfigHash, InputSnapshotHash, Created/Started/EndedUtc, Version | Immutable inputs once queued; modes local/colab. |
| ProcessingAssetAttempt | Id, RunId, AssetId, AttemptToken, State, OutputRoot, ErrorCode | Unique run/asset/attempt; no outputs from stale token may publish. |
| SegmentSet | Id, AssetId, RunId, AttemptId, ConfigHash, SourceHash, CreatedUtc | Immutable after validation. |
| VideoSegment | Id, SegmentSetId, LegacySegmentId, StartSec, EndSec, Caption, TranscriptText, OcrText, Summary, ThumbnailArtifactId | Unique set+legacy segment ID; 0<=start<end; caption required by legacy contract. |
| IndexSnapshot | Id, CollectionId, State, ArtifactId, EmbeddingModel, Revision, Dimension, RowCount, ConfigHash, CreatedUtc | Immutable Ready snapshot; no unknown/mixed model for live semantic search. |
| IndexMember | IndexId, AssetId, SegmentSetId | Unique index+asset; set belongs to that asset. |
| Artifact | Id, Kind, RelativePath, Sha256, ByteLength, ContentType, ProvenanceJson | Paths resolved beneath registered roots; hash immutable. |
| BenchmarkVersion | Id, Name, RawArtifactId, CleanArtifactId, SanitizeReportId, QueryCount, ParentId, SplitRole, SplitSeed | Versions are immutable; raw and cleaned counts distinct. |
| LabelSetVersion | Id, Kind, ArtifactId, ParentId, Provenance, ReviewedCount, TotalCount | Kind boundary/caption; provenance heuristic/human/mixed/unknown. |
| CaptionReview | Id, LabelSetDraftId, AssetId, LegacySegmentId, Reference, Correctness, Specificity, Grounding, Category, ReviewerNote, Version | Rubric integer 1-5; publishing freezes the label-set version. |
| RerankerVersion | Id, ArtifactId, ModelType, FeatureSchemaHash, TrainingBenchmarkId, IndexId, ConfigJson, EvaluationRole | model_type logreg_numpy for current adapter; arrays/features validated. |
| EvaluationRun | Id, Kind, State, IndexId, BenchmarkId, LabelSetId, ModelId, ConfigJson, InputHashes, ResultOrigin | Origin imported/live; kind segmentation/caption/retrieval/significance/error/ablation/comparison/integrated/training. |
| EvaluationMetric | EvaluationId, Method, Name, Value, Denominator, Unit | Null for undefined values; raw report retained; no fabricated zero. |
| QueryOutcome | EvaluationId, QueryOrdinal, Query, ExpectedJson, BaselineJson, RerankedJson, RankShift, Category | Paginated; ordinal distinguishes repeated query text. |
| JobEvent | RunId, Sequence, TimeUtc, Level, Stage, AssetId, Message, Completed, Total | Unique run+sequence; monotonic cursor; totals nullable. |
| ColabHandoff | Id, RunId, AttemptToken, BundleHash, ConfigHash, State, ResultArtifactId | Imports idempotent by attempt+result hash. |
| DemoQuery | Id, CollectionId, Text, Purpose, ExpectedAssetId, SortOrder | Used to organize examples, never substitute a live response. |

Evaluation jobs and processing jobs share a durable job dispatch record/interface.
Training may return a RerankerVersion; an evaluation cannot silently activate it.

## States and Invariants

~~~mermaid
stateDiagram-v2
  [*] --> Queued
  Queued --> Running
  Queued --> WaitingExternal
  Queued --> Canceled
  WaitingExternal --> Validating
  WaitingExternal --> Canceled
  Running --> Validating
  Running --> CancelRequested
  Running --> Failed
  Running --> Interrupted
  CancelRequested --> Canceled
  Validating --> Succeeded
  Validating --> Partial
  Validating --> Failed
  Validating --> Canceled
~~~

A token/concurrency check decides the completion-versus-cancellation race.
Interrupted, Failed, Partial, and Canceled attempts never resume in place; retry
creates a linked attempt. Successfully validated assets from a partial run may be
reused by a later attempt or published explicitly with the incomplete scope disclosed.

Index state: Building -> Validating -> Ready or Rejected. Activation is a collection
pointer, not mutation of the index bytes. No state transition writes to an imported
historical artifact.

Asset state tracks ingestion/processing readiness, but the active snapshot determines
whether a version is searchable. Reprocessing a ready asset must not remove its old
searchable segments. MissingMedia is separate availability metadata.

## Concurrency and Provider Differences

SQL Server maps Version to a database-generated rowversion. SQLite uses an
application-managed GUID token regenerated on mutation and checked by EF.
The API treats both as an opaque ETag. A stale If-Match returns 412, not success.
Do not attempt to use SQL rowversion migrations unchanged on SQLite.

Maintain provider-specific migration assemblies/directories under Infrastructure.
Store normalized columns for frequently filtered data; JSON strings retain raw
details but avoid relying on vendor-specific JSON queries in shared code.
Dapper SQL differences are isolated in a provider dialect for report queries.

## Indexes and Transactions

Required indexes: asset collection/state; run state/created time; segment set/start
time; event run/sequence; evaluation input identity; query outcome evaluation/ordinal;
artifact hash/kind; unique IndexMember index/asset.

Persist a queued job and its idempotency record in one transaction. Database commits
cannot atomically commit external files: first validate immutable files, then commit
their references and active pointer. Unreferenced staged artifacts are recoverable;
cleanup is explicit and never deletes pinned thesis evidence.

Collection archive hides it from normal navigation. Destructive purge is a later
explicit operation; no automatic cascade removes imported source videos.

