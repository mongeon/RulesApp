# Domain Model & Rules — RulesApp (`docs/domain.md`)

This document defines the **domain behavior** of RulesApp (baseball rule research app) and the **non-negotiable constraints** that every feature (ingestion, search, chat, admin tools) must respect.

It’s written so a developer (or AI coding agent) can implement the system and validate correctness iteratively.

---

## 1) Core Goal

RulesApp helps users (players, coaches, umpires) **find answers inside the official rulebooks**:

- **Canada** rulebook (FR + EN)
- **Quebec** rulebook (FR only)
- **Regional Association** supplemental rules (FR only; one per association)

Users ask questions in natural language. The system responds **only** with information grounded in the uploaded PDFs and provides **citations** (rule number + page + rulebook).

---

## 2) Key Concepts & Glossary

### Season
- Rules are versioned by **season** (e.g., `2026`).
- Only **active season** content is searchable for now.
- Future: generate a **diff report** between seasons (not searchable).

### Association
- A regional association is identified by `associationId` (e.g., `ABC`).
- Regional rulebooks are **scoped to one association**.

### ScopeLevel (Jurisdiction)
`ScopeLevel` defines precedence order:
1. `Regional` (most specific)
2. `Quebec`
3. `Canada` (least specific)

### Rulebook
A rulebook is one of:
- `Canada` (FR/EN)
- `Quebec` (FR/EN)
- `Regional:{associationId}` (FR or EN or both)

### Chunk
A **chunk** is the unit indexed/searched (typically a rule or section).
Each chunk must have:
- `ChunkId` (unique + stable)
- `RuleNumberText` (as found in the PDF, if detected)
- `RuleKey` (canonical key when possible, e.g., `6.01(a)`)
- `Title` (best effort)
- `PageStart`, `PageEnd`
- `PdfPath` (blob path)

### RuleNumberText vs RuleKey
- `RuleNumberText`: what the PDF shows (may vary by formatting).
- `RuleKey`: canonical identifier used to match “the same rule” across scopes.
  - Example: `6.01(a)` should match across Canada/Quebec/regional override statements.
- Some regional supplements may use **independent numbering** (e.g., `R-12`). These may not map to a national/provincial `RuleKey`.

### Citation
A citation must include at minimum:
- **Rulebook** (Canada / Quebec / Regional:{associationId})
- **Rule number** (`RuleNumberText` or `RuleKey` if available)
- **Page number(s)** (PageStart–PageEnd)
- **PdfPath** (or a stable link/key that can be converted into a link)

Optional (nice): include a short **quoted excerpt** from the chunk.

---

## 3) Precedence & Conflict Resolution (Non-Negotiable)

### Precedence
When multiple rules address the same topic and conflict:
- **Regional overrides Quebec and Canada**
- **Quebec overrides Canada**
- If there is no override, the higher-level rule still applies.

### Canonical matching (when possible)
If two chunks share the same `RuleKey`, they refer to the “same rule concept” and should be compared by precedence.

**Effective rule selection:**
- If `RuleKey` is present:
  - choose highest precedence chunk for that `RuleKey`:
    - Regional(association) > Quebec > Canada
- If `RuleKey` is missing:
  - treat the chunk as **standalone** (cannot automatically override base rules unless admin confirms a mapping)

### Override semantics
Regional/Quebec supplements can:
- **Override** a base rule (replace or modify)
- **Append** (add extra constraints without replacing)
- **Clarify** (interpretation guidance)

The app must be conservative:
- If the system cannot confidently determine override targets, it must not hide base rules automatically.
- Confirmed mappings via admin can enforce override behavior.

### Admin-confirmed override mappings
Override mappings exist in three states:
- `Proposed` (heuristic detection)
- `Confirmed` (admin approves target `RuleKey` + relationship type)
- `Rejected`

Confirmed mappings can be applied at runtime to:
- hide overridden base rule chunks (Override relationship)
- show both base and supplement with “Additional regional rule…” (Append)
- show base rule + clarification (Clarify)

---

## 4) Language Rules

### Supported languages
- UI and responses must support **French and English**.
- Canada rulebook exists in **FR + EN**.
- Quebec and regional exist in **FR or EN or both**.

### Response language
- The response should be in the **user-selected language** based on the user preference in the UI.
- Citations may reference FR-only text even for EN responses.

### Translation policy
- Translating FR-only citations into EN prose is allowed, but:
  - the answer must still be grounded in the cited chunks
  - do not introduce new meaning not supported by the source
  - keep phrasing cautious when translating (“The regional supplement states…”)

---

## 5) “Grounded Only” Answer Policy (Chat & Search)

### Hard constraint: no external answers
- The application must **not** answer using general baseball knowledge.
- It must answer **only** using uploaded rulebooks.

### Not Found behavior
If the system cannot find a relevant rule in the indexed chunks:
- return `status = not_found`
- do not guess
- optionally suggest how to refine the question (ask for context)

### Citation requirement
If the system answers (`status = ok`):
- it must include **at least one citation**
- citations must refer to retrieved chunk IDs from the current request context

---

## 6) Search Scope Filters (User Experience + Validation)

### Global-only search
Users may search Canada/Quebec without selecting an association:
- `associationId = null`
- only `associationId == null` content is eligible

### Regional search requires association
If the user includes `Regional` scope:
- `associationId` **must** be selected
- API must reject invalid combinations (HTTP 400)

### Typical UI filter presets
- Public: Canada ✅ Quebec ✅ Regional ✅ (Regional checkbox disabled until association is chosen)
- Admin RuleKey picker: Canada ✅ Quebec ✅ Regional ❌

---

## 7) Ingestion Domain Rules (PDF → Chunks)

