using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RulesApp.Shared;
using RulesApp.Shared.Helpers;

namespace RulesApp.Api.Services;

public interface IChunker
{
    List<RuleChunkDto> ChunkPages(
        List<PageDto> pages,
        string seasonId,
        string? associationId,
        DocType docType);
}

public class Chunker : IChunker
{
    private static readonly Regex RuleNumberPattern = new(@"^\s*(?:R[èe]gle|Rule)\s+(\d+(?:\.\d+)?)", 
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    
    private const int MaxChunkSize = 1000;
    private const int MinChunkSize = 200;
    
    public List<RuleChunkDto> ChunkPages(
        List<PageDto> pages,
        string seasonId,
        string? associationId,
        DocType docType)
    {
        var chunks = new List<RuleChunkDto>();
        var scopeLevel = docType.GetScopeLevel();
        var language = docType.GetLanguage();
        var rulebook = DocTypeHelpers.GetRulebookName(docType, associationId);
        var pdfPath = BlobPaths.GetRulesPdfPath(seasonId, associationId, docType);
        
        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Text))
            {
                continue;
            }
            
            var paragraphs = SplitIntoParagraphs(page.Text);
            
            foreach (var para in paragraphs)
            {
                if (para.Length < MinChunkSize)
                    continue;
                
                var chunkText = para.Length > MaxChunkSize 
                    ? para.Substring(0, MaxChunkSize) 
                    : para;
                
                // Try to detect rule number
                var ruleMatch = RuleNumberPattern.Match(chunkText);
                string? ruleNumberText = ruleMatch.Success ? ruleMatch.Groups[1].Value : null;
                string? ruleKey = ruleNumberText != null ? $"RULE_{ruleNumberText.Replace(".", "_")}" : null;
                
                // Extract title (first line or sentence)
                var title = ExtractTitle(chunkText);
                
                // Generate deterministic chunk ID
                var chunkId = GenerateChunkId(seasonId, associationId, docType, page.PageNumber, chunkText);
                
                chunks.Add(new RuleChunkDto(
                    ChunkId: chunkId,
                    ScopeLevel: scopeLevel,
                    AssociationId: associationId,
                    Rulebook: rulebook,
                    Language: language,
                    RuleNumberText: ruleNumberText,
                    RuleKey: ruleKey,
                    Title: title,
                    PageStart: page.PageNumber,
                    PageEnd: page.PageNumber,
                    PdfPath: pdfPath,
                    Text: chunkText
                ));
            }
        }
        
        return chunks;
    }
    
    private static List<string> SplitIntoParagraphs(string text)
    {
        // Simple split on double newlines or periods followed by capital letters
        var parts = text.Split(new[] { "\n\n", ". " }, StringSplitOptions.RemoveEmptyEntries);
        
        var paragraphs = new List<string>();
        var current = new StringBuilder();
        
        foreach (var part in parts)
        {
            current.Append(part);
            current.Append(" ");
            
            if (current.Length >= MinChunkSize)
            {
                paragraphs.Add(current.ToString().Trim());
                current.Clear();
            }
        }
        
        if (current.Length > 0)
        {
            paragraphs.Add(current.ToString().Trim());
        }
        
        return paragraphs;
    }
    
    private static string? ExtractTitle(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return null;
        
        var firstLine = lines[0].Trim();
        return firstLine.Length > 100 ? firstLine.Substring(0, 100) : firstLine;
    }
    
    private static string GenerateChunkId(string seasonId, string? associationId, DocType docType, int pageNumber, string text)
    {
        // Deterministic ID based on content
        var input = $"{seasonId}|{associationId ?? "GLOBAL"}|{docType}|{pageNumber}|{text.Substring(0, Math.Min(100, text.Length))}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
