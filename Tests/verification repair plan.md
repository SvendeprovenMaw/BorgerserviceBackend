# Plan For Repair Scope

Dette dokument beskriver næste planlagte scope oven på verification-servicen: et repair-first flow med stage gates, thresholds og kontrolleret regeneration.

Det er stadig et designforslag. Der er ikke implementeret repair-kode endnu.

## Formål

Målet er at gøre pipeline-flowet robust over for normale LLM-fejl uden at lade et svagt mellemresultat forplante sig videre til næste fase.

Repair-scope skal derfor kunne:

- sortere hallucinerede eller utilstrækkeligt understøttede elementer fra
- reparere sikre strukturfejl og inkonsistente relationer
- genberegne om output stadig har nok kvalitet til næste fase
- stoppe eller regenerere en fase, hvis repair efterlader for lidt brugbart indhold

## Designprincipper

### 1. Repair er konservativt

Repair må gerne:

- fjerne elementer
- nedtone confidence eller severity
- omskrive tekst mere forsigtigt
- normalisere struktur og relationer

Repair må ikke:

- opfinde nye evidensobjekter
- opfinde nye citations
- opfinde nye requirement-links
- opfinde ny støtte, som ikke findes i upstream-materialet

### 2. Downstream kræver både validitet og tilstrækkelighed

Det er ikke nok, at repaired output er schema-validt.

En fase må først gå videre, når den både er:

- mekanisk gyldig nok
- dækkende nok til at næste fase stadig kan arbejde meningsfuldt

### 3. Repair og regeneration er forskellige værktøjer

- repair løser lokale problemer i et ellers brugbart output
- regeneration bruges, når en fase er for svag som helhed

## Foreslået flow pr. fase

1. Generér fase-output med nuværende prompt og schema.
2. Kør mechanical verification.
3. Klassificér findings som hard-invalid eller soft-quality.
4. Hvis policy tillader det, kør deterministisk repair.
5. Hvis policy tillader det, kør constrained LLM repair på det reducerede problemfelt.
6. Re-kør mechanical verification på repaired output.
7. Evaluer stage gate og thresholds.
8. Hvis gaten består, fortsæt til næste fase.
9. Hvis gaten fejler og retry budget findes, regenerér samme fase med verifier-feedback.
10. Hvis gaten stadig fejler, stop pipelinen.

## Foreslået policy i config

Repair-scope bør ikke styres af ét globalt minimumstal. Det bør være stage-specifik config.

Et muligt format i appsettings kunne være:

```json
{
  "Verification": {
    "RepairEnabled": true,
    "MaxRepairAttemptsPerStage": 1,
    "MaxRegenerationAttemptsPerStage": 1,
    "Stages": {
      "CandidateEvidence": {
        "BlockOnAnyHardInvalid": true,
        "MaxDiscardRatio": 0.40,
        "MinApprovedItems": 4,
        "MinCoveredRequirements": 3,
        "MinStrongOrMediumItems": 2
      },
      "Matching": {
        "BlockOnAnyHardInvalid": true,
        "MinRequirementCoverageRatio": 0.75,
        "AllowHighConfidenceWithoutEvidence": false
      },
      "ApplicationGeneration": {
        "BlockOnAnyHardInvalid": true,
        "MaxUnsupportedClaims": 0,
        "MaxDanglingRelations": 0,
        "MaxUnsupportedSections": 0
      }
    }
  }
}
```

## Finding-klassifikation

Repair-scope bør arbejde med to niveauer af findings:

### Hard-invalid

Eksempler:

- schema-fejl
- manglende citations
- requirement-, evidence- eller claim-referencer der ikke findes
- tomme obligatoriske felter
- claim-section relationer der ikke er gensidigt konsistente

Disse findings bør som udgangspunkt blokere downstream, indtil de er repareret eller fjernet.

### Soft-quality

Eksempler:

- weak evidence
- dubletter eller næsten-dubletter
- high confidence uden stærk støtte
- for lav requirement coverage
- for generisk ansøgningstekst

Disse findings må godt overleve i begrænset omfang, hvis stage-gaten stadig er opfyldt.

## Stage gates

### Requirements

Denne fase er mest følsom over for forkert kildeudtræk og dubletter.

Gaten bør primært sikre:

- nul citations til kandidatmateriale
- nul tomme requirement-felter
- ingen dublette requirement_id
- eventuel minimumsmængde af must-have krav, hvis jobopslag senere mærkes med prioritet

### Candidate evidence

Dette er den vigtigste repair-fase, fordi hallucinationer her kan ødelægge alt downstream.

Gaten bør mindst sikre:

