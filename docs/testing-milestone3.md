# Testing Milestone 3 — Precedence + Override Proposals

This guide walks through testing the precedence resolution system and override detection/management functionality.

## Prerequisites

1. **Milestone 2 completed** (search working with keyword retrieval)
2. **Azurite running** (local storage emulator)
3. **Functions running** (`func start` in `src/RulesApp.Api`)
4. **Multiple rulebooks indexed** with overlapping rule numbers
   - At minimum: CanadaFr and QuebecFr (to test Canada->Quebec precedence)
   - Ideally: RegionalFr for an association (to test Regional->Quebec->Canada)

## Configuration

Same as Milestone 2 (`local.settings.json` with search endpoint, storage, etc.)

## Domain Context: Precedence Rules

### Hierarchy
- **Regional > Quebec > Canada**
- When multiple chunks share the same `ruleKey`, the highest precedence level becomes the "effective rule"
- Confirmed overrides can explicitly map a lower-level chunk to override a higher-level one

### Example Scenario
- Canada rule `1.04` says: "Le terrain doit être rectangulaire"
- Quebec rule `1.04` says: "Le terrain doit mesurer au moins 60m x 90m"
- Regional (LBJEQ) rule `1.04` says: "Pour notre association, le terrain doit être 55m x 85m minimum"

When searching for rule 1.04 with `associationId=LBJEQ`:
- Without precedence: returns 3 separate chunks
- With precedence: returns the Regional chunk as primary, with references to Canada/Quebec versions

## Test Steps

### 1. Upload Multiple Rulebooks

If not already done, upload Canada, Quebec, and Regional rulebooks:

```powershell
# Canada FR
curl.exe -X POST http://localhost:7071/api/admin/upload `
  -F "file=@C:\path\to\canada-fr.pdf" `
  -F "docType=CanadaFr" `
  -F "seasonId=2025"

# Quebec FR
curl.exe -X POST http://localhost:7071/api/admin/upload `
  -F "file=@C:\path\to\quebec-fr.pdf" `
  -F "docType=QuebecFr" `
  -F "seasonId=2025"

# Regional (example: LBJEQ)
curl.exe -X POST http://localhost:7071/api/admin/upload `
  -F "file=@C:\path\to\lbjeq-regional.pdf" `
  -F "docType=RegionalFr" `
  -F "associationId=LBJEQ" `
  -F "seasonId=2025"
```

### 2. Trigger Ingestion with Override Detection

```powershell
curl.exe -X POST "http://localhost:7071/api/admin/build?associationId=LBJEQ"
```

This should:
- Process all PDFs
- Generate chunks
- **Detect potential overrides** (heuristic analysis)
- Store override proposals in OverrideMappings table with status=Proposed

### 3. Check for Override Proposals

List proposed overrides:

```powershell
curl.exe "http://localhost:7071/api/admin/overrides?status=Proposed&associationId=LBJEQ"
```

Expected response:
```json
{
  "seasonId": "2025",
  "associationId": "LBJEQ",
  "status": "Proposed",
  "overrides": [
    {
      "mappingId": "abc-123-...",
      "seasonId": "2025",
      "associationId": "LBJEQ",
      "sourceRuleKey": "RULE_1_04",
      "sourceChunkId": "regional-chunk-id",
      "sourceScope": "Regional",
      "targetRuleKey": "RULE_1_04",
      "targetChunkId": "quebec-chunk-id",
      "targetScope": "Quebec",
      "status": "Proposed",
      "confidence": 0.85,
      "detectionReason": "Text contains 'cette règle remplace' or similar override indicator",
      "createdAt": "2025-12-24T...",
      "reviewedAt": null,
      "reviewedBy": null
    }
  ]
}
```

### 4. Review and Confirm Override

Inspect the proposed override:

```powershell
curl.exe "http://localhost:7071/api/admin/overrides/abc-123-..."
```

Expected: Full details including chunk text previews for source and target.

