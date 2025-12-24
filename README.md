# RulesApp — Baseball Rule Research (Canada / Québec / Régional)

RulesApp is a bilingual (FR/EN) web application that helps players, coaches, and umpires **search and reference baseball rules** from three sources:

- **Canada** rulebook (FR + EN)
- **Québec** rulebook (FR only)
- **Regional association** supplemental rules (FR only, varies per association)

The app enforces **precedence** and **grounding**:

- Regional can override Québec/Canada
- Québec can override Canada
- The app **must not** answer outside the uploaded rulebooks
- Every answer must include **citations** (rule number + page + rulebook)

---

## Repository Structure

```
/src
  /RulesApp.Shared   # DTOs, enums, shared logic (net10.0)
  /RulesApp.Api      # Azure Functions (.NET isolated) APIs + ingestion workers (net10.0)
  /RulesApp.Web      # Blazor WebAssembly UI (net10.0)
/docs
  domain.md
  ingestion.md
  search.md
AGENTS.md
```

---

## Tech Stack

- **.NET 10** across the solution
- **Blazor WebAssembly** frontend
- **Azure Functions (isolated worker)** backend
- **Azure Storage** (Blob + Queue + Table) for files + ingestion workflow + metadata
- **Azure AI Search** for indexing & retrieval
- **Azure OpenAI** (optional, later milestone) for chat answers (RAG), with strict citation validation
- Hosting: **Azure Static Web Apps** for the UI
  - Note: SWA managed functions support HTTP triggers only, so ingestion workers run in a **separate Azure Function App** (still “Azure Functions only”).

---

## High-Level Architecture

```
       +---------------------------+
       |      RulesApp.Web         |
       |   Blazor WASM (Public)    |
       | + Admin UI (restricted)   |
       +------------+--------------+
                    |
                    | HTTPS
                    v
       +---------------------------+
       |       RulesApp.Api        |   (Azure Functions, isolated)
       |  - /api/search            |
       |  - /api/chat (later)      |
       |  - /api/admin/*           |
       |  - Queue worker ingestion |
       +------------+--------------+
                    |
        +-----------+-------------------------------+
        |                                           |
        v                                           v
+---------------------------+              +---------------------------+
|     Azure Storage         |              |      Azure AI Search      |
|  - Blob: PDFs + artifacts |              |  - rules-active index     |
|  - Queue: rules-ingest    |              |  - keyword -> hybrid      |
|  - Tables: metadata       |              +---------------------------+
+---------------------------+

                            (Optional later)
                   +---------------------------+
                   |       Azure OpenAI        |
                   |   RAG answer generation   |
                   | (strict citations only)   |
                   +---------------------------+
```

---

## Core Domain Rules (Read These First)

- **Precedence:** Regional > Québec > Canada (`docs/domain.md`)
- **Regional search requires association selection**
- **Answers must be grounded** in retrieved chunks only
- **Citations are mandatory** for “ok” answers; otherwise return `not_found`

See:
- `docs/domain.md`
- `docs/ingestion.md`
- `docs/search.md`

---

## Milestones / Implementation Plan (Incremental, Testable)

We build in vertical slices so you can evaluate quality as you go:

1. **Upload PDFs → Blob** (admin)
2. **Ingest → extract + chunk → write chunks.json** (queue worker)
3. **Debug viewer** to inspect chunks (admin)
4. **Index to Azure AI Search** (keyword search)
5. **Hybrid retrieval** (embeddings)
6. **Precedence resolver**
7. **Override proposals + admin confirm/reject**
8. **Chat (RAG)** with strict citation validation
9. **Season publish + diff report** (optional)

---

## Local Development (Quickstart)

### Prereqs
- .NET SDK **10.x**
- Azure Functions Core Tools
- Azurite (Blob + Queue + Table)
- Node.js (only if you use SWA CLI locally; optional)

### 1) Start Azurite
Example (Docker):
```bash
docker run --rm -it \
  -p 10000:10000 -p 10001:10001 -p 10002:10002 \
  mcr.microsoft.com/azure-storage/azurite
````

### 2) Configure local settings

Create `src/RulesApp.Api/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",

    "Storage:ConnectionString": "UseDevelopmentStorage=true",

    "Search:Endpoint": "https://YOUR-SEARCH.search.windows.net",
    "Search:ApiKey": "YOUR-KEY",
    "Search:IndexName": "rules-active",

    "OpenAI:Endpoint": "",
    "OpenAI:ApiKey": "",
    "OpenAI:Deployment": ""
  }
}
```

> You can leave Search/OpenAI empty until you reach those milestones.

### 3) Run the API (Functions)

From `src/RulesApp.Api`:

```bash
func start
```

### 4) Run the Web app

From `src/RulesApp.Web`:

```bash
dotnet run
```

By default, the Web app calls `/api/*` endpoints. For local dev, configure the Web project to point to your local Functions URL (e.g. `http://localhost:7071`).

---

## Configuration Reference

### Storage (required early)

- `Storage:ConnectionString`

### Azure AI Search (required for Milestone 4+)

- `Search:Endpoint`
- `Search:ApiKey`
- `Search:IndexName`

### Azure OpenAI (required for chat milestone)

- `OpenAI:Endpoint`
- `OpenAI:ApiKey`
- `OpenAI:Deployment`

---

## Admin Features

Admin-only actions:

- upload PDFs
- start ingestion/index build
- view chunk debug pages
- review override proposals (confirm/reject)
- publish season (later)

> Auth strategy depends on your deployment choice (SWA auth roles or external auth). During early milestones, you may stub admin auth locally.

---

## Data Storage (Tables & Blobs)

### Blob layout (recommended)

```
rules/{seasonId}/global/CanadaFr.pdf
rules/{seasonId}/global/CanadaEn.pdf
rules/{seasonId}/global/QuebecFr.pdf
rules/{seasonId}/{associationId}/RegionalFr.pdf

ingest/{jobId}/pages.json
ingest/{jobId}/chunks.json
```

### Tables (recommended)

- `SeasonState` — active season pointer
- `Associations` — list of associations
- `IngestionJobs` — ingestion status per association/docType
- `OverrideMappings` — proposed/confirmed overrides

---

## Evaluation & Quality

Use the test set in `docs/domain.md` to track:

- “correct rule in top 3?”
- citations correct?
- not_found when appropriate?
- override precedence behavior correct?

A simple approach:

- keep a spreadsheet and log results after each tuning iteration
- add an Admin “Test Console” page later to visualize:

  - raw hits
  - effective hits (after precedence)
  - citations used

---

## Contributing / Agent Guidance

If you use AI coding agents, read:

- `AGENTS.md` — project boundaries, allowed actions, conventions

---

## License

TBD (add your preferred license)
