````markdown
# Search & Retrieval Guide — RulesApp (`docs/search.md`)

This document defines how RulesApp performs **search and retrieval** over rulebook chunks, including:
- index schema expectations (Azure AI Search)
- filtering rules (season, association, scope)
- ranking strategy (keyword → hybrid vector)
- precedence resolution (Regional > Quebec > Canada)
- evaluation checklist and tuning playbook

> Principle: Retrieval must be **correct + citeable**.  
> If we can’t produce reliable citations, we must return **not_found**.

---

## 1) Retrieval Goals (Non-Negotiable)

### Must-haves
- Support natural language queries (users don’t know rule numbers).
- Support bilingual usage (FR/EN), while some sources are FR-only.
- Always return **citations**: rulebook + rule number (if present) + page(s).
- Never answer outside indexed rulebook content.

### Good-to-haves
- Scope filters: Canada / Quebec / Regional (regional requires association).
- “Open PDF at page” links.

---

## 2) Data Model Requirements (What Search Documents Must Contain)

Each searchable document represents a **chunk**.

Minimum required fields:
- `id` (string) — stable chunk ID
- `seasonId` (string) — filterable
- `associationId` (string|null) — null for global (Canada/Quebec), associationId for regional
- `scopeLevel` (string) — `Canada|Quebec|Regional` (filterable)
- `rulebook` (string) — `Canada|Quebec|Regional:{associationId}`
- `language` (string) — `fr|en` (filterable)
- `ruleNumberText` (string|null)
- `ruleKey` (string|null) — canonical key like `6.01(a)` when available
- `title` (string|null)
- `pageStart` (int), `pageEnd` (int)
- `pdfPath` (string) — blob path (or stable locator)
- `content` (string) — normalized chunk text

Optional but recommended:
- `contentFr` / `contentEn` (two fields) if you want one doc per rule with bilingual fields  
  OR store two docs per rule chunk (one per language). Choose one approach and stay consistent.
- `referencedRuleKeys` (collection of strings) — extracted references found in the text
- `overridesRuleKeys` (collection of strings) — confirmed override targets (from admin)
- `contentHash` (string) — supports diffing and idempotent re-index
- `sourceJobId` (string) — debug traceability

---

## 3) Index Strategy (Simplest First)

### Recommended index naming
Start with **one index**:
- `rules-active`

Later (optional):
- `rules-staging` during ingestion, then swap active alias/setting on publish.

For easiest initial results:
- index straight into `rules-active` during ingestion
- keep `SeasonState.ActiveSeasonId` as the authoritative season filter

---

## 4) Filtering Rules (Season, Association, Scope)

### 4.1 Active Season Filter (always applied)
Every query must include:
- `seasonId == ActiveSeasonId`

### 4.2 Association Filter (depends on user selection)
Case A — user selected an association `A`:
- eligible docs:
  - global docs: `associationId == null` (Canada/Quebec)
  - regional docs: `associationId == A`

Filter:
- `(associationId eq 'A' or associationId eq null)`

Case B — no association selected:
- eligible docs:
  - global docs only: `associationId == null`

Filter:
- `associationId eq null`

### 4.3 Scope filter (Canada / Quebec / Regional)
User can choose scope levels:
- Canada
- Quebec
- Regional

**Regional requires association selection.**
If request includes `Regional` and `AssociationId` is null:
- API returns HTTP 400
- UI prompts the user to select an association

Scope filter expression example:
- `(scopeLevel eq 'Canada' or scopeLevel eq 'Quebec')`
- `(scopeLevel eq 'Regional')` (only when association is present)

### 4.4 Language filter
Response language is a UI preference. Retrieval can be:
- Strict: return only `language == userLanguage`
- Flexible: return preferred language first, fall back to FR if EN missing

Recommended:
- If user language is EN:
  - retrieve EN + FR, but rank EN higher (e.g., in post-processing)
  - because Quebec/Regional are FR-only
- If user language is FR:
  - retrieve FR only (or FR + EN if desired)

---

## 5) Query Pipeline (Keyword First → Hybrid Later)

### Stage 1: Keyword / lexical search
Use Azure AI Search “search” query:
- query text: user natural language
- searchable fields: `title`, `content`, `ruleNumberText`, `ruleKey`
- return topK hits with required metadata for citations

This is enough to ship a first version.

### Stage 2: Hybrid search (keyword + vector embeddings)
Add:
- `contentVector` field
- embed chunks during ingestion
- embed query at request time
- hybrid scoring improves natural language matching

Do hybrid only after chunking is decent.

---

## 6) Search API Contract (Recommended)

