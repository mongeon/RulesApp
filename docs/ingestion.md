# Ingestion & Chunking Guide — RulesApp (`docs/ingestion.md`)

This document describes how to ingest rulebook PDFs into **stable, searchable chunks** and how to iterate safely.
It focuses on *practical chunking strategies*, *evaluation*, and *tuning* for real-world PDFs.

> Principle: **Chunking quality is the #1 driver** of search and RAG quality.  
> Don’t move to embeddings/chat until chunking is “good enough”.

---

## 1) Ingestion Overview (PDF → Chunks → Index)

### Inputs
- PDF files in Blob Storage:
  - `/rules/{seasonId}/global/CanadaFr.pdf`
  - `/rules/{seasonId}/global/CanadaEn.pdf`
  - `/rules/{seasonId}/global/QuebecFr.pdf`
  - `/rules/{seasonId}/{associationId}/RegionalFr.pdf`

### Outputs
Per ingestion job (`jobId`):
- `/ingest/{jobId}/pages.json`  
  Extracted per-page text and light metadata.
- `/ingest/{jobId}/chunks.json`  
  Chunk list with citations metadata.
- (Later) indexed into Azure AI Search.

### Core processing stages
1. **Fetch PDF** (blob download)
2. **Extract text per page** (PdfPig)
3. **Normalize** (whitespace, hyphenation, bullets, headers/footers)
4. **Detect structure** (rule headers, section headers)
5. **Chunk** into rule-aligned segments
6. **Derive metadata** (rule number text, rule key, title, page range)
7. **Write artifacts** to blob for inspection
8. (Later) **Index** chunks (keyword, then embeddings)

---

## 2) Determinism & Idempotency

### Why determinism matters
- You want to re-run ingestion without creating duplicates.
- Admin “confirmed override mappings” rely on stable chunk identity.

### Recommendations
- Make `ChunkId` stable based on:
  - `seasonId`, `scopeLevel`, `associationId` (if any), `docType`, `pageStart`, and a hash of normalized chunk text
- Alternatively: use stable “anchor id” derived from:
  - rule header text + first page number + a content hash

**Example stable chunk id**
chunkId = sha1($"{seasonId}|{scope}|{assoc}|{doc}|{ruleKeyOrHeader}|{pageStart}|{textHash}")

markdown
Copy code

### Job id
Build jobId from:
- seasonId + associationId + docType + pdfETag
so re-running the same PDF can reuse the same job id (optional).

---

## 3) PdfPig Extraction Notes (Practical)

PdfPig can produce “weird” spacing and line breaks. Common issues:
- extra spaces between letters (especially from PDFs with positioned glyphs)
- broken words at line wraps (`hyphen-\nation`)
- headers/footers repeated on every page
- tables and diagrams: extracted text may be incomplete or misordered

### Suggested extraction strategy
Extract **per page** and preserve page boundaries:
- page number is a primary citation mechanism
- chunk page range is easier to compute

Store in `pages.json`:
- pageIndex (1-based page number)
- rawText
- normalizedText (after cleanup)

---

## 4) Normalization (Critical for Header Detection)

Run normalization before header detection.

### 4.1 Whitespace normalization
- collapse multiple spaces
- normalize line endings
- preserve paragraph boundaries (blank lines) when possible

### 4.2 De-hyphenation across line breaks
- detect patterns like `word-\nnext` and convert to `wordnext`
- be careful: only do it when `-` is at end-of-line and next line begins with a letter

### 4.3 Header/footer removal (optional but powerful)
If PDFs have consistent headers/footers, detect and remove:
- find lines repeated across many pages (e.g., >60% pages)
- strip them from each page

Start without this; add only if it’s clearly harming chunking.

---

## 5) Chunking Strategy by Document Type

Chunking should be tuned per source, but implemented with shared primitives:
- page segmentation
- header detection
- fallback chunking

### 5.1 Canada (FR/EN) — structured numbering (best case)
Canada rules often have recognizable rule headers like:
- `6.01(a)` or `2.00` or `Rule 6.01` (varies)
- subparts: `(a)`, `(1)`

**Goal:** chunks align to “one rule (and its subparts)” when possible.

**Header detection patterns (examples)**
- `^\s*(\d+(\.\d+)+)\s*`  (e.g., `6.01`)
- `^\s*(\d+(\.\d+)+)\s*\([a-z]\)` (e.g., `6.01(a)`)
- `^\s*Règle\s+(\d+(\.\d+)+)` (French)
- `^\s*Rule\s+(\d+(\.\d+)+)` (English)

**Chunk boundary rule**
- Start a new chunk when a rule header is detected.
- End at the next rule header (or end of document).

**Title derivation**
- Use the remainder of the header line(s) as title if present.

### 5.2 Quebec (FR) — similar structure, but FR-only
Often resembles Canada numbering, but may have:
- different headings
- extra provincial notes

Use same header patterns plus French variants.

### 5.3 Regional supplements (FR) — the hard case
Regional docs vary:
- some explicitly override rules (“Remplace la règle 6.01…”)
- some have independent numbering (e.g., “A-12”, “R-3”)
- some are bullet lists with no stable headers

**Recommended approach: layered chunking**
1) Detect explicit “section headers” (e.g., all caps lines, underlined headings, numbered headings)
2) Detect explicit rule references (Canada/Quebec style) inside a paragraph
3) Fallback to paragraph-based chunking

**Fallback chunking rules**
- Split on blank lines into paragraphs
- Merge paragraphs until chunk is between ~500 and ~1,500 characters (tunable)
- Ensure you keep page ranges correct

**Regional numbering patterns**
- `^\s*([A-Z]{1,3}-\d+)\s*` (e.g., `R-12`, `A-3`)
- `^\s*(\d+)\.\s+` (e.g., `1. ...`) for simple lists

