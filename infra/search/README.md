# Azure AI Search Index Schema

This folder contains the index schema definition for the RulesApp search functionality.

## Index: rules-active

The `rules-active` index stores all searchable rule chunks from uploaded rulebooks.

### Fields

| Field | Type | Description | Searchable | Filterable | Facetable |
|-------|------|-------------|------------|------------|-----------|
| `chunkId` | String | Unique identifier (key) | No | No | No |
| `seasonId` | String | Season identifier (e.g., "2025") | Yes | Yes | Yes |
| `associationId` | String | Association code (null for global) | Yes | Yes | Yes |
| `docType` | String | Document type (CanadaFr, QuebecFr, etc.) | Yes | Yes | Yes |
| `scope` | String | Scope level (Canada, Quebec, Regional) | Yes | Yes | Yes |
| `ruleKey` | String | Rule identifier (e.g., "RULE_1_01") | Yes | Yes | No |
| `ruleNumberText` | String | Display rule number (e.g., "1.01") | Yes | No | No |
| `title` | String | Rule title/heading | Yes | No | No |
| `text` | String | Full chunk text content | Yes | No | No |
| `pageStart` | Int32 | Starting page number | No | Yes | No |
| `pageEnd` | Int32 | Ending page number | No | Yes | No |
| `textLength` | Int32 | Character count of text | No | Yes | No |

### Filter Logic

The search implementation applies these filters:

1. **Season**: Always filter by active season (or requested seasonId)
2. **Scope**: Filter by requested scope levels (Canada/Quebec/Regional)
3. **Association**:
   - If `associationId` provided: Include docs for that association OR global docs (null)
   - If no `associationId`: Only include global docs (null)

### Usage

The index is created/updated via the `AdminSearchIndex` function:

```powershell
POST /api/admin/search-index
```

Chunks are automatically indexed during ingestion by the `RulesIngestWorker`.

### Schema Updates

To update the schema:

1. Modify `rules-active.index.json`
2. Update `SearchStore.cs` `CreateOrUpdateIndexAsync()` method
3. Redeploy and call the admin endpoint to apply changes
4. Re-run ingestion to populate new fields

**Note**: Adding new fields is safe. Removing or changing field types may require deleting and recreating the index.