Confirm the override if it's correct:

```powershell
curl.exe -X POST "http://localhost:7071/api/admin/overrides/abc-123-..." `
  -H "Content-Type: application/json" `
  -d '{"action": "confirm", "reviewedBy": "admin@example.com"}'
```

Expected response:
```json
{
  "mappingId": "abc-123-...",
  "status": "Confirmed",
  "reviewedAt": "2025-12-24T...",
  "reviewedBy": "admin@example.com"
}
```

### 5. Test Precedence Resolution Without Overrides

Search for a rule that exists in multiple scopes (natural precedence):

```powershell
$body = @{
    query = "terrain"
    scopes = @("Canada", "Quebec", "Regional")
    associationId = "LBJEQ"
    top = 10
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/search" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Expected response should include:
- **Primary chunk**: highest precedence (Regional if exists, else Quebec, else Canada)
- **Alternate versions**: lower precedence versions marked as alternates
- Each result should have `isPrimary` flag

Example result:
```json
{
  "query": "terrain",
  "totalResults": 3,
  "ruleGroups": [
    {
      "ruleKey": "RULE_1_04",
      "primaryChunk": {
        "chunkId": "regional-chunk-...",
        "ruleKey": "RULE_1_04",
        "scope": "Regional",
        "docType": "RegionalFr",
        "associationId": "LBJEQ",
        "isPrimary": true,
        "textPreview": "Pour notre association, le terrain doit..."
      },
      "alternateChunks": [
        {
          "chunkId": "quebec-chunk-...",
          "scope": "Quebec",
          "isPrimary": false
        },
        {
          "chunkId": "canada-chunk-...",
          "scope": "Canada",
          "isPrimary": false
        }
      ]
    }
  ]
}
```

### 6. Test Precedence with Confirmed Override

Now test the same search after confirming an override (step 4):

```powershell
# Same search as step 5
$body = @{
    query = "terrain"
    scopes = @("Canada", "Quebec", "Regional")
    associationId = "LBML"
    top = 10
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/search" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Expected: If a confirmed override maps Regional->Quebec, the Regional chunk should still be primary (highest natural precedence), but metadata should indicate the confirmed override relationship.

### 7. Test Rejecting Override Proposals

Reject an incorrect proposal:

```powershell
curl.exe -X POST "http://localhost:7071/api/admin/overrides/xyz-789-..." `
  -H "Content-Type: application/json" `
  -d '{"action": "reject", "reviewedBy": "admin@example.com", "reason": "False positive - no actual override"}'
```

Expected:
```json
{
  "mappingId": "xyz-789-...",
  "status": "Rejected",
  "reviewedAt": "2025-12-24T...",
  "reviewedBy": "admin@example.com",
  "rejectionReason": "False positive - no actual override"
}
```

Verify rejected overrides don't affect search results.

### 8. Test Cross-Association Isolation

Search with a different association:

```powershell
$body = @{
    query = "terrain"
    associationId = "AUTRE-ASSOC"
    scopes = @("Canada", "Quebec", "Regional")
    top = 10
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/search" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Expected:
- Regional results for LBJEQ should NOT appear
- Only Regional results for AUTRE-ASSOC (if uploaded) should appear
- Overrides are association-scoped

### 9. Test Edge Cases

#### Case 1: Rule only in Canada (no precedence needed)
```powershell
# Search for a rule that only exists in Canada rulebook
$body = @{
    query = "some unique canada only rule"
    scopes = @("Canada", "Quebec", "Regional")
    associationId = "LBJEQ"
    top = 5
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:7071/api/search" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Expected: Single result, marked as primary, no alternates.

#### Case 2: Multiple rules match with same ruleKey
Verify precedence correctly identifies the highest-level version per association.

#### Case 3: Override detection false positives
Review proposed overrides and reject any that don't indicate actual rule replacements.

### 10. Test Override Detection Heuristics

Check that the OverrideDetector correctly identifies override indicators in text:

Common French patterns:
- "cette règle remplace"
- "en remplacement de"
- "au lieu de la règle"
- "exception à la règle"

Common English patterns:
- "this rule replaces"
- "overrides rule"
- "in place of rule"
- "exception to rule"

Manual verification:
1. Get chunk text with override indicator:
   ```powershell
   curl.exe "http://localhost:7071/api/admin/debug/chunk?jobId=...&chunkId=..."
   ```

2. Verify a proposed override exists for that chunk:
   ```powershell
   curl.exe "http://localhost:7071/api/admin/overrides?sourceChunkId=..."
   ```

### 11. Test Precedence Resolver Logic

Unit test scenarios (if implementing tests):

```csharp
// Scenario 1: Regional > Quebec > Canada
var chunks = new[] {
    CreateChunk(scope: "Canada", ruleKey: "RULE_1_04"),
    CreateChunk(scope: "Quebec", ruleKey: "RULE_1_04"),
    CreateChunk(scope: "Regional", ruleKey: "RULE_1_04", associationId: "LBJEQ")
};
var result = precedenceResolver.Resolve(chunks, associationId: "LBJEQ");
Assert.Equal("Regional", result.Primary.Scope);
Assert.Equal(2, result.Alternates.Count);

// Scenario 2: Quebec > Canada (no Regional)
var chunks = new[] {
    CreateChunk(scope: "Canada", ruleKey: "RULE_2_01"),
    CreateChunk(scope: "Quebec", ruleKey: "RULE_2_01")
};
var result = precedenceResolver.Resolve(chunks, associationId: null);
Assert.Equal("Quebec", result.Primary.Scope);
Assert.Equal(1, result.Alternates.Count);
```

### 12. Performance and Scaling

Test with realistic data:
- 100+ rules per rulebook
- 20-30 confirmed overrides
- Verify search latency remains < 200ms
- Check memory usage during precedence resolution

## Success Criteria

✅ OverrideMappings table stores Proposed/Confirmed/Rejected overrides  
✅ Ingestion detects potential overrides with confidence scores  
✅ Admin endpoints allow listing, reviewing, confirming, and rejecting overrides  
✅ PrecedenceResolver correctly identifies primary chunk by scope hierarchy  
✅ Search results group chunks by ruleKey with primary/alternate flags  
✅ Confirmed overrides are respected in precedence logic  
✅ Rejected overrides don't affect search results  
✅ Override detection has < 20% false positive rate (manual review)  
✅ Precedence resolution adds < 50ms to search latency  
✅ Cross-association isolation works correctly  

## Troubleshooting

### No override proposals detected
- Check ingestion logs for override detection step
- Verify chunk text contains override indicators
- Lower confidence threshold if too strict
- Review OverrideDetector heuristic patterns

### Wrong chunk selected as primary
- Check scope precedence logic in PrecedenceResolver
- Verify associationId filtering
- Check confirmed overrides aren't incorrectly applied

### Override confirmation doesn't affect results
- Verify OverrideMappings table update
- Check search pipeline calls PrecedenceResolver.GetConfirmedOverrides
- Clear any caching

### Performance degradation
- Check precedence resolution happens after initial retrieval (not per-chunk)
- Verify override lookups are batched
- Consider caching confirmed overrides per season/association

## Admin UI Considerations (Future)

For Milestone 3, focus on API endpoints. Future UI features:
- **Override proposal list**: table with source/target rule keys, confidence, status
- **Review modal**: side-by-side chunk text comparison
- **Bulk actions**: confirm/reject multiple proposals
- **RuleKeyPicker**: dropdown to manually map different target rule if detection is wrong

## Next Steps

Once Milestone 3 is verified:
- **Milestone 4**: Implement chat/RAG endpoint with strict grounding
- **Enhancement**: Add semantic search (embeddings) for better retrieval
- **Enhancement**: Build admin UI for override management
- **Enhancement**: Add override detection confidence tuning
