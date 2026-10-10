# UI and Interaction Specification

Audience: one presenter operating a projected laptop screen, with a committee
examining retrieval evidence. Use a compact application shell and real video/media
as the primary visual content. The first screen is the active collection's search
workspace; an empty installation opens import/setup.

## Navigation

Persistent collection selector plus Search, Library, Jobs, Research, and Demo.
Research contains Results, Benchmarks/Labels, Models, and Experiments.
Global status shows local runtime readiness and active index version. Preserve query,
selected result, filters, and playback position when switching result comparisons.

## Screens

| Screen | Content and actions | Required states |
| --- | --- | --- |
| Search | Query, topK, alpha slider, baseline/reranked/compare segmented control, compatible model selector; ranked list and video. | No index, warming, searching, results, empty scope, dependency error. |
| Library | Asset table, import preview, upload progress, metadata, chronological segments, thumbnail strip. | Empty, queued upload, duplicate/collision, missing source, unsupported codec, ready. |
| Jobs | State, completed/failed/skipped counts, elapsed time, asset outcomes, logs, retry/cancel, candidate publication. | Queued, running, waiting for Colab, interrupted, partial, failed, canceled, succeeded. |
| Research Results | Run/input selectors; Track A/B/C tables; baseline/reranker comparison, CI plots, query outcome table, artifact export. | Recorded/live origin, incompatible comparison, missing artifact, heuristic labels, undefined metric. |
| Benchmarks/Labels | Raw/cleaned counts, exclusions, version lineage, review entries and media, score steppers 1-5, category menu, publish new version. | Read-only published version, editable draft, validation errors, stale edit. |
| Models/Experiments | Reranker registration/validation, training job, input split, activation, segmentation/modality variants, optional comparator status. | Compatible, unavailable checkpoint, unverified provenance, same-benchmark replay, running. |
| Colab Handoff | Bundle export/download, attempt identity, notebook link/instructions, result upload, verification receipt. | Waiting, transfer incomplete, stale attempt, partial, accepted, rejected. |
| Demo/Settings | Prepared query list, checklist, current provider/runtime readiness, historical report shortcuts, storage profiles. | Ready/degraded/missing prerequisites; no fake successful preflight. |

## Search Layout

Desktop: compact query toolbar above two unframed columns. Left: video with 16:9
aspect ratio, caption details, interval rail. Right: fixed-width result list with
thumbnail, asset name, start/end time, caption excerpt, and score. Comparison uses
two result tabs or parallel lists only when width permits. At 1366x768 keep controls
and first result visible; at narrow widths stack video and results with no overlap.

Default profile: topK=10, candidateK=200, hybridAlpha=0.6. Advanced parameters are
collapsed except alpha, which is a clearly labeled slider. Changes take effect on
Search, not every slider movement. Cancel stale requests so an old response cannot
replace the latest query.

Selecting a result updates the asset and waits for loadedmetadata before setting
currentTime. Preserve millisecond data even if labels use mm:ss. Highlight start/end,
support replay-segment and continue-video modes, and expose native volume/fullscreen.
Use the browser's supported decoding; show a meaningful error if playback fails.

Evidence tabs show Visual, Speech, and On-screen text. Empty modalities say
Unavailable for this run; populated imported text displays normally. These are
available evidence sources, not explanations of causal score contribution.

## Research Presentation

Show named metrics with actual K, sample/query counts, baseline/reranked values,
absolute deltas, and report identity. Charts supplement accessible tables.
Show the original raw artifact on demand. Never label the historical 896 count as
the present library count. Do not turn a ranking score into a percent confidence.

Query diagnostics show expected and retrieved spans and allow both to be played.
Show rank-improved, rank-worse, unchanged and top1-miss categories as computed by the
legacy error pack; do not redefine success categories in frontend code.

## Visual and Accessibility Rules

Follow the requested charcoal theme: background #0B0F17, neutral raised controls,
light text, cyan selection, amber warnings, green ready status, red failure.
Use color plus icon/text state, never color alone. Self-host a legible sans-serif
and monospace timestamps. Use familiar icon controls with tooltips/accessible names;
play/pause, retry, export and cancel should not depend on custom ambiguous symbols.

Use tables for inventories and metrics, tabs for tracks, switches for binary settings,
numeric inputs/steppers for K and rubric scores. Avoid marketing content and nested
cards. Keep headings compact, focus visible, labels readable on projection, and
status announcements accessible. Maintain stable player/thumbnail dimensions to
prevent layout shifts while loading.

