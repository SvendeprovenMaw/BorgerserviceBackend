# Plan For Verification Service

Dette dokument er en plan for den videre udbygning af verification-servicen til pipeline-flowet.

Grundlæggende mechanical verification er allerede implementeret. Dokumentet beskriver den målarkitektur, der stadig mangler omkring repair, thresholds, gates og udvidet orchestration.

Den mere konkrete implementeringsskitse findes i [verification implementation checklist.md](verification%20implementation%20checklist.md).

## Mål

Servicen skal kunne verificere output fra alle pipeline-steps på en ensartet måde, men stadig have step-specifikke regler.

Den skal kunne håndtere:

- fælles mekaniske checks for alle dokumenttyper
- step-specifikke mekaniske checks
- repair-first downstream-kontrol, hvor hallucinationer og svage matches kan sorteres fra
- stage-specifikke thresholds i config, så downstream kun tillades ved tilstrækkelig datakvalitet
- LLM-baseret verifiering som supplement til mekaniske checks
- referencer mellem upstream- og downstream-dokumenter
- klare pass, warning og fail-resultater
- persistence af verifieringsresultater pr. run og pr. fase

## Overordnet idé

Designet bør være lagdelt:

1. En fælles mekanisk verifier-motor i kode.
2. En fase-specifik LLM verifier, som kun kører når det giver mening.

Det betyder i praksis:

- mekanisk verifiering kører altid først
- hvis JSON eller schema fejler, skal LLM-verifieren normalt ikke køre
- hvis mekaniske checks består eller kun giver warnings, kan LLM-verifieren køre
- endeligt resultat samles i en samlet verification report

## Udvidet strategi: repair før downstream

Når et LLM indgår i hver fase, bør false positives og delvist svage outputs forventes som en normal driftstilstand og ikke som en undtagelse.

Den praktiske strategi bør derfor ikke være "alt eller intet" på første output, men heller ikke "fortsæt så længe schemaet passer". Den bør være:

1. Generér dokumentet.
2. Verificér dokumentet mekanisk.
3. Reparér konservativt, hvis findings kan afhjælpes uden at opfinde ny støtte.
4. Re-verificér det reparerede dokument.
5. Evaluer stage-thresholds og downstream-gate.
6. Fortsæt kun hvis output stadig har nok kvalitet og dækning.

Det er især vigtigt for candidate evidence. Hvis et repair-loop fjerner størstedelen af evidensobjekterne, kan outputtet være formelt gyldigt, men stadig ubrugeligt for næste fase. I så fald skal pipelinen stoppe eller regenerere samme fase frem for at gå videre med et tyndt datagrundlag.

## Klassifikation af findings

Findings bør ikke kun have severity. De bør også opdeles i to operationelle grupper:

- hard-invalid: schema-brud, manglende citations, ugyldige referencer, tomme obligatoriske felter, dangling relations
- soft-quality: svag støtte, lav coverage, dubletter, overmodig confidence, tekstmæssig redundans

Denne opdeling gør det muligt at styre repair mere præcist:

- hard-invalid skal være væk eller eksplicit nedgraderet via policy, før downstream normalt tillades
- soft-quality må godt eksistere i begrænset omfang, hvis stage-thresholds stadig er opfyldt

## Foreslået arkitektur

### 1. Orchestrator

Én central service, fx en VerificationOrchestrator, som tager imod:

- hvilken fase der verificeres
- det genererede dokument
- relevante upstream-dokumenter
- metadata om run-folder, filer og schemas

Orchestratoren skal kalde disse lag i rækkefølge:

1. Common mechanical checks
2. Phase-specific mechanical checks
3. Repair-klassifikation og eventuel repair-pass
4. Re-verification og threshold-evaluering
5. LLM verification, hvis policy tillader det

Til sidst samles alt i én verification report med samlet status.

Orchestratoren bør også kunne skelne mellem tre udfald pr. fase:

- approved: downstream er tilladt
- retryable: output bør regenereres eller repareres igen
- blocked: pipelinen skal stoppe

### 2. Fælles verification context

Alle rules bør arbejde på en delt context-model, fx VerificationContext, med felter som:

