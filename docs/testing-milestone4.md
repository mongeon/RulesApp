# Testing Milestone 4 — Chat (RAG) with Strict Citations

This guide walks through testing the chat/RAG endpoint with grounded answers and strict citation validation.

## Prerequisites

1. **Milestones 1-3 completed**
   - PDFs uploaded and ingested
   - Azure AI Search indexed with rules
   - Precedence resolver working
2. **Azure OpenAI resource** (optional for enhanced responses)
   - Update `local.settings.json` with Azure OpenAI endpoint and key if using AI generation
3. **Azurite running** (local storage emulator)
4. **Functions running** (`func start` in `src/RulesApp.Api`)

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
    "Search:IndexName": "rules-active",
    "OpenAI:Endpoint": "https://YOUR-OPENAI.openai.azure.com",
    "OpenAI:Key": "YOUR-OPENAI-KEY",
    "OpenAI:DeploymentName": "gpt-4"
  }
}
```

**Note**: OpenAI configuration is optional. If not configured, the chat endpoint will return the retrieved context without AI enhancement.

## Test Steps

### 1. Basic Chat Request (Without AI Generation)

Test simple retrieval with precedence collapse:

```powershell
$body = @{
    query = "Combien de joueurs dans une équipe?"
    seasonId = "2025"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Expected response:
```json
{
  "status": "ok",
  "query": "Combien de joueurs dans une équipe?",
  "answer": "Based on the rulebook context...",
  "citations": [
    {
      "chunkId": "abc123...",
      "ruleKey": "RULE_4_01",
      "ruleNumberText": "4.01",
      "title": "Règle 4.01 – Composition de l'équipe",
      "scope": "Canada",
      "docType": "CanadaFr",
      "seasonId": "2025",
      "associationId": null,
      "pageStart": 15,
      "pageEnd": 15,
      "textPreview": "Une équipe est composée de neuf joueurs..."
    }
  ],
  "contextUsed": 1,
  "totalRetrieved": 5
}
```

### 2. Chat with Association Context

Test regional precedence:

```powershell
$body = @{
    query = "Quelle est la durée maximale d'un match?"
    seasonId = "2025"
    associationId = "LBML"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Expected: Primary citation from Regional if override exists, otherwise Quebec/Canada.

### 3. Not Found Scenario

Test query with no relevant rules:

```powershell
$body = @{
    query = "Quel est le prix d'un billet de match?"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Expected response:
```json
{
  "status": "not_found",
  "query": "Quel est le prix d'un billet de match?",
  "answer": "No relevant rules found in the provided rulebooks.",
  "citations": [],
  "contextUsed": 0,
  "totalRetrieved": 0
}
```

### 4. Citation Validation

Test that all citations in the response refer to retrieved chunks:

```powershell
$body = @{
    query = "Règles sur le terrain de jeu"
    seasonId = "2025"
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body

# Verify each citation has a valid chunkId
$response.citations | ForEach-Object {
    if (-not $_.chunkId) {
        Write-Error "Missing chunkId in citation: $_"
    }
    if (-not $_.pageStart) {
        Write-Error "Missing pageStart in citation: $_"
    }
    if (-not $_.ruleNumberText -and -not $_.ruleKey) {
        Write-Warning "No rule identifier in citation: $_"
    }
}
```

### 5. Precedence Verification

Test that chat endpoint applies precedence correctly:

```powershell
# First, search to see all candidates
$searchBody = @{
    query = "uniforme des joueurs"
    seasonId = "2025"
    associationId = "LBML"
    top = 10
} | ConvertTo-Json

$searchResults = Invoke-RestMethod -Uri "http://localhost:7071/api/search" `
    -Method Post `
    -ContentType "application/json" `
    -Body $searchBody

# Then chat - should use primary from precedence
$chatBody = @{
    query = "uniforme des joueurs"
    seasonId = "2025"
    associationId = "LBML"
} | ConvertTo-Json

$chatResponse = Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method Post `
    -ContentType "application/json" `
    -Body $chatBody

# Verify chat uses the primary chunk from precedence results
if ($searchResults.ruleGroups.Count -gt 0) {
    $primaryChunk = $searchResults.ruleGroups[0].primaryChunk
    if ($chatResponse.citations[0].chunkId -ne $primaryChunk.chunkId) {
        Write-Error "Chat did not use precedence-resolved primary chunk"
    }
} else {
    Write-Warning "No rule groups found in search results"
}
```

### 6. Multi-Rule Question

Test question requiring multiple rules:

```powershell
$body = @{
    query = "Quelles sont les dimensions du terrain et la distance entre les buts?"
    seasonId = "2025"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Expected: Multiple citations covering terrain dimensions and base distances.

### 7. Bilingual Query

Test English query (if CanadaEn data exists):

```powershell
$body = @{
    query = "What is a regulation game?"
    seasonId = "2025"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Expected: Results from CanadaEn or translated context.

### 8. Test 20 Domain Questions

Run comprehensive test questions from [docs/domain.md](domain.md):

```powershell
# Create test script
$questions = @(
    "Combien de joueurs dans une équipe?",
    "Quelle est la distance entre les buts?",
    "Qu'est-ce qu'un match réglementaire?",
    "Règles sur les bâtons de baseball",
    "Uniforme obligatoire pour les joueurs",
    "Durée d'un match de baseball",
    "Rôle de l'arbitre en chef",
    "Qu'est-ce qu'une prise?",
    "Règles sur le marbre",
    "Zone des balles",
    "Substitution de joueurs",
    "Règles sur les lancers",
    "Qu'est-ce qu'un retrait?",
    "Coureur sur les buts",
    "Interférence offensive",
    "Équipement de protection obligatoire",
    "Rôle du receveur",
    "Match déclaré forfait",
    "Ordre de passage au bâton",
    "Terrain de jeu dimensions"
)

$results = @()
foreach ($question in $questions) {
    $body = @{
        query = $question
        seasonId = "2025"
    } | ConvertTo-Json
    
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
            -Method Post `
            -ContentType "application/json" `
            -Body $body
        
        $results += [PSCustomObject]@{
            Question = $question
            Status = $response.status
            CitationCount = $response.citations.Count
            ContextUsed = $response.contextUsed
            HasRuleNumber = ($response.citations | Where-Object { $_.ruleNumberText -or $_.ruleKey }).Count -gt 0
        }
    } catch {
        $results += [PSCustomObject]@{
            Question = $question
            Status = "error"
            Error = $_.Exception.Message
        }
    }
}

# Display results
$results | Format-Table -AutoSize

# Calculate success rate
$successCount = ($results | Where-Object { $_.Status -eq "ok" }).Count
$totalCount = $results.Count
$successRate = [math]::Round(($successCount / $totalCount) * 100, 2)
Write-Host "Success Rate: $successRate% ($successCount/$totalCount)" -ForegroundColor Green
```

### 9. Test Edge Cases

#### Empty query
```powershell
$body = @{ query = "" } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Expected: HTTP 400 Bad Request

#### Very long query
```powershell
$body = @{
    query = "A" * 1000  # 1000 characters
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Expected: Either truncated or 400 Bad Request

#### Query with special characters
```powershell
$body = @{
    query = "Règle sur les joueurs (équipe A vs B) - match #1"
    seasonId = "2025"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Expected: Normal response with proper escaping

### 10. Grounding Validation

Verify that answers are strictly grounded:

```powershell
# Ask about something not in rulebooks
$body = @{
    query = "Quelle est la température idéale pour jouer?"
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body

# Should return not_found
if ($response.status -eq "ok" -and $response.citations.Count -eq 0) {
    Write-Error "Answer provided without citations - grounding violation!"
}
```

### 11. Performance Testing

Measure response times:

```powershell
$queries = @(
    "Combien de joueurs?",
    "Distance entre les buts?",
    "Durée du match?"
)

foreach ($query in $queries) {
    $body = @{ query = $query } | ConvertTo-Json
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    
    $response = Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
        -Method Post `
        -ContentType "application/json" `
        -Body $body
    
    $stopwatch.Stop()
    Write-Host "$query - $($stopwatch.ElapsedMilliseconds)ms" -ForegroundColor Cyan
}
```

Expected: < 2 seconds for keyword search + precedence (without AI generation)

## Success Criteria

✅ Chat endpoint retrieves and ranks candidates correctly  
✅ Precedence resolution applied (regional > quebec > canada)  
✅ All citations include chunkId, ruleKey/ruleNumberText, page numbers  
✅ Citation validation ensures all cited chunks were retrieved  
✅ Returns "not_found" when no relevant context exists  
✅ No hallucinated rules or invented information  
✅ 20 test questions return correct rules (>80% success rate)  
✅ Performance: < 2s without AI, < 5s with AI generation  
✅ Proper error handling for edge cases  

## Troubleshooting

### No results returned
- Verify search index has documents (check stats endpoint)
- Test search endpoint first to verify retrieval works
- Check season filter matches ingested data

### Wrong precedence applied
- Verify override mappings are confirmed (not just proposed)
- Check PrecedenceResolver logic
- Test search/precedence endpoint to see grouped results

### Citations missing information
- Check chunk metadata during ingestion
- Verify search index includes all required fields
- Review SearchHit mapping in search response

### "Not found" for known rules
- Increase retrieval topK parameter
- Check query keywords match indexed text
- Verify filters (season, association) are correct

### AI generation errors (if using Azure OpenAI)
- Verify OpenAI configuration in local.settings.json
- Check Azure OpenAI deployment is active
- Review API quotas and rate limits
- Check prompt construction in chat service

### Performance issues
- Check search service tier (Basic vs Standard)
- Verify precedence grouping is efficient
- Consider caching frequently asked questions
- Monitor Azure AI Search query times

## Optional Enhancements

Once Milestone 4 is verified, consider:
- **Conversation history**: Multi-turn chat with context
- **Hybrid search**: Add semantic ranking with embeddings
- **Answer quality scoring**: Rate limit answer confidence
- **Caching**: Cache frequent queries
- **Feedback loop**: Track answer quality ratings

## Web UI Testing (if implemented)

1. Navigate to Chat page
2. Enter question in French
3. Select association (optional)
4. Submit query
5. Verify:
   - Answer displayed with proper formatting
   - Citations listed with rule numbers and page links
   - "Not found" message shown when appropriate
   - Loading state during request
   - Error handling for failed requests

## Evaluation Metrics

Track these metrics for quality assessment:
- **Answer rate**: % of questions with status=ok
- **Citation completeness**: % of answers with rule numbers + pages
- **Grounding violations**: Count of answers without supporting citations
- **User satisfaction**: Manual rating of answer quality (1-5)
- **Response time**: P50 and P95 latency

## Next Steps

After Milestone 4:
- Gather user feedback on answer quality
- Tune retrieval parameters (topK, filters)
- Add conversation context for follow-up questions
- Implement answer caching for common queries
- Add telemetry for continuous monitoring
