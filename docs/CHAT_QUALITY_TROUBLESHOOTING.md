# Chat Quality Troubleshooting — LBML Regional Rules

**Issue:** Chat doesn't return regional rule 2.3 (annulation et remise de partie) despite it being clearly stated on page 6.

**Date:** January 2, 2026

---

## Diagnostic Checklist

### 1. ✅ Verify Chunking — Check if Rule 2.3 Was Detected

First, confirm that rule 2.3 was properly extracted and chunked:

```bash
# Get the latest LBML ingestion job
curl.exe http://localhost:7071/api/admin/jobs/latest

# Replace {jobId} with the LBML upload job
curl.exe "http://localhost:7071/api/admin/debug/chunks?jobId={jobId}" | ConvertFrom-Json | Select-Object -ExpandProperty chunks | Where-Object { $_.ruleNumberText -like "*2.3*" -or $_.title -like "*annul*" }
```

**Expected result:** Should find at least one chunk with:
- `ruleNumberText`: "2.3"
- `title`: Something like "Annulation et remise de partie"
- `pageStart`: 6

**If not found:** The chunker didn't detect rule 2.3 as a rule number. This could be because:
- Pattern mismatch in regex (e.g., "2.3" format not matching French regional numbering)
- Rule is part of a larger section that was chunked differently
- PDF text extraction issue

---

### 2. ✅ Verify Search Indexing — Check Azure AI Search

Confirm the chunk is in the search index:

```bash
# Search for rule 2.3
curl.exe -X POST "http://localhost:7071/api/search" `
  -H "Content-Type: application/json" `
  -d '{
    "query": "2.3",
    "associationId": "LBML",
    "scopes": ["Regional"],
    "top": 10
  }' | ConvertFrom-Json | Select-Object -ExpandProperty results
```

**Expected result:** Should return the chunk(s) with rule 2.3.

**If empty:** The chunk exists in blobs but wasn't indexed. Re-run indexing:
```bash
curl.exe -X POST http://localhost:7071/api/admin/index/create
```

---

### 3. ✅ Verify Search Relevance — Keyword Matching

Test with the exact query the user asked:

```bash
curl.exe -X POST "http://localhost:7071/api/search" `
  -H "Content-Type: application/json" `
  -d '{
    "query": "annulation remise partie",
    "associationId": "LBML",
    "scopes": ["Canada", "Quebec", "Regional"],
    "top": 10
  }' | ConvertFrom-Json
```

**Check the results:**
- Is the 2.3 rule in the top 3?
- What is its **score**? (shown in results)
- Is the `scope` showing as "Regional"?
- Is the `ruleNumberText` correctly set to "2.3"?

**If score is low (<5):** The search relevance is poor. This could be because:
- Keywords "annulation remise" don't appear in the chunk text
- The query is too different from the rule's actual wording
- Search fields aren't properly boosted

---

### 4. ✅ Verify Chat Query — Check Chat Scopes

The chat service should be searching all scopes including Regional. Check that it's passing the right scopes:

**Current code** (ChatService.cs, lines 45-47):
```csharp
var scopes = new List<string> { "Canada", "Quebec" };
if (!string.IsNullOrEmpty(request.AssociationId))
{
    scopes.Add("Regional");
}
```

✅ This **should** include Regional when associationId is set.

**Verify via debugging:**
```bash
# Check the chat request - it should include "Regional" in scopes
curl.exe -X POST "http://localhost:7071/api/chat" `
  -H "Content-Type: application/json" `
  -d '{
    "query": "Quel est la règle pour annulation et remise de partie?",
    "associationId": "LBML",
    "maxContext": 5,
    "useAI": false
  }'
```

Check the response:
- `status`: Should be "ok" if rule found, "not_found" otherwise
- `contextUsed`: How many chunks were used (should be > 0)
- `totalRetrieved`: Total chunks considered

---

## Common Root Causes (Ranked by Likelihood)

### 1. **Chunker Regex Doesn't Match French Regional Numbering** (Most Likely)

**Problem:** The `RuleNumberPattern` regex expects patterns like `6.01(a)` but regional rules might be formatted as `2.3 -` or just `2.3` with no subsections.

**Current regex** (Chunker.cs, line 26):
```csharp
@"(?:^|\n)\s*(?:\d+\s+)?(?:R[èe]gle\s+)?(\d+\.\d+(?:\.\d+)?(?:\s*\([a-z]\))?)\s*(?:-|[A-ZÀÂÇÉÈÊËÎÏÔÛÙÜŸŒÆ])"
```

**Problem:** Requires either:
- A dash after the number
- A capital letter after the number

**But regional rules might use:** `2.3 Annulation et remise` (space + lowercase)

**Fix:** Update the regex to handle regional formatting:
```csharp
@"(?:^|\n)\s*(\d+\.\d+(?:\.\d+)?(?:\s*\([a-z]\))?)\s*(?:[-–—]|(?=[A-ZÀ-Ÿ][a-zà-ÿ]))"
```

This allows:
- Various dash characters: `-`, `–`, `—`
- Lookahead for capital letter followed by lowercase (no dash required)

---

### 2. **Search Score Threshold Too High**