- pipeline_stage
- run_id eller run_folder
- generated_document_json
- generated_document_id
- parsed_files
- source_file_roles
- expected_output_schema
- verification_schema
- upstream_documents
- prompt_paths og schema_paths

Det gør, at mange rules kan genbruges på tværs af faser.

Contexten bør desuden kunne bære:

- originale findings
- repaired document JSON
- discard-statistik pr. repair-attempt
- gate metrics som coverage og surviving item counts
- retry counters for repair og regeneration

### 3. Rule packs pr. fase

I stedet for én stor verifier bør der være rule packs:

- CommonRules
- RequirementsRules
- CandidateEvidenceRules
- MatchingRules
- ApplicationGenerationRules

Hver rule returnerer findings med severity og machine-readable rule id.

### 4. Gate evaluator

Ud over almindelig verifiering bør designet have en separat GateEvaluator, der vurderer om en fase må gå videre efter repair.

Den bør bruge stage-specifik policy fra config i stedet for én global minimum-værdi.

Eksempler:

- Candidate evidence bør gate på approved evidence count, discard ratio og requirement coverage
- Matching bør gate på requirement coverage og forbud mod high-confidence matches uden overlevende evidens
- Application generation bør gate på nul dangling relations og nul unsupported evidence-backed claims

### 5. Repair-lag

Repair bør opdeles i to typer:

- deterministisk repair i kode for sikre strukturproblemer
- constrained LLM repair for semantisk oprydning, pruning og konservativ omskrivning

Deterministisk repair bør tage de billige og sikre fejl først, fx:

- fjerne dubletter
- fjerne tomme objekter
- fjerne ugyldige references eller citations der allerede er bevist ugyldige
- nedjustere confidence når støtten ikke matcher

LLM repair bør kun køre på et allerede valideret og afgrænset input og må kun:

- prune eksisterende elementer
- omskrive eksisterende tekst mere konservativt
- relinke eksisterende elementer, hvis relationen allerede kan bevises af upstream-data
- nedtone eller afvise påstande

LLM repair må ikke:

- opfinde ny evidens
- opfinde nye citations
- opfinde nye requirement-links
- tilføje støtte som ikke allerede findes i upstream-dokumenterne

## Foreslået datamodel for findings

Hver verification-regel bør returnere et ens objekt, fx:

- rule_id
- category
- severity
- subject_type
- subject_id
- message_da
- details
- suggested_fix_da
- blocking_for_downstream

### Severity-niveauer

- info: kun sporbarhed eller statistik
- warning: dokumentet kan bruges, men bør inspiceres
- error: dokumentet er ikke godkendt til downstream

### Category-eksempler

- json_parse
- schema_validation
- metadata
- referential_integrity
- uniqueness
- citation_quality
- source_alignment
- llm_semantic_review

## Fælles verification-flow

For alle steps bør flowet være:

1. Parse JSON
2. Validér mod output-schema
3. Kør common mechanical checks
4. Kør phase-specific mechanical checks
5. Klassificér findings i hard-invalid og soft-quality
6. Hvis policy tillader det: kør repair-pass
7. Re-kør schema og mechanical checks på repaired output
8. Evaluer stage-thresholds og downstream-gate
9. Hvis stadig gyldigt nok: kør LLM-verifier
10. Aggregér findings til summary
11. Gem verification artifacts i run-folder

Hvis repaired output ikke klarer downstream-gaten, bør systemet ikke automatisk fortsætte. Det bør i stedet enten:

- regenerere samme fase med verifier-findings som feedback
- eller stoppe runnet og markere fasen som utilstrækkelig

Valget mellem stop og regeneration bør være policy-styret.

## Stage-thresholds i config

En ren minimum-count er ikke nok som gate. Policy bør være stage-specifik.

### Candidate evidence

Candidate evidence bør typisk gate på en kombination af:

- minimum approved evidence count
- maksimum discard ratio
- minimum antal dækkede requirement_ids
- minimum antal strong eller medium evidensobjekter
- eventuel minimumsdækning for kritiske krav

Det betyder fx, at 1 overlevende evidens ikke er nok, selv hvis schema og references stadig er gyldige.

### Matching

Matching bør typisk gate på:

