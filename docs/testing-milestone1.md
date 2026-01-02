# Testing Milestone 1 — Upload + Ingestion

This guide walks through testing the complete ingestion pipeline.

## Prerequisites

1. **Azurite running** (local storage emulator)
2. **Functions running** (`func start` in `src/RulesApp.Api`)
3. **Sample PDF** (any PDF file for testing)

## Test Steps

### 1. Start Azurite

```powershell
# Using Docker
docker run --rm -it `
  -p 10000:10000 -p 10001:10001 -p 10002:10002 `
  mcr.microsoft.com/azure-storage/azurite
```

Or install Azurite globally:
```powershell
npm install -g azurite
azurite
```

### 2. Start the Functions API

```powershell
cd src/RulesApp.Api
func start
```

You should see:
```
Functions:
  AdminBuild: [POST] http://localhost:7071/api/admin/build
  AdminDebugChunk: [GET] http://localhost:7071/api/admin/debug/chunk
  AdminDebugChunks: [GET] http://localhost:7071/api/admin/debug/chunks
  AdminJobsLatest: [GET] http://localhost:7071/api/admin/jobs/latest
  AdminUpload: [POST] http://localhost:7071/api/admin/upload
  Health: [GET] http://localhost:7071/api/health
  RulesIngestWorker: queueTrigger
```

### 3. Upload a PDF

```powershell
# Upload Canada FR rulebook (example)
curl.exe -X POST http://localhost:7071/api/admin/upload `
  -F "seasonId=2026" `
  -F "scopeLevel=Canada" `
  -F "docType=Fr" `
  -F "file=@C:\Users\gabri\Downloads\6cb071ff-2f69-11f0-80ed-0611ff2db335.pdf"
```

Expected response:
```json
{
  "jobId": "job_abc123...",
  "status": "queued",
  "blobPath": "rules/2026/global/CanadaFr.pdf",
  "seasonId": "2026",
  "associationId": null,
  "scopeLevel": "Canada",
  "docType": "CanadaFr",
  "size": 123456
}
```

### 4. Start Ingestion Build

```powershell
curl.exe -X POST http://localhost:7071/api/admin/build
```

Expected response:
```json
{
  "message": "Build started",
  "seasonId": "2025",
  "associationId": null,
  "enqueuedJobs": 1,
  "jobs": [
    {
      "jobId": "abc-123-...",
      "docType": "CanadaFr",
      "scope": "global"
    }
  ]
}
```

### 5. Check Ingestion Progress

Wait a few seconds for the queue worker to process, then:

```powershell
curl.exe http://localhost:7071/api/admin/jobs/latest
```

Expected response:
```json
{
  "seasonId": "2025",
  "associationId": null,
  "jobs": [
    {
      "jobId": "abc-123-...",
      "seasonId": "2025",
      "associationId": null,
      "docType": "CanadaFr",
      "status": "Completed",
      "startedAt": "2025-12-24T...",
      "completedAt": "2025-12-24T...",
      "pageCount": 25,
      "chunkCount": 50,
      "errorMessage": null
    }
  ]
}
```

### 6. View Chunks Summary

```powershell
curl.exe "http://localhost:7071/api/admin/debug/chunks?jobId=b67abfd7-1da1-4608-8086-0a5c2ba2f758"
```

Expected response:
```json
{
  "jobId": "abc-123-...",
  "chunkCount": 50,
  "chunks": [
    {
      "chunkId": "a1b2c3d4...",
      "ruleKey": "RULE_1_01",
      "ruleNumberText": "1.01",
      "title": "Règle 1.01 – Objectifs du jeu",
      "pageStart": 5,
      "pageEnd": 5,
      "textPreview": "Le baseball est un jeu entre deux équipes...",
      "textLength": 456
    }
  ]
}
```

### 7. View Individual Chunk

```powershell
curl.exe "http://localhost:7071/api/admin/debug/chunk?jobId=b67abfd7-1da1-4608-8086-0a5c2ba2f758&chunkId=acbcf6a5dd847c1e33b0f8fb072bd6307"
```

Expected response: Full chunk object with complete text.

## Success Criteria

✅ Upload endpoint accepts PDF and stores in blob  
✅ Build endpoint enqueues jobs  
✅ Queue worker processes PDF  
✅ pages.json created with extracted text  
✅ chunks.json created with rule-aligned chunks  
✅ Table entities track job status  
✅ Debug endpoints return chunk data  

## Troubleshooting

### Functions don't start
- Check that Azurite is running
- Verify `local.settings.json` exists with Storage:ConnectionString

### Queue worker doesn't trigger
- Check Functions console for errors
- Verify queue extension is loaded
- Check Azurite queue storage explorer

### No chunks created
- Check worker logs in Functions console
- Verify PDF is valid and readable
- Check blob storage for pages.json artifact

## Next Steps

Once Milestone 1 is verified:
- Milestone 2: Add Azure AI Search indexing
- Milestone 3: Precedence and override detection
- Milestone 4: Chat endpoint with RAG