**Problem:** In ChatService.cs line 70:
```csharp
var relevantResults = searchResults.Results
    .Where(r => r.Score > 1.0)  // ← This threshold might be too strict
    .ToList();
```

For French text with simple keywords, a score of 1.0 might filter out valid results.

**Solution:** Lower threshold or make it contextual:
```csharp
var relevantResults = searchResults.Results
    .Where(r => r.Score > 0.5 || searchResults.TotalResults < 5)  // More lenient if few results
    .ToList();
```

---

### 3. **AssociationId Not Being Passed from Web UI**

**Problem:** The Chat.razor component might not be sending `associationId` to the API.

**Check Chat.razor** (lines ~165-170):
```csharp
var request = new ChatRequest(
    Query: query.Trim(),
    SeasonId: string.IsNullOrWhiteSpace(seasonId) ? null : seasonId,
    AssociationId: string.IsNullOrWhiteSpace(associationId) ? null : associationId,
    // ...
);
```

✅ This **should** be passing it correctly.

**Verify:** Check the browser's Network tab or add logging to see if associationId is in the request.

---

## Quick Fixes to Try (In Order)

### Fix 1: Update Chunker Regex (Most Likely to Help)

Open [src/RulesApp.Api/Services/Chunker.cs](../src/RulesApp.Api/Services/Chunker.cs#L26), replace:

```csharp
private static readonly Regex RuleNumberPattern = new(
    @"(?:^|\n)\s*(?:\d+\s+)?(?:R[èe]gle\s+)?(\d+\.\d+(?:\.\d+)?(?:\s*\([a-z]\))?)\s*(?:-|[A-ZÀÂÇÉÈÊËÎÏÔÛÙÜŸŒÆ])", 
    RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
```

With:

```csharp
private static readonly Regex RuleNumberPattern = new(
    @"(?:^|\n)\s*(?:\d+\s+)?(?:R[èe]gle\s+)?(\d+\.\d+(?:\.\d+)?(?:\s*\([a-z]\))?)\s*(?:[-–—]|\s+(?=[A-ZÀ-Ÿ][a-zà-ÿ]))", 
    RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
```

Then **re-upload and re-index the LBML PDF**.

---

### Fix 2: Lower Search Relevance Threshold

Open [src/RulesApp.Api/Services/ChatService.cs](../src/RulesApp.Api/Services/ChatService.cs#L70), change:

```csharp
var relevantResults = searchResults.Results
    .Where(r => r.Score > 1.0)
    .ToList();
```

To:

```csharp
var relevantResults = searchResults.Results
    .Where(r => r.Score > 0.5 || searchResults.TotalResults < 5)
    .ToList();
```

No re-indexing needed; takes effect immediately.

---

### Fix 3: Verify Association ID in Chat Request

Add this temporary debug output to Chat.razor, then test with association "LBML":

```razor
// Before SearchAsync, add:
@if (!string.IsNullOrWhiteSpace(associationId))
{
    <div class="alert alert-info">🔍 Searching for association: <strong>@associationId</strong></div>
}
```

This confirms the association is being sent to the API.

---

## Testing After Fixes

Once you apply a fix, test with:

```bash
# 1. Re-create chunks from LBML PDF
curl.exe -X POST http://localhost:7071/api/admin/index/create
.\upload-pdf.ps1 -FilePath "C:\Users\gabri\Downloads\6c755337-47b6-11f0-a918-0614b4bc81d9.pdf" `
    -SeasonId "2026" -ScopeLevel "Regional" -DocType "Fr" -AssociationId "LBML"

# 2. Test search directly
curl.exe -X POST "http://localhost:7071/api/search" `
  -H "Content-Type: application/json" `
  -d '{
    "query": "annulation remise",
    "associationId": "LBML",
    "scopes": ["Regional"],
    "top": 5
  }' | ConvertFrom-Json

# 3. Test chat
curl.exe -X POST "http://localhost:7071/api/chat" `
  -H "Content-Type: application/json" `
  -d '{
    "query": "Quel est la règle pour annulation et remise de partie?",
    "associationId": "LBML"
  }' | ConvertFrom-Json
```

Expected:
- Search returns rule 2.3 in top 3 results
- Chat returns rule 2.3 with citation (Rule 2.3, Page 6, LBML)

---

## Next Steps

1. **Run diagnostic checklist** above to pinpoint the issue
2. **Apply most likely fix** (usually Fix 1: regex update)
3. **Re-upload and test** with the verification commands
4. **Report back** which fix worked

---

## Related Files

- [src/RulesApp.Api/Services/Chunker.cs](../src/RulesApp.Api/Services/Chunker.cs) — Rule detection regex
- [src/RulesApp.Api/Services/ChatService.cs](../src/RulesApp.Api/Services/ChatService.cs) — Search score filtering
- [src/RulesApp.Api/Services/SearchStore.cs](../src/RulesApp.Api/Services/SearchStore.cs) — Search ranking/filtering
- [src/RulesApp.Web/Pages/Chat.razor](../src/RulesApp.Web/Pages/Chat.razor) — Chat UI/request building

---

## Notes

- French regional documents often have different rule numbering (2.3 vs 6.01(a))
- Search relevance depends on exact keyword matching
- Regional rules must have correct `associationId` in both chunker and chat requests