- minimum coverage ratio for requirements
- forbud mod matched requirements, der kun hviler på discarded evidens
- forbud mod high-confidence uden overlevende evidens
- eventuel særregel for must-have requirements

### Application generation

Application generation bør typisk gate på:

- nul dangling claim-section relationer
- nul unsupported evidence-backed claims
- nul references til bortrepareret evidens
- eventuel maksimumgrænse for generisk bridge-tekst uden støtte

## Repair kontra regeneration

Repair skal bruges til lokal oprydning. Regeneration skal bruges, når en fase som helhed er for svag.

En god tommelfingerregel er:

- brug repair når hovedparten af outputtet er brugbart, men enkelte dele skal sorteres eller normaliseres
- brug regeneration når stage-thresholds fejler, coverage er for lav, eller repair vil fjerne så meget indhold, at næste fase mister sit datagrundlag

Det bør styres af separate retry budgets i config, fx:

- max repair attempts per stage
- max regeneration attempts per stage
- stop immediately on threshold failure eller allow single regeneration

## Common mechanical checks

De fælles checks fra dine regler bør ligge i et delt regelsæt.

### JSON checks

- output må ikke være tomt
- output skal kunne parses som JSON
- root skal være et objekt

### Schema validation

- output matcher forventet schema præcist
- alle required felter findes
- typer er gyldige
- enum-værdier er gyldige
- ingen felter uden for schemaet når additionalProperties er false

### Metadata checks

- dokument-ID findes
- schema-version findes, hvis jeres policy kræver det
- parsed_files findes
- errors-felt findes, selv hvis tomt

Bemærkning:
De nuværende parsing-dokumenter har ikke alle schema-version-felter. Planen bør derfor indføre en tydelig policy for hvilke metadatafelter der er obligatoriske fremadrettet. Hvis eksisterende schemas ikke har dem endnu, bør reglen kunne køre i compatibility mode og markere warning i stedet for hard fail.

### Unikke ID’er

- alle object IDs i relevante arrays er unikke
- ingen dubletter i lister med IDs

### Citation-minimum

- hvert claim, krav eller evidensobjekt har mindst én citation, når dokumenttypen kræver det
- citation har filename
- citation har excerpt
- excerpt må ikke være tomt eller whitespace

### Parsed file consistency

- alle citerede filer findes i _meta.parsed_files
- ingen citation peger på filer, som ikke var en del af runnet

## Phase-specific mechanical rules

### Requirements extraction

Mekaniske regler:

- hvert krav har requirement_id
- hvert krav har requirement_text_da
- hvert krav har normalized_label
- hvert krav har category
- hvert krav har importance
- hvert krav har mindst én citation
- ingen dubletter af requirement_id
- ingen tomme normalized_label
- ingen tomme requirement_text_da
- alle citations skal være fra jobopslaget
- citations må ikke pege på kandidatdokumenter
- ingen to krav må have samme normalized_label og identisk tekst
- importance skal være gyldig enum

### Candidate evidence

Mekaniske regler:

- hvert evidensobjekt har evidence_id
- hvert evidensobjekt har fact_da
- hvert evidensobjekt har normalized_label
- hvert evidensobjekt har category
- hvert evidensobjekt har support_type
- hvert evidensobjekt har strength
- hvert evidensobjekt har mindst én citation
- relevant_requirement_ids skal findes, hvis dokumentet er krav-orienteret
- alle relevant_requirement_ids skal findes i krav-dokumentet
- alle citations skal pege på kandidatmateriale og ikke jobopslaget
- fact_da må ikke være tom
- requirement_relevance_reason_da må ikke være tom, hvis der er krav-links

Ekstra heuristik i kode:

- hvis support_type er testimonial, bør kilden ligne reference eller testimonial
- hvis support_type er document_metadata, skal excerpt stadig være udfyldt
- hvis strength er strong men citationerne er meget svage, markér warning

### Matching

Mekaniske regler:

- hvert match har gyldigt requirement_id
- requirement_id findes i krav-dokumentet
- alle matched_evidence_ids findes i evidens-dokumentet
- ingen dubletter i matched_evidence_ids
- verdict er gyldig enum
- confidence er gyldig enum
- rationale_da findes og er ikke tom
- major_strength_evidence_ids findes i evidens-dokumentet
- major_gap_requirement_ids findes i krav-dokumentet

