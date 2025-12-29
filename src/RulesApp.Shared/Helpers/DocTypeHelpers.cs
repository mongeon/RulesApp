namespace RulesApp.Shared.Helpers;

public static class DocTypeHelpers
{
    public static ScopeLevel GetScopeLevel(this DocType docType)
    {
        return docType switch
        {
            DocType.CanadaFr => ScopeLevel.Canada,
            DocType.CanadaEn => ScopeLevel.Canada,
            DocType.QuebecFr => ScopeLevel.Quebec,
            DocType.RegionalFr => ScopeLevel.Regional,
            _ => throw new ArgumentOutOfRangeException(nameof(docType))
        };
    }
    
    public static Language GetLanguage(this DocType docType)
    {
        return docType switch
        {
            DocType.CanadaFr => Language.FR,
            DocType.CanadaEn => Language.EN,
            DocType.QuebecFr => Language.FR,
            DocType.RegionalFr => Language.FR,
            _ => throw new ArgumentOutOfRangeException(nameof(docType))
        };
    }
    
    public static bool RequiresAssociation(this DocType docType)
    {
        return docType == DocType.RegionalFr;
    }
    
    public static string GetRulebookName(DocType docType, string? associationId)
    {
        return docType switch
        {
            DocType.CanadaFr => "Canada (FR)",
            DocType.CanadaEn => "Canada (EN)",
            DocType.QuebecFr => "Quebec (FR)",
            DocType.RegionalFr => $"Regional:{associationId}",
            _ => throw new ArgumentOutOfRangeException(nameof(docType))
        };
    }
}
