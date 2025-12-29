namespace RulesApp.Shared.Helpers;

public static class BlobPaths
{
    public static string GetRulesPdfPath(string seasonId, string? associationId, DocType docType)
    {
        var fileName = docType.ToString() + ".pdf";
        
        if (string.IsNullOrEmpty(associationId))
        {
            return $"rules/{seasonId}/global/{fileName}";
        }
        
        return $"rules/{seasonId}/{associationId}/{fileName}";
    }
    
    public static string GetIngestionPagesPath(string jobId)
    {
        return $"ingest/{jobId}/pages.json";
    }
    
    public static string GetIngestionChunksPath(string jobId)
    {
        return $"ingest/{jobId}/chunks.json";
    }
}
