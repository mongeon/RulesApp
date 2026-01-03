using System;

namespace RulesApp.Web;

public class LanguageState
{
    public string Current { get; private set; } = "FR";

    public event Action? OnChange;

    public void SetLanguage(string language)
    {
        var normalized = string.IsNullOrWhiteSpace(language) ? "FR" : language.Trim().ToUpperInvariant();
        if (normalized != "FR" && normalized != "EN")
        {
            normalized = "FR";
        }

        if (normalized == Current)
        {
            return;
        }

        Current = normalized;
        OnChange?.Invoke();
    }
}