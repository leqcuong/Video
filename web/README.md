# Thesis Demo Web Development Pack

Status: development documents prepared; the new .NET application is not implemented.

Start with [the product specification](../specs/001-thesis-demo/spec.md), then
[the implementation plan](../specs/001-thesis-demo/plan.md) and
[the task backlog](../specs/001-thesis-demo/tasks.md).

The agreed setup is a .NET website on the laptop, with optional GPU processing in
a user-operated Google Colab notebook. Python remains the ML and evaluation engine.
The proposed UI is Blazor Interactive Server; the host also exposes versioned APIs.
SQL Server supports .NET practice, and SQLite supports portable demonstration.

## Documents

| Document | Purpose |
| --- | --- |
| [Spec](../specs/001-thesis-demo/spec.md) | Personas, scope, user stories, requirements, success criteria. |
| [Plan](../specs/001-thesis-demo/plan.md) | Stack, architecture, ownership, deployment, delivery phases. |
| [Research](../specs/001-thesis-demo/research.md) | Verified inputs, conflicts, and technical decisions with sources. |
| [Data model](../specs/001-thesis-demo/data-model.md) | Entities, keys, versions, states, and database portability. |
| [API contract](../specs/001-thesis-demo/contracts/api.md) | Endpoints, requests, responses, errors, and concurrency. |
| [Python contract](../specs/001-thesis-demo/contracts/pipeline-bridge.md) | Legacy calls, search protocol, artifacts, and publication. |
| [Colab contract](../specs/001-thesis-demo/contracts/colab-handoff.md) | Portable job bundles, notebook execution, result import. |
| [UI specification](../specs/001-thesis-demo/ux-spec.md) | Screens, controls, content, states, and accessibility. |
| [Traceability](../specs/001-thesis-demo/traceability.md) | Thesis/customer/backlog features mapped to work and verification. |
| [Verification](../specs/001-thesis-demo/verification-plan.md) | Contract, integration, parity, and acceptance checks. |
| [Quickstart](../specs/001-thesis-demo/quickstart.md) | Implementation prerequisites and future local run procedure. |
| [Defense runbook](../specs/001-thesis-demo/demo-runbook.md) | Rehearsal, presentation sequence, and recovery. |
| [Tasks](../specs/001-thesis-demo/tasks.md) | Ordered, reviewable development tasks with file ownership. |
| [Risks and decisions](../specs/001-thesis-demo/risks-and-decisions.md) | Assumptions, technical risks, and release decisions. |

The research document records the source material and design corrections. Source
paths for the archived pipeline refer to data outside this web-only repository.

## Current evidence

The paper records 896 assets. The current index and asset folders contain 895
distinct assets, verified on 2026-10-10. The raw benchmark contains 3,500 queries;
the cleaned benchmark contains 3,486. The application must report actual imported
counts while retaining the historical paper context.

The locally discovered SDK is .NET 9.0.102; .NET 10 must be installed for this plan.
The default Python found on PATH is 32-bit Python 3.13; implementation must select
a compatible 64-bit ML environment explicitly. No SDKs or models were installed
while preparing these documents.

