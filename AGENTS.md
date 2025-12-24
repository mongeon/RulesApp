# AGENTS.md

This file guides AI coding agents (Copilot, GPT CLI tools, etc.) working on the **RulesApp** repository.
It defines context, expectations, and boundaries so an agent can help effectively and safely.

## TL;DR (How to help best)
- Work across **all three projects**: Shared, Api, Web.
- Respect domain rules: **precedence** (Regional > Quebec > Canada), **bilingual UX** (FR/EN), and **strict grounding** (answers must be backed by rulebook citations).
- Keep solutions **simple** and **cost-conscious** (low traffic, small user base).
- Use **.NET 10 (net10.0)** across the repo.
- Azure: **code/IaC only**. Never touch live Azure resources directly.

---

## Solution Structure

- `src/RulesApp.Shared`
  - Shared DTOs, enums, validation helpers, small domain utilities.
- `src/RulesApp.Api`
  - Azure Functions backend (.NET isolated worker), ingestion, search APIs, admin APIs.
- `src/RulesApp.Web`
  - Blazor WebAssembly frontend (public UI + admin UI).

### .NET Version Policy
- Target **.NET 10** (net10.0) for all projects.
- .NET 10 is GA (released Nov 11, 2025).  
  See: .NET Blog announcement and .NET release notes.  
  (For references: https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/ and dotnet/core release notes.)

### Azure Functions Model
- Use the **isolated worker model**.
- Azure Functions runtime **v4** supports .NET versions including **.NET 10** in isolated worker apps.  
  See Microsoft docs: .NET isolated process guide / runtime versions.  
  (References: https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide and https://learn.microsoft.com/azure/azure-functions/functions-versions)

### Static Web Apps Integration Note (Important)
- SWA "managed functions" only support **HTTP triggers**.
- Our ingestion requires Queue/Timer triggers; therefore the API must be a **standalone Azure Function App** (still Azure Functions), deployed separately and optionally connected as a backend (“bring your own”).  
  See Microsoft docs: add API / APIs with Functions.  
  (References: https://learn.microsoft.com/azure/static-web-apps/add-api and https://learn.microsoft.com/azure/static-web-apps/apis-functions)

---

## Domain Concepts (Must Understand)

### Rulebooks & Scope Levels
- **Canada**: FR + EN
- **Quebec**: FR only
- **Regional Association**: FR only, many associations, varying formats

### Precedence / Overrides
- Regional rules can override Quebec/Canada
- Quebec rules can override Canada
- The application must not invent rules: answers must be grounded in uploaded rulebooks only.

### Required Answer Format
- Always provide citations:
  - rule number (if present)
  - page number(s)
  - rulebook identifier (Canada/Quebec/Regional + season)
- If not found, answer must be “not found in the provided rulebooks” (no speculation).

### Association Context
- Users may select an association.
- If searching “Regional” scope, association selection is required (regional docs are association-scoped).

### Seasonal Content
- Only current season is searchable.
- Ingestion happens once/year; changes mid-season are rare.
- Optional future: season-to-season diff reporting (not indexed).

---

## Allowed Actions (Agent Permissions)

✅ Allowed (within repository)
- Create/modify/delete C# code, Razor components, JSON files, configuration, and infrastructure-as-code files.
- Implement ingestion pipeline:
  - PDF parsing (PdfPig)
  - chunking
  - indexing to Azure AI Search
  - override detection + admin review workflows
- Implement search/retrieval logic (keyword + hybrid, precedence collapse).
- Implement admin endpoints and UI.
- Add tests and small tooling scripts.

❌ Not allowed
- No direct Azure resource manipulation:
  - No portal actions, no `az` commands that create/modify live services, no editing live indexes/tables manually.
  - Only generate code / IaC that a human will deploy.
- Never commit secrets or credentials.
- No expensive architecture changes without explicit approval.

⚠️ Ask before doing
- Major schema/index changes (AI Search fields, partition keys, etc.).
- Adding new paid services or dependencies that may increase cost/ops burden.
- Big architectural refactors.

---

## Implementation Principles

### 1) Vertical slices with measurable outputs
Prefer milestones that produce something testable:
1. upload -> blob stored
2. ingest -> chunks.json output
3. debug viewer -> inspect chunks
4. index -> searchable results with citations
5. precedence -> correct “effective rule”
6. overrides admin -> confirm/reject mapping
7. chat -> strict citations and grounding

### 2) Observability built-in
Log (at minimum) for ingestion and chat:
- jobId, associationId, docType, seasonId
- chunk counts, rule-number detection rate
- indexing counts/failures
- retrieval topK, selected context IDs
- validation failures (e.g., citation not in context)

### 3) Keep it simple
- Prefer Table Storage/Blob/Queue and straightforward Function triggers.
- Avoid agentic orchestration frameworks unless explicitly requested.

---

## Coding Guidelines

### C# / .NET
- TargetFramework: `net10.0`
- Favor explicit DTOs in `RulesApp.Shared`
- Keep functions small and composable:
  - `BlobStore`, `TableStore`, `QueueClient`, `SearchClient` wrappers
  - `Chunker`, `OverrideDetector`, `PrecedenceResolver`, `CitationValidator`

### Pdf Processing
- Use **UglyToad.PdfPig** for extraction.
- Always preserve:
  - source PDF path
  - page start/end
  - any detected rule number text / headings
- Chunking must be inspectable via debug endpoints.

### Search
- Always filter by:
  - active season
  - association scope rules:
    - if associationId present: (associationId == selected OR associationId == null)
    - if associationId absent: associationId == null (global only)
  - scope levels (Canada/Quebec/Regional) optional filter

### Admin-Only Operations
- Document upload, ingestion, override confirmation are admin-only.
- Public users can search/chat, but cannot upload.

---

## Local Dev Expectations
- Local storage emulators (Azurite) are used for blobs/queues/tables.
- Functions should run locally with Functions Core Tools.
- Agents should keep local run steps updated in README where relevant.

---

## Output Quality Requirements (Especially for Chat)
- Never answer outside indexed rule content.
- Always include citations (rule number + page + rulebook).
- If retrieval yields no sufficient evidence: return a "not found" response.
- All citations must refer to retrieved chunks (validate IDs).

---

## Where to Put More Domain Info
- Put deeper domain rules, example questions, and test cases in:
  - `README.md` (top-level)
  - `docs/` folder (recommended):
    - `docs/domain.md` (precedence + examples)
    - `docs/ingestion.md` (chunking rules, thresholds)
    - `docs/search.md` (filters, scoring, evaluation checklist)
