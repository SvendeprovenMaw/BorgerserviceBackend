# Verification Stage Sequence Diagrams

This document visualizes the currently implemented verification flow in the verified sample pipeline.

Each diagram starts when a stage is executed inside `SampleLlmFlowService` and follows the stage output through persistence, mechanical verification, gate evaluation, and any stage-specific recovery logic.

## Requirements Stage

```mermaid
sequenceDiagram
    autonumber
    actor Route as Verified Pipeline Route
    participant Flow as SampleLlmFlowService
    participant OpenAI as OpenAI Responses API
    participant Results as LLM/Results/Run N
    participant Verify as VerificationOrchestrator
    participant Gate as DownstreamGateEvaluator
    participant Repair as RequirementsDeterministicRepairService
    participant VNotes as Verification Notes
    participant GNotes as Gate Notes

    Route->>Flow: RunPipelineWithVerificationAsync()
    Flow->>OpenAI: Generate requirements.json
    OpenAI-->>Flow: Requirements JSON
    Flow->>Results: Save requirements.json
    Flow->>Verify: VerifyStageAsync(requirements request)
    Verify->>VNotes: Check structure, IDs, and citations
    Note right of VNotes: JSON parse + schema subset validation.<br/>requirements[] must exist.<br/>Each requirement needs a unique requirement_id,<br/>a normalized_label, and requirement_text_da.<br/>Duplicate normalized_label + text pairs are rejected.<br/>source_citations must exist and only cite the job posting.<br/>The same checks are rerun after repair.
    Verify-->>Flow: StageVerificationResult
    Flow->>Results: Save requirements_verification.json
    Flow->>Gate: Evaluate(requirements)
    Gate->>GNotes: Apply requirements gate
    Note right of GNotes: Hard-invalid findings can block immediately.<br/>Also checks requirements.count >= configured minimum.<br/>This stage has no advisory-only path because downstream IDs depend on it.<br/>The same gate runs again after repair.
    Gate-->>Flow: GateEvaluationResult
    Flow->>Results: Save requirements_gate.json

    alt Gate not approved and repair is allowed
        Flow->>Repair: RepairAsync(requirements.json)
        Repair-->>Flow: Repaired requirements JSON + actions
        Flow->>Results: Save requirements repair artifacts
        Flow->>Verify: Re-verify repaired requirements
        Verify-->>Flow: StageVerificationResult
        Flow->>Gate: Re-evaluate repaired requirements
        Gate-->>Flow: GateEvaluationResult
        Flow->>Results: Save repaired verification and gate artifacts
    end

    alt Requirements approved
        Flow-->>Route: Continue to candidate evidence stage
    else Requirements still blocked
        Flow->>Results: Save pipeline_verification_summary.json
        Flow->>Results: Save fit_advisory.json if enabled
        Flow-->>Route: Stop pipeline after requirements
    end
```

## Candidate Evidence Stage

```mermaid
sequenceDiagram
    autonumber
    actor Route as Verified Pipeline Route
    participant Flow as SampleLlmFlowService
    participant OpenAI as OpenAI Responses API
    participant Results as LLM/Results/Run N
    participant Verify as VerificationOrchestrator
    participant Gate as DownstreamGateEvaluator
    participant VNotes as Verification Notes
    participant GNotes as Gate Notes

    Route->>Flow: Continue after approved requirements
    Flow->>OpenAI: Generate candidate_evidence.json
    OpenAI-->>Flow: Candidate evidence JSON
    Flow->>Results: Save candidate_evidence.json
    Flow->>Verify: VerifyStageAsync(candidate evidence request)
    Verify->>VNotes: Check evidence integrity and traceability
    Note right of VNotes: evidence_items[] must exist.<br/>Each evidence item needs a unique evidence_id and fact_da.<br/>relevant_requirement_ids must exist in requirements.json.<br/>Linked requirements require requirement_relevance_reason_da.<br/>citations must exist, include excerpts, and only point to candidate files.<br/>Extra heuristics flag weak testimonial or excerpt quality.
    Verify-->>Flow: StageVerificationResult
    Flow->>Results: Save candidate_evidence_verification.json
    Flow->>Gate: Evaluate(candidate evidence)
    Gate->>GNotes: Apply evidence quality metrics
    Note right of GNotes: Hard-invalid evidence can still block.<br/>The gate first filters out invalid evidence items.<br/>Then it evaluates approved_items, discard_ratio,<br/>covered_requirements, and medium/strong evidence count.<br/>Metric misses become advisory reasons, not automatic blocking.
    Gate-->>Flow: GateEvaluationResult
    Flow->>Results: Save candidate_evidence_gate.json

    Note over Flow,Gate: Decision can be continue or continue_with_advisory

    alt Candidate evidence approved
        Flow-->>Route: Continue to matching stage
    else Candidate evidence blocked
        Flow->>Results: Save pipeline_verification_summary.json
        Flow->>Results: Save fit_advisory.json if enabled
        Flow-->>Route: Stop pipeline after candidate evidence
    end
```

## Matching Stage

