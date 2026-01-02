# Testing Milestone 2 — Azure AI Search Keyword Retrieval

This guide walks through testing the Azure AI Search integration with keyword search and citation retrieval.

## Prerequisites

1. **Azure AI Search resource** (local or cloud)
   - For local dev: Use Azure AI Search emulator or cloud service free tier
   - Update `local.settings.json` with search endpoint and admin key
2. **Azurite running** (local storage emulator)
3. **Functions running** (`func start` in `src/RulesApp.Api`)
4. **Milestone 1 completed** (PDF uploaded and ingested with chunks created)

## Configuration

### local.settings.json
```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "Storage:ConnectionString": "UseDevelopmentStorage=true",
    "Search:Endpoint": "https://YOUR-SEARCH.search.windows.net",
    "Search:AdminKey": "YOUR-ADMIN-KEY",
    "Search:IndexName": "rules-active"
  }
}
```

## Test Steps

### 1. Verify Index Schema Exists

Check that the index definition is in the repo:
```powershell
Get-Content infra/search/rules-active.index.json
```

Expected: JSON schema with fields like chunkId, seasonId, associationId, scope, docType, ruleKey, text, etc.

### 2. Create/Update Index (Initial Setup)

If using a new search service, create the index:
```powershell
curl.exe -X POST http://localhost:7071/api/admin/index/create `
  -H "x-functions-key: admin"
