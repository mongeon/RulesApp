# Improving Chat Answer Relevance

## Problem

The chat endpoint was returning answers based on less relevant rules. Example:
- Question: "Combien de manches dans un match?" (How many innings in a game?)
- Issue: Retrieved rules included supplementary innings (Quebec 40.7) and post-game reports (8.04), which are less relevant than the core rule about game length (7.01)

## Root Causes

1. **No relevance filtering** - All search results used regardless of score
2. **Keyword-only search** - No semantic understanding of query intent  
3. **Equal context weighting** - AI treated all provided context as equally important
4. **No score-based ordering** - Context not ordered by relevance
5. **Too many low-quality results** - Retrieving 20 results diluted precision with noise

## Changes Implemented

### 1. **Score Threshold Filtering** ✅
```csharp
// Filter by minimum relevance score (keep only results with score > 1.0)
var relevantResults = searchResults.Results
    .Where(r => r.Score > 1.0)
    .ToList();
```
**Impact**: Removes low-quality matches that add noise

### 2. **Improved Search Options** ✅
```csharp
SearchFields = { "text", "ruleNumberText", "title" }, // Target specific fields
QueryType = SearchQueryType.Simple,
MinimumCoverage = 80.0 // Require 80% query term coverage
```
**Impact**: Better matching on rule numbers and titles

### 3. **Reduced Retrieval Count** ✅
Changed from `Top: 20` to `Top: 15`
**Impact**: Precision over recall - fewer but better matches

### 4. **Score-Based Ordering** ✅
```csharp
var orderedChunks = contextChunks.OrderByDescending(c => c.Score).ToList();
```
**Impact**: Most relevant rules appear first in context

### 5. **Relevance Indicators in Prompt** ✅
```
[MOST RELEVANT] Rule 7.01 (Canada, Page 84, Score: 5.23):
[HIGH RELEVANCE] Rule 7.02 (Quebec, Page 29, Score: 3.87):
[REFERENCE] Rule 8.04 (Canada, Page 90, Score: 2.14):
```
**Impact**: AI explicitly told which rules are most relevant

### 6. **Enhanced System Prompt** ✅
```
1. PRIORITIZE rules marked as [MOST RELEVANT] and [HIGH RELEVANCE]
8. Focus on the most relevant rules rather than mentioning all provided rules
```
**Impact**: AI focuses on best matches, not all matches

## Expected Improvements

### Before (Issues):
- ❌ Retrieved 20 results including low-scoring ones
- ❌ No minimum score threshold
- ❌ Context presented in arbitrary order
- ❌ AI treated all context equally
- ❌ Answers cited less relevant rules

### After (Fixes):
- ✅ Retrieve 15 results, filter by score > 1.0
- ✅ Only high-quality matches included
- ✅ Context ordered by relevance score
- ✅ AI explicitly prioritizes top matches
- ✅ Answers focus on most relevant rules

## Additional Recommendations

### Short-Term (High Impact, Low Effort)

#### 1. **Add Semantic Search** (Azure AI Search Semantic Ranking)
```bicep
// In infra/main.bicep, upgrade search to support semantic ranking
resource searchService 'Microsoft.Search/searchServices@2023-11-01' = {
  sku: {
    name: 'standard' // Required for semantic search
  }
  properties: {
    semanticSearch: 'standard' // Enable semantic search
  }
}
```

Then in SearchStore.cs:
```csharp
options.QueryType = SearchQueryType.Semantic;
options.SemanticSearch = new SemanticSearchOptions
{
    SemanticConfigurationName = "rules-semantic-config",
    QueryCaption = new QueryCaption(QueryCaptionType.Extractive)
};
```

**Impact**: Understands semantic meaning, not just keywords
**Cost**: Semantic search adds ~$500/month on Standard tier

#### 2. **Query Enhancement** 
Add query preprocessing in ChatService:
```csharp
private string EnhanceQuery(string originalQuery)
{
    // Expand abbreviations
    var enhanced = originalQuery
        .Replace("combien", "nombre quantité durée", StringComparison.OrdinalIgnoreCase)
        .Replace("manches", "manches innings périodes", StringComparison.OrdinalIgnoreCase);
    
    return enhanced;
}
```

**Impact**: Better keyword coverage for FR/EN synonyms

#### 3. **Boost Rule Number Matches**
If user mentions a rule number (7.01), boost that exact match:
```csharp
if (Regex.IsMatch(request.Query, @"\d+\.\d+"))
{
    options.ScoringParameters.Add("ruleNumberBoost-10"); // Boost by 10x
}
```

**Impact**: Direct rule number queries get exact matches first

### Medium-Term (Medium Impact, Medium Effort)

#### 4. **Better Chunking Strategy**
Current chunking might split rules awkwardly. Improvements:
- Detect rule boundaries (e.g., "7.01", "Règle 7.01")
- Never split a rule across chunks
- Include rule heading in every chunk of that rule

**Impact**: Each chunk better represents complete rule context

