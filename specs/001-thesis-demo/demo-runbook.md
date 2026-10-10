# Thesis Defense Demo Runbook

Target: 10-12 minute guided demo, adjusted to the presentation slot.
This is a rehearsal plan, not evidence that the application is already ready.

## Before Defense Day

Prepare the laptop's dependencies/models, the complete index, and a small set of
verified playable examples. Import historical reports and warm live search. Select
three example queries: a clear hit, a reranker rank change, and a meaningful failure.
Verify them on actual media; do not invent success examples from captions alone.
Prepare a short uploaded clip for processing and a previously validated Colab
result bundle in case remote compute is unavailable.

Record corpus/index/model hashes, current counts, historical run identity, hardware,
query timing and memory use. Rehearse without internet and with projection at
1366x768. Keep original reports and media backed up; preserve the current active
index before experimenting.

## Presentation Sequence

| Time | Action | Evidence to explain |
| --- | --- | --- |
| 0:00-1:00 | Open collection and readiness | Current 895-asset inventory versus historical paper context of 896. |
| 1:00-3:00 | Run natural-language search and play a result | Segment caption, fractional timestamps, timeline localization. |
| 3:00-4:30 | Compare baseline and reranker | Same query/index, candidateK=200, topK=10, alpha=0.6, rank changes. |
| 4:30-6:00 | Inspect a video and its evidence | Visual caption availability; current ASR/OCR stubs; missing text handled honestly. |
| 6:00-8:30 | Open recorded Tracks A/B/C and bootstrap | Benchmark identity, labels, confidence intervals, and limits of the saved experiment. |
| 8:30-9:30 | Open one query failure | Expected versus retrieved interval and source report. |
| 9:30-11:00 | Show short-clip processing/job state | Actual progress, generated artifacts, safe publication; precomputed example if compute is slow. |
| 11:00-12:00 | Show Colab handoff receipt and export | Verified transfer and reproducibility; live notebook optional after prior validation. |

## Scientific Talking Points

Recorded wave100 comparison: Recall@10 0.4237 -> 0.4329; MRR 0.2591 -> 0.2611;
Mean Top-1 IoU 0.1922 -> 0.1922. These are saved-run values, not guaranteed outcomes
for future data. The dashboard reads them from reports, not this document.

The reranker changes candidate order, not boundaries. A different top result can
change temporal overlap, although the stored experiment's aggregate was unchanged.
Track A/B labels need stronger independent human review. Saved training metadata
points to the evaluation benchmark; do not describe this replay as independently
held-out generalization. The finite bootstrap estimates must be interpreted in
that experimental context.

## Recovery Table

| Failure | Presenter action |
| --- | --- |
| Internet/Colab disconnected | Continue local search/media and recorded reports; show a prior verified handoff receipt. |
| Live query model not ready | Show recorded report/query outcomes with recorded status; resolve preflight before claiming live search. |
| Processing slow | Leave job running or cancel; continue on the previously active index. |
| One video fails | Open another verified example and keep missing-media state visible. |
| Browser reconnects | Restore selected query/snapshot and playback position after host reconnect. |
| Application restarts | Reopen persisted collection; inspect interrupted job and retry only deliberately. |

Release requires completed gates in verification-plan.md. A fallback keeps the
presentation honest; it does not turn an unverified runtime feature into a passed test.