### `POST /api/search`
Request DTO (example):
```json
{
  "associationId": "ABC",
  "language": "fr",
  "query": "Est-ce que les crampons métalliques sont permis?",
  "scopeLevels": ["Canada", "Quebec", "Regional"]
}
````

Response DTO (example):

```json
{
  "hits": [
    {
      "chunkId": "sha1...",
      "score": 12.34,
      "scopeLevel": "Regional",
      "rulebook": "Regional:ABC",
      "language": "fr",
      "ruleNumberText": "R-12",
      "ruleKey": null,
      "title": "Équipement",
      "pageStart": 3,
      "pageEnd": 3,
      "pdfPath": "rules/2026/ABC/RegionalFr.pdf",
      "snippet": "Les crampons métalliques sont ..."
    }
  ]
}
```

### `POST /api/chat`

Chat should internally call the same retrieval logic but then:

* apply precedence
* optionally apply override mappings
* produce an answer with citations
* validate citations against the retrieved context

---

## 7) Ranking & Post-processing

### 7.1 Basic ranking (keyword-only)

Rely on AI Search ranking and tune:

* fields boost (title > ruleKey > content)
* `searchMode = any` initially
* use filter (season + association + scope)

### 7.2 Enrichment in post-processing (recommended)

Even before embeddings, add a small post-ranker:

* If query contains a rule number or ruleKey:

  * boost docs whose `ruleKey` exactly matches
* If user language is EN:

  * prefer `language == 'en'` docs if available

### 7.3 Snippet generation

Return a short `snippet` for UI:

* use first 200–300 chars of content
* or implement basic highlight (optional)

---

## 8) Precedence Resolution (Effective Context)

Precedence is applied after retrieval:

* group results by `ruleKey` where present
* for each group keep highest precedence:

  * Regional (selected association) > Quebec > Canada

Standalone chunks (no ruleKey):

* keep if relevant (don’t auto-override)

Confirmed override mappings:

* if a regional chunk has confirmed `Override` mapping to `ruleKey X`:

  * prefer regional chunk, optionally suppress base chunk for X
* if `Append` or `Clarify`:

  * include both (regional first), annotate relationship in chat response

> Search endpoint may optionally provide both raw hits and “effective hits”, but chat must use effective context.

---

## 9) Azure AI Search Schema (Suggested)

### Core fields

* `id` (key)
* `seasonId` (filterable)
* `associationId` (filterable)
* `scopeLevel` (filterable, facetable)
* `rulebook` (filterable)
* `language` (filterable, facetable)
* `ruleKey` (filterable)
* `ruleNumberText` (searchable)
* `title` (searchable)
* `content` (searchable)
* `pageStart` / `pageEnd` (filterable)
* `pdfPath` (filterable)
* `overridesRuleKeys` (collection, filterable optional)

### Analyzer guidance (simple)

* If you store per-language documents:

  * set `content` analyzer based on `language` at index time (harder)
  * simplest: use one analyzer that works “okay” for both (less ideal)
* Practical approach:

  * store separate fields `content_fr` and `content_en`
  * query the relevant field(s) based on UI language

Start simple:

* two content fields: `content_fr`, `content_en`
* for FR queries: searchFields = `title`, `ruleKey`, `ruleNumberText`, `content_fr`
* for EN queries: include `content_en` and optionally `content_fr` fallback

---

## 10) Hybrid Search (When You’re Ready)

### Add vector field

* `contentVector` (collection of floats, searchable vector)
* store embeddings for each chunk

### Hybrid query behavior

* Compute query embedding
* Combine lexical + vector search
* Return topK documents

### What to embed

For each chunk, embed:

* `title + "\n" + first N chars of content` (N = 1000–1500)
* keep stable normalization so embeddings are stable

---

## 11) Evaluation Checklist (Use Every Iteration)

Create a small “20-question test set” (see `docs/domain.md`).

For each question:

* record top 3 search hits
* check:

  * correct rule present?
  * citations correct (page/rulebook)?
  * language behavior correct?
* track:

  * `Top3ContainsCorrectRule` (Y/N)
  * `CitationCorrect` (Y/N)
  * `NeedOverrideMapping` (Y/N)
  * notes

### Gate to move on

* keyword search acceptable before embeddings
* hybrid acceptable before chat

---

## 12) Tuning Playbook (Common Retrieval Issues)

### Problem: Wrong rule returned (topic is broad)

Try:

* reduce chunk size (more precise chunks)
* add title extraction
* increase `title` boosting

### Problem: Correct rule exists but not in top results

Try:

* improve chunk text normalization
* add synonyms or domain terms (optional)
* add embeddings (hybrid)

### Problem: Regional content not showing up

Check:

* association filter is correct:

  * association selected: include `(assoc OR null)`
  * association missing: only `null`
* scope filter includes `Regional`
* regional docs indexed with correct `associationId`

### Problem: EN user gets FR-only results too often

Try:

* prefer EN field in searchFields
* post-process: if EN hits exist, rank them above FR hits
* keep FR fallback for Quebec/regional sources

### Problem: Too many irrelevant results for common words

Try:

* add field boosts and reduce reliance on content-only scoring
* add a basic stopword strategy via analyzers (later)
* add phrase boosting for rule number patterns

---

## 13) Debugging & Observability (Required)

Log on every search request:

* `seasonId`, `associationId`, `scopeLevels`, `language`
* applied filter string
* query text
* topK returned ids + scores
* count per scopeLevel

In admin “Test Console” page show:

* raw hits (before precedence)
* effective hits (after precedence)
* which items got suppressed due to overrides

---

## 14) Implementation Notes for .NET (Azure SDK)

### Keep retrieval logic in one place

* `SearchService.SearchAsync(...)` used by:

  * `/api/search`
  * `/api/chat` (as first stage)

### Keep filter construction unit-tested

* association scope rules are easy to break
* add unit tests for filter builder:

  * no association + regional -> invalid
  * association + regional -> ok
  * scopeLevels empty -> defaults (Canada+Quebec+Regional when association present)

