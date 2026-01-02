using RulesApp.Shared;
using System.Text.RegularExpressions;

namespace RulesApp.Api.Services;

/// <summary>
/// Detects potential rule overrides using heuristic text analysis.
/// Looks for patterns indicating that a rule replaces or modifies another rule.
/// </summary>
public class OverrideDetector
{
    // French patterns indicating override
    private static readonly string[] FrenchOverridePatterns = new[]
    {
        @"(?:cette\s+règle\s+)?remplace\s+(?:la\s+)?règle",
        @"en\s+remplacement\s+de\s+(?:la\s+)?règle",
        @"au\s+lieu\s+de\s+(?:la\s+)?règle",
        @"exception\s+(?:à|a)\s+(?:la\s+)?règle",
        @"modifie\s+(?:la\s+)?règle",
        @"(?:cette\s+règle\s+)?annule\s+(?:la\s+)?règle",
        @"contrairement\s+(?:à|a)\s+(?:la\s+)?règle",
        @"différent\s+de\s+(?:la\s+)?règle"
    };

    // English patterns indicating override
    private static readonly string[] EnglishOverridePatterns = new[]
    {
        @"(?:this\s+rule\s+)?replaces\s+rule",
        @"in\s+place\s+of\s+rule",
        @"instead\s+of\s+rule",
        @"exception\s+to\s+rule",
        @"overrides\s+rule",
        @"modifies\s+rule",
        @"supersedes\s+rule",
        @"contrary\s+to\s+rule",
        @"different\s+from\s+rule"
    };

    /// <summary>
    /// Detects potential overrides in a set of chunks.
    /// Returns proposals for chunks that appear to override other rules.
    /// </summary>
    public List<OverrideProposal> DetectOverrides(
        List<RuleChunkDto> chunks,
        string seasonId,
        string? associationId)
    {
        var proposals = new List<OverrideProposal>();

        // Group chunks by ruleKey to find cross-scope duplicates
        var ruleGroups = chunks
            .Where(c => !string.IsNullOrEmpty(c.RuleKey))
            .GroupBy(c => c.RuleKey!);

        foreach (var group in ruleGroups)
        {
            var groupChunks = group.ToList();
            
            // Only check for overrides if we have multiple scope levels for same rule
            if (groupChunks.Count <= 1)
                continue;

            // Check each chunk for override indicators
            foreach (var chunk in groupChunks)
            {
                var detectionResults = AnalyzeChunkForOverride(chunk);
                
                if (detectionResults.HasOverrideIndicator)
                {
                    // Find potential target (rule being overridden)
                    var targets = FindOverrideTargets(chunk, groupChunks, detectionResults);
                    
                    foreach (var target in targets)
                    {
                        proposals.Add(new OverrideProposal(
                            SourceRuleKey: chunk.RuleKey!,
                            SourceChunkId: chunk.ChunkId,
                            SourceScope: chunk.ScopeLevel.ToString(),
                            TargetRuleKey: target.RuleKey!,
                            TargetChunkId: target.ChunkId,
                            TargetScope: target.ScopeLevel.ToString(),
                            Confidence: detectionResults.Confidence,
                            DetectionReason: detectionResults.Reason
                        ));
                    }
                }
            }
        }

        return proposals;
    }

    /// <summary>
    /// Analyzes a chunk's text for override indicators.
    /// </summary>
    private (bool HasOverrideIndicator, double Confidence, string Reason) AnalyzeChunkForOverride(
        RuleChunkDto chunk)
    {
        var text = chunk.Text.ToLowerInvariant();
        var patterns = chunk.Language == Language.FR 
            ? FrenchOverridePatterns 
            : EnglishOverridePatterns;

        foreach (var pattern in patterns)
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var match = regex.Match(text);
            
            if (match.Success)
            {
                // Calculate confidence based on pattern strength and context
                double confidence = 0.7; // Base confidence
                
                // Boost confidence if rule number is mentioned nearby
                if (Regex.IsMatch(text, @"\d+\.\d+"))
                    confidence += 0.1;
                
                // Boost confidence for explicit phrases
                if (pattern.Contains("remplace") || pattern.Contains("replaces"))
                    confidence += 0.1;

                // Cap at 0.95 (never 100% certain from heuristics alone)
                confidence = Math.Min(0.95, confidence);

                return (true, confidence, $"Text contains override pattern: '{match.Value}'");
            }
        }

        // Check for implicit override indicators
        // Regional/Quebec rules that mention specific differences
        if (chunk.ScopeLevel != ScopeLevel.Canada)
        {
            var implicitPatterns = chunk.Language == Language.FR
                ? new[] { "pour notre association", "spécifiquement", "contrairement" }
                : new[] { "for our association", "specifically", "unlike" };

            foreach (var pattern in implicitPatterns)
            {
                if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, 0.5, $"Text contains implicit override indicator: '{pattern}'");
                }
            }
        }

        return (false, 0.0, string.Empty);
    }

    /// <summary>
    /// Finds potential target chunks that are being overridden.
    /// Typically, a higher-level scope overrides a lower-level one (Regional > Quebec > Canada).
    /// </summary>
    private List<RuleChunkDto> FindOverrideTargets(
        RuleChunkDto source,
        List<RuleChunkDto> candidates,
        (bool HasOverrideIndicator, double Confidence, string Reason) detection)
    {
        var targets = new List<RuleChunkDto>();

        // Find chunks with same ruleKey but lower precedence
        var sourcePrecedence = GetScopePrecedence(source.ScopeLevel);

        foreach (var candidate in candidates)
        {
            // Skip self
            if (candidate.ChunkId == source.ChunkId)
                continue;

            // Only override lower-precedence rules
            var candidatePrecedence = GetScopePrecedence(candidate.ScopeLevel);
            if (candidatePrecedence < sourcePrecedence)
            {
                targets.Add(candidate);
            }
        }

        return targets;
    }

    /// <summary>
    /// Gets numeric precedence for scope levels.
    /// </summary>
    private int GetScopePrecedence(ScopeLevel scope)
    {
        return scope switch
        {
            ScopeLevel.Regional => 3,
            ScopeLevel.Quebec => 2,
            ScopeLevel.Canada => 1,
            _ => 0
        };
    }

    /// <summary>
    /// Extracts referenced rule numbers from override text.
    /// Example: "Cette règle remplace la règle 1.04" -> "1.04"
    /// </summary>
    public List<string> ExtractReferencedRules(string text, Language language)
    {
        var rules = new List<string>();
        
        // Common rule number patterns
        var patterns = new[]
        {
            @"règle\s+(\d+\.?\d*)",  // "règle 1.04"
            @"rule\s+(\d+\.?\d*)",   // "rule 1.04"
            @"(\d+\.\d+)",           // "1.04"
        };

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var ruleNum = match.Groups[1].Value;
                    if (!rules.Contains(ruleNum))
                        rules.Add(ruleNum);
                }
            }
        }

        return rules;
    }
}