When detected, store it as `RuleNumberText` but do not force-map to `RuleKey`.

---

## 6) RuleKey Normalization

`RuleKey` is used to match “same rule concept” across scopes.
It must be conservative and deterministic.

### 6.1 Extracting RuleKey from headers
If header matches:
- `6.01(a)(1)`  
normalize to `6.01(a)(1)` exactly (lowercase letters)

### 6.2 Extracting RuleKey from references inside text (regional)
For override detection, you may extract referenced keys like:
- “règle 6.01(a)”
- “Rule 6.01(a)”

Store these references as `ReferencedRuleKeys[]` on the chunk (optional) to support override detection.

**Do not** set the chunk’s own `RuleKey` from a reference unless the chunk is clearly “about that rule” (admin confirmation is safest).

---

## 7) Override Detection (Heuristic → Proposal)

Override detection is a separate step after chunking (still within ingestion).

### 7.1 Detect phrases (FR examples)
- “remplace la règle”
- “modifie la règle”
- “en remplacement de”
- “nonobstant la règle”
- “s’applique au lieu de”
- “ajout à la règle”
- “en plus de la règle”
- “clarification”

### 7.2 Output
Write override proposals to Table:
- `OverrideMappings`:
  - `Status = Proposed`
  - `SourceChunkId`
  - `ProposedTargetRuleKey` (if extracted)
  - `RelationType` guessed (Override/Append/Clarify)
  - `Confidence`

Admin reviews and confirms/adjusts.

---

## 8) Debug Viewer: What to Show (Admin-Only)

Implement endpoints:
- `GET /api/admin/jobs/latest?associationId=...`
- `GET /api/admin/debug/chunks?jobId=...`
- `GET /api/admin/debug/chunk?jobId=...&chunkId=...`

### Chunk list view should show
For each chunk:
- `chunkId`
- `rulebook` / `scopeLevel` / `docType` / `language`
- `RuleNumberText` and `RuleKey` (if present)
- `PageStart–PageEnd`
- `Title`
- first 200–300 chars preview
- flags:
  - `hasRuleHeaderDetected`
  - `isFallbackChunk`
  - `referencedRuleKeys` count (optional)

### Chunk detail view should show
- full normalized text
- raw per-page text around the chunk (optional)
- extracted header matches (debug panel)

---

## 9) Quality Checklist (Use After Every Chunking Change)

Run ingestion and check these metrics:

### 9.1 Canada/Quebec expected metrics
- **Rule header detection rate**: high (aim >80% of chunks)
- **Chunk size distribution**: few mega-chunks
- **Page ranges**: correct
- **Rule numbers**: mostly present and accurate

### 9.2 Regional expected metrics
- chunking is “reasonable”:
  - no single chunk swallowing the entire PDF
  - headings/sections produce separate chunks
  - paragraph fallback produces digestible chunks
- override phrases appear in chunks that contain the referenced rule keys (when they exist)

### 9.3 Red flags
- Too many chunks missing page numbers
- Many chunks with no meaningful text (noise)
- Many chunks with repeated header/footer text
- Extremely long chunks (e.g., >10k chars)
- Extremely short chunks (e.g., <100 chars) unless they’re headings

---

## 10) Tuning Playbook (Common Fixes)

### Problem: Rule headers not detected
Try:
- improve normalization (remove extra whitespace)
- add header regex variants
- inspect raw extracted lines around the header
- detect headers by “line starts with digit pattern” rather than full regex match

### Problem: Many mega-chunks
Try:
- reduce reliance on “next header only”
- add secondary boundary rules (e.g., major section headings)
- enable paragraph fallback when header detection fails for long spans

### Problem: Repeated header/footer polluting chunks
Try:
- detect repeated lines across pages and strip them
- strip page numbers lines if they are consistent and not part of content

### Problem: Regional supplements messy
Try:
- detect all-caps headings or underline separators
- implement paragraph-merge chunking with target size window
- preserve list items as separate paragraphs, then merge

---

## 11) Recommended Parameter Defaults (Start Here)

These are starting points—adjust based on real PDFs.

- Paragraph merge target:
  - `minChars = 500`
  - `maxChars = 1500`
- Hard cap:
  - `maxCharsHard = 3500` (force split)
- Header detection:
  - run on normalized lines
  - keep original lines for display/debug

---

## 12) Minimal Data Structures (Reference)

### `pages.json` example
```json
{
  "jobId": "2026:ABC:RegionalFr:etag123",
  "pages": [
    { "page": 1, "raw": "...", "normalized": "..." },
    { "page": 2, "raw": "...", "normalized": "..." }
  ]
}
```

### `chunks.json` example
```json
Copy code
{
  "jobId": "2026:ABC:RegionalFr:etag123",
  "chunks": [
    {
      "chunkId": "sha1...",
      "scopeLevel": "Regional",
      "associationId": "ABC",
      "rulebook": "Regional:ABC",
      "language": "fr",
      "docType": "RegionalFr",
      "title": "Équipement",
      "ruleNumberText": "R-12",
      "ruleKey": null,
      "pageStart": 3,
      "pageEnd": 3,
      "text": "Les crampons métalliques sont..."
    }
  ]
}
13) When to Move On (Gate to Next Milestone)
Proceed to Azure AI Search indexing only when:

- Canada/Quebec chunking is clean and mostly rule-aligned
- Regional chunking is at least split into reasonable sections/paragraph chunks
- citations (page ranges) look correct in the debug viewer

Proceed to embeddings/hybrid only after keyword search is reasonable.

Proceed to chat only after retrieval + precedence is trustworthy.