```mermaid
sequenceDiagram
    autonumber
    actor Route as Verified Pipeline Route
    participant Flow as SampleLlmFlowService
    participant OpenAI as OpenAI Responses API
    participant Results as LLM/Results/Run N
    participant Verify as VerificationOrchestrator
    participant Gate as DownstreamGateEvaluator
    participant Repair as MatchingDeterministicRepairService
    participant VNotes as Verification Notes
    participant GNotes as Gate Notes

    Route->>Flow: Continue after approved candidate evidence
    Flow->>OpenAI: Generate matching.json
    OpenAI-->>Flow: Matching JSON
    Flow->>Results: Save matching.json
    Flow->>Verify: VerifyStageAsync(matching request)
    Verify->>VNotes: Check references, rationale, and support
    Note right of VNotes: matches[] must exist.<br/>Each requirement_id must exist in requirements.json.<br/>matched_evidence_ids must be unique and exist in candidate_evidence.json.<br/>rationale_da is required for each match.<br/>Warnings flag matched/high-confidence claims without evidence.<br/>overall_assessment references are also validated.<br/>The same checks are rerun after repair and regeneration.
    Verify-->>Flow: StageVerificationResult
    Flow->>Results: Save matching_verification.json
    Flow->>Gate: Evaluate(matching)
    Gate->>GNotes: Apply matching gate
    Note right of GNotes: Hard-invalid findings can block.<br/>Approved requirement coverage is calculated from valid match records only.<br/>The gate checks coverage ratio and, if configured,<br/>forbids matched-without-evidence or high-confidence-without-evidence records.<br/>These quality misses stay advisory unless the policy marks them as hard failures.<br/>The same gate is reused after repair and regeneration.
    Gate-->>Flow: GateEvaluationResult
    Flow->>Results: Save matching_gate.json

    alt Advisory issue or failed gate triggers recovery
        Flow->>Repair: RepairAsync(matching.json)
        Repair-->>Flow: Repaired matching JSON + actions
        Flow->>Results: Save matching repair artifacts
        Flow->>Verify: Re-verify repaired matching
        Verify-->>Flow: StageVerificationResult
        Flow->>Gate: Re-evaluate repaired matching
        Gate-->>Flow: GateEvaluationResult
        Flow->>Results: Save repaired verification and gate artifacts

        alt Still not approved and regeneration attempts remain
            Flow->>Results: Save matching regeneration feedback
            Flow->>OpenAI: Regenerate matching with feedback JSON
            OpenAI-->>Flow: Regenerated matching JSON
            Flow->>Results: Save matching regeneration artifacts
            Flow->>Verify: Re-verify regenerated matching
            Verify-->>Flow: StageVerificationResult
            Flow->>Gate: Re-evaluate regenerated matching
            Gate-->>Flow: GateEvaluationResult
            Flow->>Results: Save regenerated verification and gate artifacts
        end
    end

    alt Matching approved
        Flow-->>Route: Continue to application generation stage
    else Matching blocked
        Flow->>Results: Save pipeline_verification_summary.json
        Flow->>Results: Save fit_advisory.json if enabled
        Flow-->>Route: Stop pipeline after matching
    end
```

## Application Generation Stage

```mermaid
sequenceDiagram
    autonumber
    actor Route as Verified Pipeline Route
    participant Flow as SampleLlmFlowService
    participant OpenAI as OpenAI Responses API
    participant Results as LLM/Results/Run N
    participant Verify as VerificationOrchestrator
    participant Gate as DownstreamGateEvaluator
    participant Repair as ApplicationGenerationDeterministicRepairService
    participant VNotes as Verification Notes
    participant GNotes as Gate Notes

    Route->>Flow: Continue after approved matching
    Flow->>OpenAI: Generate application_generation.json
    OpenAI-->>Flow: Application generation JSON
    Flow->>Results: Save application_generation.json
    Flow->>Verify: VerifyStageAsync(application request)
    Verify->>VNotes: Check graph integrity across claims and sections
    Note right of VNotes: Schema and _meta document IDs must match upstream outputs.<br/>claim_register and sections must exist with unique IDs and text.<br/>claim evidence/requirement refs must exist in upstream documents.<br/>claim.section_ids and section.claim_ids must be reciprocal.<br/>application_strategy refs are validated.<br/>assembled_application_da must exist and preserve section order.<br/>The same checks are rerun after repair.
    Verify-->>Flow: StageVerificationResult
    Flow->>Results: Save application_generation_verification.json
    Flow->>Gate: Evaluate(application generation)
    Gate->>GNotes: Apply final integrity gate
    Note right of GNotes: Hard-invalid findings can block immediately.<br/>The gate counts unsupported_claims,<br/>dangling_relations, and unsupported_sections.<br/>Each metric must stay within the configured maximum.<br/>This is the final hard integrity gate before returning the pipeline output.<br/>The same gate runs again after repair.
    Gate-->>Flow: GateEvaluationResult
    Flow->>Results: Save application_generation_gate.json

    alt Final gate not approved and repair is allowed
        Flow->>Repair: RepairAsync(application, requirements, candidate evidence)
        Repair-->>Flow: Repaired application JSON + actions
        Flow->>Results: Save application repair artifacts
        Flow->>Verify: Re-verify repaired application
        Verify-->>Flow: StageVerificationResult
        Flow->>Gate: Re-evaluate repaired application
        Gate-->>Flow: GateEvaluationResult
        Flow->>Results: Save repaired verification and gate artifacts
    end

    Flow->>Results: Save pipeline_verification_summary.json

    alt Application stage approved
        Flow->>Results: Save fit_advisory.json if enabled
        Flow-->>Route: Return completed pipeline response
    else Application stage still blocked
        Flow->>Results: Save fit_advisory.json if enabled
        Flow-->>Route: Return blocked pipeline response
    end
```

## Notes

- Requirements is the hardest prerequisite stage because downstream IDs depend on it.
- Candidate evidence can continue with advisory quality signals, but mechanical integrity failures still block.
- Matching can use deterministic repair first and regeneration second.
- Application generation is allowed to complete only after its internal claim and section references are consistent enough for the final gate.