### Source PDFs
- Uploaded once per year, before season.
- Rarely change during season.

### Chunking expectations
Chunking must produce reasonably stable, rule-aligned chunks:
- Canada/Quebec should have high rule header detection
- Regional varies widely → chunker must be tolerant and fall back to headings/sections

### Minimal metadata per chunk
- rule number text (best effort)
- canonical rule key (best effort)
- page range
- scope level + association ID (regional only)
- language tag

### Determinism
Given the same PDF + season + scope metadata:
- chunk IDs and chunk boundaries should be stable enough to compare results between runs
- indexing should be safe to re-run (upsert with stable IDs)

---

## 8) How Precedence Applies in Retrieval (Recommended Implementation)

Given a user question:

1. Retrieve candidates across eligible scopes (based on association + scope filter).
2. Group candidates by `RuleKey` where present.
3. For each `RuleKey` group, keep the **highest precedence** chunk.
4. Add standalone regional chunks with no `RuleKey` (they might still be relevant).
5. Apply admin-confirmed override mappings:
   - if a confirmed `Override` mapping indicates “regional chunk overrides ruleKey X”:
     - prefer the regional chunk
     - optionally suppress base chunk for X
6. Provide final “effective context” to the answer generator.

---

## 9) Admin Workflow Domain Rules

### Admin-only
Admin can:
- upload PDFs
- run ingestion/index build
- review override proposals and confirm/reject
- publish a season

Public users can:
- select association
- search/chat

### Override review
The admin UI must make it easy to:
- see the source chunk (rule number + page + link)
- pick/confirm target `RuleKey` (Canada/Quebec only)
- choose relationship type: Override / Append / Clarify

---

## 10) Evaluation: What “Good” Looks Like

### Retrieval quality
- For a set of representative questions:
  - correct rule appears in top 3 results most of the time
  - citations refer to correct pages

### Chat quality (once enabled)
- Answers never hallucinate rule numbers/pages
- If wrong/uncertain: returns not_found instead of guessing

### Override correctness
- When a regional supplement or/and a Quebec rules overrides a known rule:
  - answer should cite the regional supplement or/and Quebec rule
  - base rule may be cited secondarily (Append/Clarify), or hidden for Override

---

## 11) Test Suite (20 Questions) — Use for Iteration

Use these tests to evaluate search/chat after each milestone.
For each test:
- record top hits (Search)
- record final context after precedence (Chat)
- confirm citations

> Note: some tests require you to pick at least one association where you know the supplement contains overrides.

### A) Global-only (no association selected)
1. “What happens if a batter is hit by a pitch?”
2. “When is a runner out for leaving the base early?”
3. “What is the rule for an infield fly?”
4. “Can a pitcher fake a throw to first base?”
5. “How many mound visits are allowed?” *(may vary by rule set; good not_found candidate if not in your PDFs)*

Expected:
- Results should cite **Canada and/or Quebec** only.
- If Quebec text doesn’t cover it, Canada should.

### B) Quebec vs Canada precedence (association optional)
6. Ask a question you know differs between Quebec and Canada (if applicable):
   - “In Quebec, is [X] allowed?” *(replace X with known seasonal detail)*

Expected:
- If Quebec differs and both exist, Quebec should win.

### C) Regional requires association selected
Pick `associationId = A1` where you know the supplement contains unique rules.

7. “What are the regional rules about [topic known to exist in supplement]?”
8. “Is metal cleats allowed in this association?”
9. “What is the mercy rule in this association?”
10. “What are the pitch count limits?” *(common regional add-on; adjust to your content)*

Expected:
- Regional chunks appear and are cited.
- If the supplement is silent, Quebec/Canada may apply and should be cited.

### D) Override-specific tests (requires known override example)
Pick a known overridden rule (admin confirmed mapping recommended).

11. Ask about the overridden rule by concept (not number):
    - “What is the rule for [overridden concept] in this association?”
12. Ask by rule number:
    - “Does rule 6.01(a) apply the same way in this association?”
13. Ask with both:
    - “In this association, does the regional supplement modify rule 6.01(a)?”

Expected:
- Regional citation is primary.
- Base rule may be secondary depending on Override/Append/Clarify relationship.

### E) “Not Found” tests (hallucination guard)
14. “What is the rule about the pitch clock?” *(if not in your PDFs)*
15. “What are the rules for extra innings in the playoffs?” *(if not defined)*
16. “What’s the rule for the designated hitter in MLB?” *(should be not_found unless explicitly covered)*

Expected:
- `not_found` with no citations.

### F) Bilingual behavior
17. Ask in English with Quebec-only content:
    - “In this association, are there special batting order rules?” *(if regional has this FR-only)*
18. Ask in French:
    - “Quelles sont les règles de l’association sur [topic] ?”

Expected:
- Response language matches UI selection.
- Citations remain valid (FR chunks are OK).

### G) Association scope filter behavior
19. Search with “Canada only” filter:
    - “Balk rule”
20. Search with “Regional only” filter and no association selected:
    - expected API 400 / UI prompts user to select association

---

## 12) Practical Notes for Developers

### RuleKey normalization
Keep `RuleKey` conservative:
- generate only when the pattern clearly matches canonical rule numbering (e.g., `6.01(a)(1)`)
- do not force-map regional independent numbering to RuleKey automatically

### Always prefer correctness over completeness
It’s acceptable to return not_found if:
- retrieval confidence is low
- rule references are ambiguous
- citations cannot be validated

---

## 13) Future Feature: Season Diff (Not Searchable)
If implemented later:
- generate a diff report between `seasonId` and previous season:
  - rules added/removed/changed by text hash
- store report as JSON
- do not index it into search/chat
