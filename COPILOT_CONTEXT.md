# COPILOT_CONTEXT.md — RulesApp (Authoritative Context)

This file is the authoritative project context for AI coding agents (Copilot Agent, etc.).
If any instructions conflict, follow this file.

## Project Summary
RulesApp is a bilingual (FR/EN) web app for searching baseball rules from:
- Canada rulebook (FR + EN)
- Québec rulebook (FR only)
- Regional association supplements (FR only, varies by association)

Users ask in natural language; the app returns answers ONLY from uploaded PDFs and MUST include citations:
- rule number (RuleNumberText and/or RuleKey)
- page number(s)
- rulebook (Canada / Québec / Regional:{associationId})

Non-negotiable:
- Regional can override Québec and Canada
- Québec can override Canada
- If not found in sources -> respond NOT_FOUND (no guessing)
- Regional search requires selecting an association

## Tech Constraints
- .NET 10 solution overall. (If Functions tooling doesn’t fully support net10.0 yet, API may temporarily target net8.0 to keep moving.)
- Frontend: Blazor WebAssembly (`src/RulesApp.Web`)
- Backend: Azure Functions isolated (`src/RulesApp.Api`)
- Shared DTOs: .NET class library (`src/RulesApp.Shared`)
- Hosting: Azure Static Web Apps for UI; ingestion worker must run in standalone Function App (Queue/Timer triggers)
- Storage: Azure Blob + Queue + Table
- Search: Azure AI Search (keyword first, hybrid embeddings later)
- Optional later: Azure OpenAI for chat RAG (strict citation validation)

## Current Repo Notes / Required Fixes
1) Remove absolute path from `src/RulesApp.Api/RulesApp.Api.csproj`:
   - There is an ItemGroup removing `.editorconfig` using an absolute Windows path.
   - Delete that ItemGroup entirely.
2) Normalize file casing:
   - Rename `Agents.md` -> `AGENTS.md`
   - Rename `docs/Ingestion.md` -> `docs/ingestion.md`
   - Update README references accordingly
3) Add a classic `RulesApp.sln` (keep `.slnx` if you want, but CI should build the `.sln`)
4) Add project references:
   - `RulesApp.Api` references `RulesApp.Shared`
   - `RulesApp.Web` references `RulesApp.Shared`
5) Do not commit secrets. Use local.settings.json (ignored) and app settings in Azure.

## Domain Model (Core Rules)
See `/docs/domain.md` for full rules. Summary:
- ScopeLevel precedence: Regional > Quebec > Canada
- associationId:
  - global docs have associationId = null
  - regional docs have associationId = selected associationId
- Search scope filter:
  - if user selects Regional => associationId required, else reject
- Always cite pages + rulebook

## Blob Layout (Recommended)
PDFs:
- rules/{seasonId}/global/CanadaFr.pdf
- rules/{seasonId}/global/CanadaEn.pdf
- rules/{seasonId}/global/QuebecFr.pdf
- rules/{seasonId}/{associationId}/RegionalFr.pdf

Ingestion artifacts:
- ingest/{jobId}/pages.json
- ingest/{jobId}/chunks.json

## Tables (Azure Table Storage)
- SeasonState
  - PK="SEASON", RK="ACTIVE": { ActiveSeasonId, PreviousSeasonId? }
- Associations
  - list of associations (10–25)
- IngestionJobs
  - PK="{seasonId}:{associationId}", RK="{jobId}" (or sortable timestamp RK)
  - status, docType, startedAt, completedAt, error, counts
- OverrideMappings
  - PK="{seasonId}:{associationId}"
  - RK="Proposed:{overrideId}" or "Confirmed:{overrideId}" or "Rejected:{overrideId}"
  - stores mapping of a regional/quebec chunk to a target RuleKey + relation type

## Minimal API Endpoints (Milestone 1)
Admin:
- POST /api/admin/upload  (multipart form-data -> blob)
- POST /api/admin/build?associationId=ABC (enqueue ingestion jobs)
- GET  /api/admin/jobs/latest?associationId=ABC
- GET  /api/admin/debug/chunks?jobId=...
- GET  /api/admin/debug/chunk?jobId=...&chunkId=...

Public:
- POST /api/search (returns hits with citations) [Milestone 2+]
- POST /api/chat  (RAG with strict citations)   [Milestone 4+]

## Ingestion (Milestone 1)
Queue: rules-ingest
Queue message:
- seasonId
- associationId (nullable for globals; but for simplicity include "GLOBAL" or use null)
- docType: CanadaFr|CanadaEn|QuebecFr|RegionalFr
- scopeLevel: Canada|Quebec|Regional
- language: fr|en
- pdfBlobPath

Worker:
1) Download PDF from blob
2) PdfPig extract per page -> pages.json
3) Normalize text
4) Chunk into rule-aligned chunks (rule header regex + fallback paragraph chunking)
5) Write chunks.json with citations metadata

Chunk DTO (minimum):
- chunkId, scopeLevel, associationId?, rulebook, language
- ruleNumberText?, ruleKey?, title?
- pageStart, pageEnd, pdfPath
- text (normalized)

## Search (Milestone 2)
Azure AI Search index: rules-active
Filters:
- always seasonId == activeSeasonId
- if associationId selected: (associationId == selected OR associationId == null)
- if no associationId: associationId == null
- scopeLevels optional: Canada/Quebec/Regional
- if scopeLevels contains Regional -> associationId must be set

## Strict Grounding (Milestone 4 Chat)
Chat MUST:
- retrieve chunks
- generate answer constrained to those chunks
- validate citations refer only to retrieved chunkIds
- else return not_found

## Coding Standards
- Keep it simple; low cost, low ops
- Prefer deterministic IDs and idempotent ingestion
- Keep logic in services; functions should be thin
- Add unit tests for:
  - filter builder
  - precedence resolver
  - chunker (basic)
