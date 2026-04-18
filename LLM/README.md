# LLM Pipeline Contracts

This folder already contained the prompt files, parsing schemas, verification schemas, and sample result runs for the current LLM pipeline.

The missing piece was a shared contract layer for the two workflows described in `Docs/Plans/LLMPipelineSplit.md`:

- Auto mode: submit one job listing source and let the backend hydrate user context, run every phase, and return final artifacts.
- Manual mode: stop after each verified phase, return the JSON artifact for review/editing, and continue only after explicit approval.

## Added in this folder

- `ApplyAI.LlmPipeline/`: a small .NET 9 library with pipeline request/response contracts, a phase catalog wired to the existing prompt/schema files, submission validation, and a state machine for progress snapshots.
- `ApplyAI.LlmPipeline.Tests/`: focused tests covering accepted-response URLs, manual approval flow, auto completion, catalog mapping, and submission validation.
- `OpenApi/llm-pipeline.openapi.yaml`: a proposed API surface and swagger grouping that matches the auto/manual workflow split without touching the current backend controller yet.

## Verified phase order

The current result assets show this core phase sequence:

1. `company_context`
2. `requirements`
3. `candidate_evidence`
4. `matching`
5. `application_generation`

Verification and repair are not separate top-level workflows. They are sub-activities inside each phase, so the progress model treats them as detailed activity states such as:

- `sending_task_to_llm`
- `waiting_for_llm_response`
- `parsing_response`
- `running_verification`
- `running_repair`
- `awaiting_user_approval`

## Progress model

The `PipelineJobStateMachine` is designed to produce JSON snapshots that the frontend can poll or consume through server-sent events.

Each snapshot includes:

- a stable `statusUrl`
- an `eventsUrl` for low-cost live updates
- the current phase and activity
- a human-readable status message
- progress percentage
- per-phase attempt counts, warnings, errors, and artifacts
- action links when the user must approve or retry a phase

## Suggested transport

Use server-sent events first, not MQTT.

Why:

- The frontend already uses RxJS, and wrapping `EventSource` in an observable is cheap.
- The pipeline status stream is one-way server-to-browser traffic, which fits SSE better than MQTT.
- SSE keeps the backend model simpler than standing up a broker or websocket hub for this use case.

Fallback:

- Keep the `statusUrl` polling contract as the baseline.
- Use `recommendedPollIntervalSeconds` from the snapshot.
- Only poll every 5 seconds while waiting on the LLM, and every 2 seconds during verification or manual approval states.

## Proposed swagger groups

- `LLM Pipeline Jobs`: create and inspect pipeline jobs.
- `LLM Pipeline Manual Review`: approve or retry a specific phase.
- `LLM Pipeline Events`: SSE feed for responsive progress updates.

## Integration steps outside this folder

The contracts in this folder are ready to be consumed, but they do not replace the missing backend wiring.

The next integration work outside `LLM/` should be:

1. Add job persistence fields to `AiProcessingJob` for workflow mode, current phase, activity, status, and timestamps.
2. Implement the routes from `OpenApi/llm-pipeline.openapi.yaml` in `AiController`.
3. Wire `OpenAIService` (or a dedicated orchestration service) to hydrate the logged-in user context, run the phase executors, and persist snapshots.
4. Replace the frontend mock document adapter with real HTTP calls and an RxJS wrapper around SSE plus polling fallback.