Ekstra heuristik i kode:

- hvis verdict er not_matched, bør matched_evidence_ids normalt være tom
- hvis verdict er matched, bør matched_evidence_ids normalt ikke være tom
- hvis overall_match_level er strong, men der er mange major_gap_requirement_ids, flag warning eller error
- hvis confidence er high og matched_evidence_ids er tom, flag inkonsistens

### Application generation

Denne fase bør have det mest omfattende mekaniske regelsæt, fordi den refererer til tre upstream-dokumenter og mange interne relationer.

Mekaniske regler:

- application_document_id må ikke være tom
- requirements_document_id skal matche det faktiske krav-dokument
- candidate_evidence_document_id skal matche det faktiske evidens-dokument
- matching_document_id skal matche det faktiske matching-dokument
- alle section_id er unikke
- alle claim_id er unikke
- alle section.claim_ids peger på eksisterende claim_id
- alle claim.section_ids peger på eksisterende section_id
- relationen mellem sections og claims er gensidigt konsistent
- selected_requirement_ids findes i krav-dokumentet
- selected_evidence_ids findes i evidens-dokumentet
- omitted_requirement_ids findes i krav-dokumentet
- section.supported_requirement_ids findes i krav-dokumentet
- section.supported_evidence_ids findes i evidens-dokumentet
- claim.evidence_ids findes i evidens-dokumentet
- claim.requirement_ids findes i krav-dokumentet
- claim_text_da må ikke være tom
- section_ids må ikke være tom
- candidate_fact, candidate_strength og role_alignment bør have mindst én evidence_id
- role_alignment bør normalt have mindst én requirement_id
- motivation_grounded bør kun bruges, hvis claimet faktisk er forankret i inputtet
- section.text_da må ikke være tom
- bridge_text bør normalt have tom eller meget begrænset claim_ids
- evidence_backed og mixed bør normalt have claim_ids
- supported IDs i section bør være konsistente med sektionens claims
- assembled_application_da må ikke være tom
- sektionstekster bør kunne genfindes i assembled_application_da i samme rækkefølge efter whitespace-normalisering
- assembled_application_da bør ikke have store tekstdele, som ikke findes i sections
- omitted requirements bør normalt ikke samtidig fremstå centralt understøttet i sections
- claims bør ikke være dubletter i let omskrevet form

## LLM verification lag

LLM-verification skal være et separat lag oven på de mekaniske checks.

### Hovedprincip

Kode skal afgøre struktur og referencer.
LLM skal kun vurdere semantik, styrke, overdrivelse og rimelig forankring.

Det er vigtigt, at LLM-verifieren ikke får lov at blive en reparationsmotor.

### Fælles LLM-principper

LLM-verifieren må ikke:

- opfinde nye facts
- opfinde nye IDs
- opfinde nye relationer
- kreativt reparere output
- godkende noget kun fordi det lyder plausibelt

LLM-verifieren skal:

- vurdere claims konservativt
- vurdere source alignment
- nedgradere overstated formuleringer
- markere unsupported eller weakly_supported når nødvendigt
- kunne sætte needs_human_review

### Requirements LLM verification

Brug det eksisterende verification schema i [requirements_verification_schema.json](LLM/AI%20Schemas/LLM%20Verification/requirements_verification_schema.json).

### Candidate evidence LLM verification

Brug det eksisterende verification schema i [candidate_evidence_verification_schema.json](LLM/AI%20Schemas/LLM%20Verification/candidate_evidence_verification_schema.json).

### Matching LLM verification

Brug det eksisterende verification schema i [requirement_match_verification_schema.json](LLM/AI%20Schemas/LLM%20Verification/requirement_match_verification_schema.json).

### Application generation LLM verification

Brug det eksisterende prompt-sæt fra dine regler, men bemærk at [application_generation_verification_schema.json](LLM/AI%20Schemas/LLM%20Verification/application_generation_verification_schema.json) aktuelt er tom.

Det betyder, at implementation af application LLM-verification bør vente på, at dette schema bliver defineret.

Det er et eksplicit precondition for implementeringen.

## Foreslået runtime-kontrakt