```

Expected response:
```json
{
  "message": "Index created/updated successfully",
  "indexName": "rules-active",
  "fieldCount": 12
}
```

### 3. Index Existing Chunks (Backfill)

Trigger re-indexing of previously ingested documents:
```powershell
curl.exe -X POST http://localhost:7071/api/admin/build
```

This should:
- Re-process existing blobs
- Generate chunks (if not cached)
- **Upload chunks to Azure AI Search**

### 4. Verify Indexed Documents

Check indexed document count:
```powershell
curl.exe "http://localhost:7071/api/admin/index/stats"
```

Expected response:
```json
{
  "indexName": "rules-active",
  "documentCount": 50,
  "storageSize": 256000
}
```

### 5. Test Basic Keyword Search

#### Test 1: Simple query (all scopes)
```powershell
# Option 1: Using Invoke-RestMethod (recommended for PowerShell)
$body = @{
    query = "terrain de jeu"
    top = 5
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/search" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body

# Option 2: Using curl.exe with escaped quotes
curl.exe -X POST "http://localhost:7071/api/search" `
  -H "Content-Type: application/json" `
  --data-raw '{\"query\": \"terrain de jeu\", \"top\": 5}'
```

Expected response:
```json
{
  "query": "terrain de jeu",
  "totalResults": 3,
  "results": [
    {
      "chunkId": "abc123...",
      "ruleKey": "RULE_1_04",
      "ruleNumberText": "1.04",
      "title": "Règle 1.04 – Le terrain",
      "scope": "Canada",
      "docType": "CanadaFr",
      "seasonId": "2025",
      "associationId": null,
      "pageStart": 8,
      "pageEnd": 9,
      "textPreview": "Le terrain de jeu doit être...",
      "score": 8.45
    }
  ]
}
```

#### Test 2: Scope filter (Quebec only)
```powershell
curl.exe -X POST http://localhost:7071/api/search `
  -H "Content-Type: application/json" `
  -d '{"query": "frappeur", "scopes": ["Quebec"], "top": 3}'
```

Expected: Only results from QuebecFr documents.

#### Test 3: Regional scope with association
```powershell
curl.exe -X POST http://localhost:7071/api/search `
  -H "Content-Type: application/json" `
  -d '{"query": "match", "scopes": ["Regional"], "associationId": "LBJEQ", "top": 5}'
```

Expected: Results from:
- RegionalFr documents for LBJEQ association
- Quebec and Canada fallback rules (if precedence is implemented)

#### Test 4: Association context without Regional scope
```powershell
curl.exe -X POST http://localhost:7071/api/search `
  -H "Content-Type: application/json" `
  -d '{"query": "arbitre", "associationId": "LBJEQ", "scopes": ["Canada", "Quebec"], "top": 3}'
```

Expected: Results from Canada and Quebec only (Regional excluded from scopes).

### 6. Test Season Filtering

Upload a document for a different season (if you have one), then search:
```powershell
curl.exe -X POST http://localhost:7071/api/search `
  -H "Content-Type: application/json" `
  -d '{"query": "terrain", "seasonId": "2024", "top": 3}'
```

Expected: Only results from 2024 season.

### 7. Test Citation Completeness

For each result, verify:
- ✅ `ruleKey` or `ruleNumberText` present
- ✅ `pageStart` and `pageEnd` present
- ✅ `docType` and `scope` identify source rulebook
- ✅ `textPreview` provides context

### 8. Test Edge Cases

#### Empty query
```powershell
curl.exe -X POST http://localhost:7071/api/search `
  -H "Content-Type: application/json" `
  -d '{"query": "", "top": 5}'
```

Expected: HTTP 400 Bad Request

#### No results
```powershell
curl.exe -X POST http://localhost:7071/api/search `
  -H "Content-Type: application/json" `
  -d '{"query": "xyznonexistentterm123", "top": 5}'
```

Expected:
```json
{
  "query": "xyznonexistentterm123",
  "totalResults": 0,
  "results": []
}
```

#### Regional without associationId
```powershell
curl.exe -X POST http://localhost:7071/api/search `
  -H "Content-Type: application/json" `
  -d '{"query": "match", "scopes": ["Regional"], "top": 5}'
```

Expected: HTTP 400 Bad Request (Regional scope requires associationId)

### 9. Test Evaluation Questions (from docs/domain.md)

Run 20 test questions and verify:
- Correct rule appears in top 3 results
- Citations are accurate
- No invented rules or hallucinations

Example questions:
1. "Combien de joueurs dans une équipe?"
2. "Quelle est la distance entre les buts?"
3. "Qu'est-ce qu'un match réglementaire?"
4. "Règles sur les batons de baseball"
5. "Uniforme obligatoire pour les joueurs"

Track success rate in a spreadsheet or test results file.

### 10. Test Web UI (if implemented)

1. Navigate to Web app (e.g., http://localhost:5000)
2. Go to Search page
3. Enter query
4. Toggle scopes (Canada/Quebec/Regional)
5. Select association (when Regional enabled)
6. Submit search
7. Verify:
   - Results display with rule numbers
   - Page numbers shown
   - Source rulebook identified
   - Text preview readable

## Success Criteria

✅ Index schema defined and created in Azure AI Search  
✅ Ingestion worker uploads chunks to search index  
✅ Search endpoint returns results with proper filters  
✅ Season filtering works correctly  
✅ Association + scope filtering works correctly  
✅ Regional scope requires associationId  
✅ Citations include rule number, page, and source  
✅ 20 test questions return correct rule in top 3 (>80% success rate)  
✅ Web UI displays search results with citations  

## Troubleshooting

### Index creation fails
- Verify Search:Endpoint and Search:AdminKey in local.settings.json
- Check Azure AI Search service is running and accessible
- Review index schema for invalid field definitions

### No documents indexed
- Check worker logs for indexing errors
- Verify chunks.json exists in blob storage
- Check Azure AI Search portal for index statistics

### Search returns no results
- Verify documents are indexed (check stats endpoint)
- Try broader queries
- Check filter values match indexed data

### Wrong results returned
- Review index schema scoring/ranking configuration
- Check if filters are too restrictive
- Verify chunk text quality in debug endpoints

### Regional search not working
- Ensure associationId is provided when Regional scope is selected
- Verify indexed documents have correct associationId field
- Check filter logic in search implementation

## Performance Expectations

- Search latency: < 200ms for keyword search
- Indexing: ~1-2 seconds per PDF (50 chunks)
- Index size: ~5 KB per chunk average

## Next Steps

Once Milestone 2 is verified:
- **Milestone 3**: Add precedence resolution and override detection
- **Milestone 4**: Implement chat/RAG endpoint with strict grounding
- **Enhancement**: Add hybrid search (keyword + semantic)