#### 5. **Add Reranking Model**
Use a cross-encoder model to rerank results:
```csharp
// After initial search, rerank using BERT-based cross-encoder
var rerankedResults = await _rerankingService.RerankAsync(
    query: request.Query,
    candidates: searchResults.Results
);
```

**Impact**: Much better relevance ordering, 10-30% improvement
**Options**: 
- Azure AI Search built-in reranking (Semantic Search)
- Custom model (ms-marco-MiniLM-L-12-v2)

#### 6. **Query Classification**
Classify query type to adjust retrieval:
```csharp
enum QueryType { 
    DirectRule,      // "What is rule 7.01?"
    Factual,         // "How many innings?"
    Procedural,      // "What happens if...?"
    Comparative      // "Difference between..."
}
```

Adjust retrieval strategy per type (DirectRule → exact match, Factual → semantic)

### Long-Term (High Impact, High Effort)

#### 7. **Fine-Tuned Embeddings**
Train custom embeddings on baseball rulebook corpus:
- Better semantic understanding of baseball-specific terminology
- Better FR/EN bilingual matching
- Domain-specific similarity

**Effort**: Requires embedding model training, GPU resources

#### 8. **User Feedback Loop**
Add thumbs up/down on answers:
```csharp
public record FeedbackRequest(
    string QueryId,
    bool IsRelevant,
    string? Comment
);
```

Track which rules users find most helpful for given queries.
Use to improve ranking over time.

#### 9. **Question Answering Model**
Replace OpenAI with fine-tuned QA model:
- Train on baseball rulebook QA pairs
- Better at extractive answers with citations
- Lower cost than OpenAI

**Options**: 
- Fine-tune RoBERTa/CamemBERT on FR baseball rules
- Use FrALBERT for French QA

## Testing the Improvements

### Test Queries
```powershell
# Query 1: Direct question about game length
$body = @{
    query = "Combien de manches dans un match?"
    seasonId = "2025"
    useAI = $true
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method POST -Body $body -ContentType "application/json"

# Expected: Should prioritize rule 7.01 about 7 innings
# Should NOT prioritize supplementary innings (40.7) or reports (8.04)

# Query 2: Rule number direct match
$body = @{
    query = "Qu'est-ce que la règle 7.01?"
    seasonId = "2025"
    useAI = $true
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method POST -Body $body -ContentType "application/json"

# Expected: Rule 7.01 should be [MOST RELEVANT]

# Query 3: Synonym handling  
$body = @{
    query = "Quelle est la durée d'une partie?"
    seasonId = "2025"
    useAI = $true
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/chat" `
    -Method POST -Body $body -ContentType "application/json"

# Expected: Should still match game length rules even with different wording
```

### Validation Checklist
- [ ] Most relevant rule appears first in citations
- [ ] Score threshold removes noise (check TotalRetrieved vs ContextUsed)
- [ ] Answer focuses on top-scored rules
- [ ] Irrelevant rules (like post-game reports) filtered out
- [ ] Bilingual queries work (FR and EN keywords)

## Metrics to Track

### Search Quality Metrics
```csharp
public record ChatMetrics(
    string QueryId,
    string Query,
    int TotalRetrieved,
    int AfterFiltering,      // NEW: After score > 1.0 filter
    int ContextUsed,
    double TopScore,          // NEW: Highest relevance score
    double AvgScore,          // NEW: Average of used context
    List<string> CitedRules,
    double ResponseTimeMs
);
```

### Target KPIs
- **Precision@3**: Top 3 results are relevant > 80%
- **Score Distribution**: Used context avg score > 2.0
- **Filter Rate**: 20-40% of results filtered by threshold (too many = threshold too high)
- **User Feedback**: Thumbs up rate > 70%

## Cost Impact

### Current Changes: **$0**
- Code changes only, no additional services

### Recommended Next Step: **Semantic Search**
- **Cost**: ~$500/month on Standard tier
- **Benefit**: 20-40% relevance improvement
- **ROI**: High for low-traffic application (cost per query still low)

### Alternative: **Stay on Basic + Query Enhancement**
- **Cost**: $0
- **Benefit**: 10-15% improvement from query expansion
- **Best for**: Very low budget scenarios

## Implementation Priority

1. ✅ **Done**: Score filtering, ordering, prompt improvements
2. 🔜 **Next**: Test current changes, measure improvement
3. 🎯 **If needed**: Add query enhancement (FR/EN synonyms)
4. 💰 **If budget allows**: Upgrade to semantic search
5. 🔄 **Iterate**: Add user feedback, monitor metrics

## Related Files
- [ChatService.cs](../src/RulesApp.Api/Services/ChatService.cs) - RAG pipeline
- [SearchStore.cs](../src/RulesApp.Api/Services/SearchStore.cs) - Search implementation  
- [main.bicep](../infra/main.bicep) - Azure AI Search configuration
- [testing-milestone4.md](testing-milestone4.md) - Testing guide
