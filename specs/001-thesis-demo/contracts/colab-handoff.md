# Colab Handoff Contract v1

Confirmed deployment: .NET UI/database/media remain on the laptop. A person operates
the Colab notebook to run selected jobs. Manual ZIP transfer is the first supported
transport. Drive may be used to transfer bundles, but it is not treated as a
transactional filesystem or an always-on dispatch queue.

## Bundle Structure

~~~text
job-{handoffId}.zip
  request.json
  manifest.json
  inputs/{assetId}/source.ext
  config/pipeline.json
  adapter/                    # pinned bridge wrapper or verified source reference
result-{handoffId}.zip
  result.json
  manifest.json
  outputs/{legacyAssetId}/segments.json
  outputs/{legacyAssetId}/segments.csv
  outputs/{legacyAssetId}/asset_metadata.json
  outputs/{legacyAssetId}/asr_segments.json
  outputs/index/combined_index.json
  outputs/provenance.json
  logs/process_manifest.jsonl
~~~

Optional files are listed explicitly. Model checkpoints are prepared through the
notebook's pinned setup; bundles record expected identities, not account credentials.
Do not package the 41+ GB legacy archive automatically.

## request.json

~~~json
{
  "schemaVersion": 1,
  "handoffId": "uuid",
  "runId": "uuid",
  "attemptToken": "uuid",
  "operation": "process",
  "createdUtc": "2026-10-10T15:00:00Z",
  "configSha256": "64-lowercase-hex-characters",
  "legacyRevision": "recorded-content-or-git-identity",
  "assets": [
    {
      "assetId": "uuid",
      "legacyAssetId": "stable-staged-stem",
      "path": "inputs/uuid/source.mp4",
      "sha256": "64-lowercase-hex-characters"
    }
  ],
  "pipelineConfigPath": "config/pipeline.json"
}
~~~

These placeholders illustrate field semantics, not a runnable job. manifest.json
enumerates every payload relative path, SHA-256, and byte length. Reject absolute
paths, traversal, duplicate paths, symlinks, unsupported versions, and configured
expanded-size limits when importing ZIP files.

## Notebook Procedure

1. Upload/select the bundle, mount Drive only if needed, and verify its manifest.
2. Unpack into Colab runtime scratch storage; copy assets to unique staged stems
   matching legacyAssetId before calling process_directory.
3. Install pinned dependencies and load compatible models. Verify the selected
   device actually runs ML on GPU where supported; current caption code does not
   explicitly select a GPU device. Device wiring is part of the bridge work.
4. Run the selected processing job interactively with visible progress.
5. Write result.json and a complete checksum manifest after processing ends.
6. Package completed outputs and transfer the result back to the laptop.

Checkpoints remain durable only if exported/saved to persistent storage. Colab
runtime files and GPU availability are temporary. A stopped notebook does not
imply that a laptop job failed; it remains WaitingExternal until imported/canceled.

## Result and Import Rules

result.json includes schemaVersion, handoffId, runId, attemptToken, requestManifestHash,
configSha256, startedUtc, finishedUtc, status (succeeded/partial/failed), per-asset
outcomes, actual device/backend/model provenance, and error summaries.

Import matches the pending attempt and request hash, verifies every payload, maps
source_video back to local assets, and validates the same pipeline contract used
locally. Full index/embeddings/provenance are needed for search activation;
segments.json alone is insufficient. Missing requested assets produce Partial.

Repeated identical imports return the existing receipt. A different result for
an already completed attempt is a conflict. Late output from a canceled/stale
attempt is retained for inspection but cannot publish. Copy bundle bytes locally
and verify them before DB updates; do not read partially synced Drive outputs.

## Acceptance

One real short-clip export/Colab execution/import must be recorded before claiming
GPU handoff works. An offline simulated round trip verifies the contract, not Colab
availability. Website and already imported search remain usable during all steps.