Servicen bør kunne kaldes med en kontrakt i stil med:

- stage_name
- generated_document_json
- generated_document_schema_path
- verification_schema_path
- upstream_documents
- parsed_files
- verification_policy_version
- verification_mode

Hvor verification_mode fx kan være:

- mechanical_only
- mechanical_plus_llm
- fail_fast
- permissive

## Policy og fleksibilitet

For at gøre løsningen fleksibel bør man skelne mellem:

- regler der altid er hårde errors
- regler der kan være warnings i compatibility mode
- regler der kun gælder for bestemte document versions

Det kan styres gennem en VerificationPolicy, fx:

- version
- enabled_rules
- severity_overrides
- stop_on_first_error
- run_llm_verifier_if_mechanical_failed
- downstream_block_threshold

Det giver mulighed for at stramme verification gradvist uden at bryde eksisterende runs.

## Foreslået output fra verification service

Der bør gemmes både detaljer og en samlet summary.

### Pr. fase

For hver fase bør følgende gemmes:

- mechanical_verification.json
- llm_verification.json hvis kørt
- combined_verification.json

### Combined verification report

Den samlede report bør indeholde:

- stage_name
- input_document_id
- output_document_id
- policy_version
- mechanical_status
- llm_status
- final_status
- approved_for_downstream
- findings
- summary_counts
- created_at

## Placering i run-folder

Anbefalet struktur pr. run:

- requirements.json
- candidate_evidence.json
- matching.json
- application_generation.json
- verification/
  - requirements_mechanical.json
  - requirements_llm.json
  - requirements_combined.json
  - candidate_evidence_mechanical.json
  - candidate_evidence_llm.json
  - candidate_evidence_combined.json
  - matching_mechanical.json
  - matching_llm.json
  - matching_combined.json
  - application_generation_mechanical.json
  - application_generation_llm.json
  - application_generation_combined.json
  - pipeline_verification_summary.json

## Downstream-adfærd

For pipeline-kobling bør servicen støtte to modes:

### 1. Strict mode

- hvis en fase får final_status = fail, stopper pipeline
- downstream-faser kører ikke

### 2. Inspect mode

- output gemmes stadig
- verification gemmes stadig
- pipeline kan fortsætte efter warnings eller endda errors, hvis målet er analyse og tuning

Til jeres nuværende arbejde med accuracy-inspektion virker inspect mode mest praktisk i starten.

## Foreslået implementeringsopdeling senere

Når I vil implementere, vil en robust rækkefølge være:

1. Definér shared verification result models.
2. Implementér common mechanical verifier.
3. Implementér requirements mechanical rules.
4. Implementér evidence mechanical rules.
5. Implementér matching mechanical rules.
6. Implementér application mechanical rules.
7. Implementér verification persistence i run-folder.
8. Integrér orchestrator i pipeline efter hver fase.
9. Brug eksisterende verification schemas for requirements, evidence og matching.
10. Udfyld application generation verification schema.
11. Implementér application LLM-verifier til sidst.

## Konkrete designbeslutninger jeg vil anbefale

- Hold mekaniske checks i kode, ikke som frie JSON-regler. Mange checks er relationelle og kræver objektgrafer.
- Hold aktivering og severity i policy-konfiguration, så fleksibiliteten ligger i policy og ikke i selve regeldefinitionen.
- Lad LLM-verifieren være rent vurderende, aldrig reparerende.
- Gem både rå LLM verification output og en samlet backend-aggregated status.
- Gør application verification til den strengeste fase, fordi den er længst fra kildedokumenterne og har størst risiko for overfortolkning.

## Åbne punkter før implementering

- Skal schema-version være et hårdt krav allerede nu, selv om de nuværende outputschemas ikke tydeligt bærer version i output?
- Skal downstream-faser stoppes på fail, eller skal inspect mode være default i testmiljøet?
- Skal mechanical verifieringen også tjekke mod faktiske run-input filer på disk, eller kun mod _meta.parsed_files?
- Hvad skal den endelige application_generation_verification_schema præcist indeholde?

## Anbefalet næste skridt

Næste konkrete skridt bør være at definere det manglende application generation verification schema og derefter bygge den fælles mechanical verifier-motor først.