- minimum approved evidence count
- maksimum discard ratio
- minimum antal dækkede requirements
- minimum antal strong eller medium evidensobjekter
- ingen surviving evidens med ugyldige requirement-links

Hvis størstedelen af evidensobjekterne forsvinder i repair, bør fasen ikke fortsætte.

### Matching

Gaten bør mindst sikre:

- minimum coverage ratio på requirements
- ingen matched requirement uden surviving evidens
- ingen high-confidence match uden surviving evidens
- ingen references til discarded evidence_ids

### Application generation

Gaten bør mindst sikre:

- nul dangling claim-section relationer
- nul evidence-backed claims uden surviving evidence
- nul section- eller strategy-references til discarded requirement_ids eller evidence_ids
- assembled application skal stadig være konsistent med surviving sections

## Foreslåede services og modeller

### Nye services

- `IRepairOrchestrator`
- `IDeterministicRepairService`
- `ILlmRepairService`
- `IDownstreamGateEvaluator`
- `IStageRegenerationService`
- `IRepairPolicyProvider`

### Nye modeller

- `RepairRequest`
- `RepairAttempt`
- `RepairAction`
- `RepairResult`
- `GateEvaluationResult`
- `StageThresholdPolicy`
- `StageExecutionDecision`

## Deterministisk repair først

Før et LLM bruges til repair, bør systemet prøve de sikre operationer i kode.

Det kan fx være:

- fjernelse af dublette IDs
- fjernelse af tomme objekter
- fjernelse af citations med ugyldige filer
- fjernelse af evidens, der peger på requirement_ids som ikke findes
- fjernelse af matched_evidence_ids som ikke findes længere
- nedjustering af confidence når støtte mangler

Det reducerer både omkostning og risiko ved efterfølgende LLM repair.

## Constrained LLM repair

LLM repair bør kun bruges på dokumenter, som allerede er schema-valide eller tæt på schema-valide efter deterministisk repair.

Input til repair-modellen bør være:

- det originale output
- mechanical findings
- upstream verified JSON
- en eksplicit liste over tilladte repair-handlinger

Prompten bør eksplicit forbyde modellen at opfinde ny støtte.

## Regeneration

Hvis repair fjerner så meget indhold, at stage-gaten fejler, bør næste skridt ikke være endnu et aggressivt repair-pass. Det bør være regeneration af samme fase med feedback.

Feedback til regeneration bør bestå af:

- kort summary af de blokkerende findings
- hvilke elementtyper der blev kasseret
- hvilke thresholds der ikke blev opfyldt
- instruktion om at være mere konservativ og kun returnere stærkt understøttede elementer

## Persistence og artifacts

Repair-scope bør gemme flere artifacts pr. fase end den nuværende verifier gør.

Eksempel:

```text
LLM/Results/Run N/
  candidate_evidence.json
  verification/
    candidate_evidence_verification.json
    candidate_evidence_gate.json
  repair/
    candidate_evidence_repair_attempt_1_input.json
    candidate_evidence_repair_attempt_1_output.json
    candidate_evidence_repair_attempt_1_verification.json
    candidate_evidence_repair_attempt_1_gate.json
    candidate_evidence_regeneration_attempt_1_feedback.json
```

Det gør det muligt at inspicere, hvorfor noget blev fjernet, og om regeneration faktisk forbedrede kvaliteten.

## Foreslået implementeringsrækkefølge

### Fase 1: Policy og gates

1. Opret config-modeller for stage thresholds og retry budgets.
2. Implementér `IDownstreamGateEvaluator` oven på den eksisterende mechanical verification.
3. Returnér gate metrics og decision i verification-resultatet.

### Fase 2: Deterministisk repair

1. Implementér deterministisk repair for candidate evidence først.
2. Re-verificér repaired output og gem repair artifacts.
3. Stop downstream når gaten fejler efter repair.

### Fase 3: LLM repair

1. Tilføj constrained LLM repair for candidate evidence.
2. Gør repair-prompten stage-specifik.
3. Genbrug mekaniske findings som struktureret input.

### Fase 4: Regeneration

1. Implementér regeneration-loop for candidate evidence.
2. Kør kun regeneration hvis repair ikke klarer gaten.
3. Begræns retry budget stramt.

### Fase 5: Udrulning til matching og application generation

1. Udvid gate evaluator til matching.
2. Udvid repair til application generation.
3. Overvej requirements repair til sidst, ikke først.

## Anbefalet første iteration

Den mest realistiske første iteration er:

- mechanical verification som det nuværende fundament
- gate evaluator med stage thresholds
- deterministisk repair for candidate evidence
- stop ved gate failure

Det giver den største værdiforøgelse med lavest risiko. LLM repair og regeneration kan bygges ovenpå bagefter.