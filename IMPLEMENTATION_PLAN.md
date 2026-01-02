# IMPLEMENTATION_PLAN.md — Step-by-step (Testable Milestones)

This plan is designed for incremental progress with constant evaluation.

## Milestone 0 — Repo cleanup (must do first)
Goal: cross-platform build + consistent casing.

Tasks:
- [ ] Remove absolute Windows `.editorconfig` removal entry from `RulesApp.Api.csproj`
- [ ] Rename `Agents.md` -> `AGENTS.md`
- [ ] Rename `docs/Ingestion.md` -> `docs/ingestion.md`
- [ ] Fix README references (AGENTS.md + docs path)
- [ ] Add classic `RulesApp.sln` (keep .slnx optional)
- [ ] Add project references: Api->Shared, Web->Shared
- [ ] Ensure `dotnet build` works from repo root

Acceptance:
- `dotnet build` succeeds on Windows + Linux
- no absolute paths in csproj

---

## Milestone 1 — Upload + Ingestion artifacts (no AI Search yet)
Goal: upload PDFs -> run ingestion -> view chunks in admin debug UI.

### M1.1 Storage clients + config
- [ ] Add packages to Api:
  - Azure.Storage.Blobs
  - Azure.Storage.Queues
  - Azure.Data.Tables
  - UglyToad.PdfPig
- [ ] Add DI in Api Program.cs:
  - BlobServiceClient, QueueServiceClient, TableServiceClient
- [ ] Create wrappers:
  - BlobStore: Get/Put blobs
  - QueueStore: Enqueue/Dequeue messages
  - TableStore: read/write entities

### M1.2 Admin upload endpoint
- [ ] POST /api/admin/upload
  - multipart form-data: seasonId (optional), associationId, docType, file
  - validate: if docType=RegionalFr => associationId required
  - write to blob path using BlobPaths helper

### M1.3 Build endpoint (enqueue jobs)
- [ ] POST /api/admin/build?associationId=ABC
  - reads activeSeasonId from SeasonState
  - enqueues messages for:
    - CanadaFr, CanadaEn, QuebecFr (global)
    - RegionalFr (association)
  - create IngestionJobs rows with status=Queued

### M1.4 Queue worker
- [ ] Queue trigger `RulesIngestWorker`
  - download PDF
  - PdfPig extract pages
  - normalize + chunk
  - write pages.json + chunks.json
  - update IngestionJobs row with counts + status=Completed/Failed

### M1.5 Debug endpoints
- [ ] GET /api/admin/jobs/latest?associationId=ABC
- [ ] GET /api/admin/debug/chunks?jobId=...
- [ ] GET /api/admin/debug/chunk?jobId=...&chunkId=...

Acceptance:
- Upload a PDF locally
- Build triggers worker
- chunks.json exists and debug endpoint returns chunks with page numbers and previews

---

## Milestone 2 — AI Search keyword retrieval
Goal: index chunks and search with citations.

- [x] Define AI Search schema (JSON in repo under /infra/search/)
- [x] Add Azure.Search.Documents package to Api
- [x] Add search DTOs to Shared/Models.cs
- [x] Create SearchStore service with indexing and search capabilities
- [x] Register SearchStore in Program.cs DI
- [x] Add indexing step in worker: upsert chunks into rules-active
- [x] POST /api/search returns top hits with citations
- [x] GET /api/admin/search-stats returns index statistics
- [x] POST /api/admin/search-index creates/updates index
- [x] Web page shows:
  - query box
  - scope toggles (Canada/Quebec/Regional)
  - association selector required when Regional selected
- [x] Update infrastructure (main.bicep) with Azure AI Search resource

Acceptance:
- 20 test questions from docs/domain.md show correct rule in top 3 most of the time

Testing:
- See [docs/testing-milestone2.md](docs/testing-milestone2.md) for comprehensive testing guide

---

## Milestone 3 — Precedence + override proposals
Goal: effective context and admin mapping.

- [ ] Implement PrecedenceResolver
- [ ] Implement heuristic override detection -> proposals into OverrideMappings (Proposed)
- [ ] Admin UI to confirm/reject and pick target RuleKey using RuleKeyPicker
- [ ] Apply confirmed overrides in precedence stage

Acceptance:
- Confirmed override changes which chunk is primary for a ruleKey

---

## Milestone 4 — Chat (RAG) with strict citations
Goal: grounded answers only.

- [ ] POST /api/chat
  - retrieves topK candidates
  - precedence collapse
  - calls Azure OpenAI (optional)
  - validates citations refer to retrieved chunkIds
  - returns ok or not_found
3) Prompt snippets to use with Copilot Agent (copy/paste)
A) “Bootstrap milestone 0”
sql
Copy code
Follow COPILOT_CONTEXT.md and IMPLEMENTATION_PLAN.md.

Implement Milestone 0 only:
- remove absolute path from RulesApp.Api.csproj
- rename Agents.md -> AGENTS.md
- rename docs/Ingestion.md -> docs/ingestion.md
- fix README references
- add RulesApp.sln and add all projects
- add project references: Api->Shared, Web->Shared
- ensure dotnet build succeeds from repo root

Do not implement ingestion yet.
B) “Implement Milestone 1 (upload + ingestion + debug endpoints)”
vbnet
Copy code
Follow COPILOT_CONTEXT.md and IMPLEMENTATION_PLAN.md.

Implement Milestone 1 end-to-end:
- Storage clients + DI
- POST /api/admin/upload
- POST /api/admin/build?associationId=...
- Queue-trigger worker that generates pages.json and chunks.json
- Debug endpoints to list chunks and show chunk text

Keep functions thin and put logic into Services.
Add minimal DTOs to RulesApp.Shared.
Make ingestion idempotent (stable chunkIds).
C) “Add search keyword-only (Milestone 2)”
pgsql
Copy code
Implement Milestone 2 (keyword-only Azure AI Search):
- create /infra/search/rules-active.index.json
- add indexer/upsert in ingestion worker
- implement POST /api/search with season + association + scope filters
- update Web UI with scope toggles and association gating for Regional

Do not add embeddings or chat yet.