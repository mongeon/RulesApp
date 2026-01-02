# Test Data — RulesApp Rulebook PDFs

This document lists the official rulebook PDF files used for testing and development of RulesApp.

## Test PDF Files (Season 2026)

### Canada Rulebooks
- **Canada, French**: `6cb071ff-2f69-11f0-80ed-0611ff2db335.pdf`
  - ScopeLevel: `Canada`
  - DocType: `Fr`
  - Language: French
  
- **Canada, English**: `6cb07278-2f69-11f0-80ed-0611ff2db335.pdf`
  - ScopeLevel: `Canada`
  - DocType: `En`
  - Language: English

### Québec Rulebooks
- **Québec, French**: `6ccc88f7-2f69-11f0-80ed-0611ff2db335.pdf`
  - ScopeLevel: `Quebec`
  - DocType: `Fr`
  - Language: French

- **Québec, English**: `6ccc8805-2f69-11f0-80ed-0611ff2db335.pdf`
  - ScopeLevel: `Quebec`
  - DocType: `En`
  - Language: English

### Regional Association Rulebooks
- **Baseball Laurentides (LBML)**: `6c755337-47b6-11f0-a918-0614b4bc81d9.pdf`
  - ScopeLevel: `Regional`
  - AssociationId: `LBML`
  - DocType: `Fr`
  - Language: French

---

## Upload Commands

The upload endpoint uses JSON with base64-encoded file content. Use the provided PowerShell helper script for easy uploads.

### Using the Helper Script

#### Upload a single file:

```powershell
.\upload-pdf.ps1 -FilePath "C:\Users\gabri\Downloads\6cb071ff-2f69-11f0-80ed-0611ff2db335.pdf" `
    -SeasonId "2026" -ScopeLevel "Canada" -DocType "Fr"
```

#### Upload all test files at once:

```powershell
.\upload-test-data.ps1
```

### Individual Upload Examples

### 1. Upload Canada French

```powershell
.\upload-pdf.ps1 -FilePath "C:\Users\gabri\Downloads\6cb071ff-2f69-11f0-80ed-0611ff2db335.pdf" `
    -SeasonId "2026" -ScopeLevel "Canada" -DocType "Fr"
```

Expected response:
```json
{
  "jobId": "job_<guid>",
  "status": "queued",
  "blobPath": "rules/2026/global/CanadaFr.pdf",
  "seasonId": "2026",
  "scopeLevel": "Canada",
  "docType": "CanadaFr",
  "size": 1234567
}
```

### 2. Upload Canada English

```powershell
.\upload-pdf.ps1 -FilePath "C:\Users\gabri\Downloads\6cb07278-2f69-11f0-80ed-0611ff2db335.pdf" `
    -SeasonId "2026" -ScopeLevel "Canada" -DocType "En"
```

### 3. Upload Québec French

```powershell
.\upload-pdf.ps1 -FilePath "C:\Users\gabri\Downloads\6ccc88f7-2f69-11f0-80ed-0611ff2db335.pdf" `
    -SeasonId "2026" -ScopeLevel "Quebec" -DocType "Fr"
```

### 4. Upload Québec English

```powershell
.\upload-pdf.ps1 -FilePath "C:\Users\gabri\Downloads\6ccc8805-2f69-11f0-80ed-0611ff2db335.pdf" `
    -SeasonId "2026" -ScopeLevel "Quebec" -DocType "En"
```

### 5. Upload Baseball Laurentides (Regional)

```powershell
.\upload-pdf.ps1 -FilePath "C:\Users\gabri\Downloads\6c755337-47b6-11f0-a918-0614b4bc81d9.pdf" `
    -SeasonId "2026" -ScopeLevel "Regional" -DocType "Fr" -AssociationId "LBML"
```

---

## Helper Scripts

### upload-pdf.ps1

Helper script to upload individual PDF files. Located in the repository root.

**Parameters:**
- `-FilePath` (required): Path to the PDF file
- `-ScopeLevel` (required): Canada, Quebec, or Regional
- `-DocType` (required): Fr or En
- `-SeasonId` (optional): Defaults to "2026"
- `-AssociationId` (optional): Required for Regional scope
- `-ApiUrl` (optional): Defaults to http://localhost:7071/api/admin/upload

### upload-test-data.ps1

Batch upload script that uploads all 5 test PDF files at once. Located in the repository root.

**Usage:**
```powershell
.\upload-test-data.ps1
```

This script internally calls `upload-pdf.ps1` for each file.

---

## API Details

The upload endpoint accepts JSON with the following structure:

```json
{
  "seasonId": "2026",
  "associationId": "LBML",
  "scopeLevel": "Canada",
  "docType": "Fr",
  "fileName": "rulebook.pdf",
  "fileContentBase64": "<base64-encoded-file-content>"
}
```

**Endpoint:** `POST /api/admin/upload`  
**Content-Type:** `application/json`

---

## Verification Commands

After uploading, verify ingestion:

### Check latest job
```powershell
curl.exe http://localhost:7071/api/admin/jobs/latest
```

### View chunks for a job
```powershell
# Replace {jobId} with actual job ID from upload response
curl.exe "http://localhost:7071/api/admin/debug/chunks?jobId={jobId}"
```

### View specific chunk detail
```powershell
# Replace {chunkId} with actual chunk ID
curl.exe "http://localhost:7071/api/admin/debug/chunk?chunkId={chunkId}"
```

---

## Expected Blob Storage Structure

After successful uploads and ingestion, blob storage will contain:

```
rules/
  2026/
    global/
      CanadaFr.pdf
      CanadaEn.pdf
      QuebecFr.pdf
      QuebecEn.pdf
    LBML/
      RegionalFr.pdf

ingest/
  job_{guid_1}/
    pages.json
    chunks.json
  job_{guid_2}/
    pages.json
    chunks.json
  ...
```

---

## Notes

- All files are for **season 2026**
- Files must exist in the specified download path before running commands
- Ensure Azurite and Functions API are running before uploading
- Regional uploads require `associationId` parameter
- Ingestion happens asynchronously via queue trigger after upload
