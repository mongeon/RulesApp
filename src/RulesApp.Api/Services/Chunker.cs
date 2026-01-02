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
    // Match multiple patterns:
    // 1. Standard: "1.04 THE PLAYING FIELD" or "Règle 1.04"
    // 2. Quebec format: "34 55.7 - REFUS DE QUITTER" (page number, then rule number, then dash)
    // 3. With subsections: "5.09(a)" or "105.2.1"
    // 4. French regional: "2.3. Annulation et remise de partie" (number, period, space, title)
    // Matches at: start of string, after newline, OR after whitespace
    private static readonly Regex RuleNumberPattern = new(
        @"(?:^|\n|\s)(?:\d+\s+)?(?:R[èe]gle\s+)?(\d+\.\d+(?:\.\d+)?(?:\s*\([a-z]\))?)\s*(?:-|\.?\s+[A-ZÀÂÇÉÈÊËÎÏÔÛÙÜŸŒÆ])", 
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    
    private const int MaxChunkSize = 4000;   // Allow larger chunks to preserve rule context
    private const int MinChunkSize = 200;
    private const int TargetChunkSize = 2500; // Higher target = fewer splits, better rule completeness
    
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
        
        // Step 1: Build complete text with page markers to track page boundaries
        var pageTexts = new List<(int pageNumber, string text)>();
        foreach (var page in pages)
        {
            if (!string.IsNullOrWhiteSpace(page.Text))
            {
                pageTexts.Add((page.PageNumber, page.Text));
            }
        }
        
        if (pageTexts.Count == 0)
            return chunks;
        
        // Step 2: Extract sections/paragraphs while tracking page ranges
        var sections = ExtractSectionsWithPageRanges(pageTexts);
        
        // Step 3: Create chunks from sections
        foreach (var section in sections)
        {
            if (section.text.Length < MinChunkSize)
                continue;
            
            // Try to detect rule number from the ORIGINAL section
            // This rule number will be propagated to ALL sub-chunks if splitting occurs
            var ruleMatch = RuleNumberPattern.Match(section.text);
            string? ruleNumberText = ruleMatch.Success ? ruleMatch.Groups[1].Value : null;
            string? ruleKey = ruleNumberText?.Trim();
            
            // Extract title from ORIGINAL section (will be propagated to all sub-chunks)
            // This ensures all fragments of a split rule share the same title
            var title = ExtractTitle(section.text);
            
            var trimmed = section.text.Trim();
            
            // If section is too large, split at sentence boundaries but PROPAGATE rule number
            if (trimmed.Length > MaxChunkSize)
            {
                var subSections = SplitAtSentenceBoundaries(trimmed, MaxChunkSize);
                foreach (var subSection in subSections)
                {
                    if (subSection.Length >= MinChunkSize)
                    {
                        // Generate deterministic chunk ID (includes sub-section text for uniqueness)
                        var chunkId = GenerateChunkId(seasonId, associationId, docType, section.pageStart, subSection);
                        
                        chunks.Add(new RuleChunkDto(
                            ChunkId: chunkId,
                            ScopeLevel: scopeLevel,
                            AssociationId: associationId,
                            Rulebook: rulebook,
                            Language: language,
                            RuleNumberText: ruleNumberText,    // PROPAGATED from original section
                            RuleKey: ruleKey,                   // PROPAGATED from original section
                            Title: title,                       // Same title for all sub-chunks of same rule
                            PageStart: section.pageStart,
                            PageEnd: section.pageEnd,
                            PdfPath: pdfPath,
                            Text: subSection
                        ));
                    }
                }
            }
            else
            {
                // Single chunk (no splitting needed)
                var chunkId = GenerateChunkId(seasonId, associationId, docType, section.pageStart, section.text);
                
                chunks.Add(new RuleChunkDto(
                    ChunkId: chunkId,
                    ScopeLevel: scopeLevel,
                    AssociationId: associationId,
                    Rulebook: rulebook,
                    Language: language,
                    RuleNumberText: ruleNumberText,
                    RuleKey: ruleKey,
                    Title: title,
                    PageStart: section.pageStart,
                    PageEnd: section.pageEnd,
                    PdfPath: pdfPath,
                    Text: section.text
                ));
            }
        }
        
        return chunks;
    }
    
    /// <summary>
    /// Extracts sections from pages while tracking page ranges.
    /// Handles cross-page paragraphs and intelligently splits large sections.
    /// </summary>
    private static List<(string text, int pageStart, int pageEnd)> ExtractSectionsWithPageRanges(
        List<(int pageNumber, string text)> pageTexts)
    {
        var sections = new List<(string text, int pageStart, int pageEnd)>();
        
        // Build combined text with page number markers
        var combined = new StringBuilder();
        var pageMap = new List<(int charPos, int pageNumber)>();  // Track where each page starts
        
        foreach (var (pageNum, text) in pageTexts)
        {
            pageMap.Add((combined.Length, pageNum));
            combined.Append(text);
            combined.Append("\n\n");  // Page separator
        }
        
        var fullText = combined.ToString();
        
        // Split into logical sections
        var rawSections = SplitIntoSections(fullText);
        
        // Map back to page ranges and split if needed
        foreach (var section in rawSections)
        {
            if (string.IsNullOrWhiteSpace(section))
                continue;
            
            var trimmed = section.Trim();
            if (trimmed.Length < MinChunkSize)
                continue;
            
            // Find page range for this section
            var sectionStart = fullText.IndexOf(trimmed);
            var sectionEnd = sectionStart + trimmed.Length;
            var pageRange = FindPageRange(pageMap, sectionStart, sectionEnd);
            
            // If section is too large, split at sentence boundaries
            if (trimmed.Length > MaxChunkSize)
            {
                var subSections = SplitAtSentenceBoundaries(trimmed, MaxChunkSize);
                foreach (var subSection in subSections)
                {
                    if (subSection.Length >= MinChunkSize)
                    {
                        sections.Add((subSection, pageRange.start, pageRange.end));
                    }
                }
            }
            else
            {
                sections.Add((trimmed, pageRange.start, pageRange.end));
            }
        }
        
        return sections;
    }
    
    /// <summary>
    /// Splits text into logical sections (by rule headers, double newlines, etc.)
    /// Preserves structure across page boundaries.
    /// </summary>
    private static List<string> SplitIntoSections(string text)
    {
        var sections = new List<string>();
        
        // Split on rule number patterns (highest priority)
        var ruleMatches = RuleNumberPattern.Matches(text);
        
        if (ruleMatches.Count == 0)
        {
            // No rules detected, split on paragraph breaks
            var parts = text.Split(new[] { "\n\n", "\n\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            sections.AddRange(parts);
            return sections;
        }
        
        // Split on rule boundaries
        int lastPos = 0;
        foreach (Match match in ruleMatches)
        {
            // Include content from last position to this rule match
            if (match.Index > lastPos)
            {
                var section = text.Substring(lastPos, match.Index - lastPos);
                if (!string.IsNullOrWhiteSpace(section))
                {
                    sections.Add(section);
                }
            }
            lastPos = match.Index;
        }
        
        // Add remaining content
        if (lastPos < text.Length)
        {
            var remaining = text.Substring(lastPos);
            if (!string.IsNullOrWhiteSpace(remaining))
            {
                sections.Add(remaining);
            }
        }
        
        return sections;
    }
    
    /// <summary>
    /// Intelligently splits large sections at sentence boundaries to preserve meaning.
    /// </summary>
    private static List<string> SplitAtSentenceBoundaries(string text, int targetSize)
    {
        var chunks = new List<string>();
        var sentences = Regex.Split(text, @"(?<=[.!?\n])\s+");
        
        var current = new StringBuilder();
        foreach (var sentence in sentences)
        {
            if (string.IsNullOrWhiteSpace(sentence))
                continue;
            
            var sentenceTrimmed = sentence.Trim();
            
            // If adding this sentence exceeds target, save current and start new chunk
            if (current.Length > 0 && current.Length + sentenceTrimmed.Length > targetSize)
            {
                var chunk = current.ToString().Trim();
                if (chunk.Length >= MinChunkSize)
                {
                    chunks.Add(chunk);
                }
                current.Clear();
            }
            
            current.Append(sentenceTrimmed).Append(" ");
        }
        
        // Add final chunk
        if (current.Length > 0)
        {
            var finalChunk = current.ToString().Trim();
            if (finalChunk.Length >= MinChunkSize)
            {
                chunks.Add(finalChunk);
            }
        }
        
        return chunks;
    }
    
    /// <summary>
    /// Finds which pages a text span covers based on character positions.
    /// </summary>
    private static (int start, int end) FindPageRange(
        List<(int charPos, int pageNumber)> pageMap,
        int sectionStart,
        int sectionEnd)
    {
        var startPage = pageMap.LastOrDefault(p => p.charPos <= sectionStart).pageNumber;
        var endPage = pageMap.LastOrDefault(p => p.charPos <= sectionEnd).pageNumber;
        
        if (startPage == 0) startPage = pageMap[0].pageNumber;
        if (endPage == 0) endPage = pageMap[0].pageNumber;
        
        return (startPage, endPage);
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
