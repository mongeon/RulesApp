using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using RulesApp.Shared;

namespace RulesApp.Api.Services;

public interface IPdfExtractor
{
    Task<List<PageDto>> ExtractPagesAsync(Stream pdfStream, CancellationToken ct = default);
}

public class PdfExtractor : IPdfExtractor
{
    public Task<List<PageDto>> ExtractPagesAsync(Stream pdfStream, CancellationToken ct = default)
    {
        var pages = new List<PageDto>();
        
        using var document = PdfDocument.Open(pdfStream);
        
        foreach (var page in document.GetPages())
        {
            var text = ExtractTextFromPage(page);
            var normalized = NormalizeText(text);
            
            pages.Add(new PageDto(
                PageNumber: page.Number,
                Text: normalized,
                CharCount: normalized.Length
            ));
        }
        
        return Task.FromResult(pages);
    }
    
    private static string ExtractTextFromPage(Page page)
    {
        var sb = new StringBuilder();
        var words = page.GetWords();
        
        foreach (var word in words)
        {
            sb.Append(word.Text);
            sb.Append(' ');
        }
        
        return sb.ToString();
    }
    
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        
        // Normalize whitespace
        text = Regex.Replace(text, @"\s+", " ");
        
        // Trim
        text = text.Trim();
        
        return text;
    }
}
