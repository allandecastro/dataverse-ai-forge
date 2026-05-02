namespace DataverseForge.Application.Agents;

/// <summary>
/// Maps a Dataverse <c>BaseLanguageLcid</c> to a system-prompt directive that
/// asks the LLM to reply in that language. Without this, every agent answers in
/// English even when the env runs on a French / German / Spanish / etc. tenant
/// — jarring when the table display names already come back localized.
///
/// The directive prepends the system prompt so the model sees it before any
/// agent-specific role text. We bias toward common Dataverse-supported languages;
/// uncommon LCIDs fall back to English silently.
/// </summary>
public static class LanguageDirective
{
    /// <summary>English directive — used as the fallback for unknown LCIDs and
    /// also when the env language hasn't been resolved yet (LCID = 0).</summary>
    public const string EnglishDirective = "Respond in English.";

    private static readonly Dictionary<int, string> LcidToLanguage = new()
    {
        // Canonical Dataverse-supported languages. Keep this list curated — adding
        // every Windows LCID would make the table noisy without behavioral value.
        [1025] = "Arabic",
        [1028] = "Chinese (Traditional)",
        [1029] = "Czech",
        [1030] = "Danish",
        [1031] = "German",
        [1032] = "Greek",
        [1033] = "English",
        [1035] = "Finnish",
        [1036] = "French",
        [1037] = "Hebrew",
        [1038] = "Hungarian",
        [1040] = "Italian",
        [1041] = "Japanese",
        [1042] = "Korean",
        [1043] = "Dutch",
        [1044] = "Norwegian",
        [1045] = "Polish",
        [1046] = "Portuguese (Brazil)",
        [1048] = "Romanian",
        [1049] = "Russian",
        [1050] = "Croatian",
        [1051] = "Slovak",
        [1053] = "Swedish",
        [1054] = "Thai",
        [1055] = "Turkish",
        [1057] = "Indonesian",
        [1058] = "Ukrainian",
        [1060] = "Slovenian",
        [1061] = "Estonian",
        [1062] = "Latvian",
        [1063] = "Lithuanian",
        [1066] = "Vietnamese",
        [2052] = "Chinese (Simplified)",
        [2070] = "Portuguese (Portugal)",
        [3082] = "Spanish",
    };

    /// <summary>
    /// Returns "Respond in {Language}." for known LCIDs. For LCID 0, unknown
    /// LCIDs, and English (1033), returns <see cref="EnglishDirective"/>.
    /// </summary>
    public static string ForLcid(int lcid)
    {
        if (lcid <= 0) return EnglishDirective;
        if (LcidToLanguage.TryGetValue(lcid, out var lang))
        {
            return $"Respond in {lang}.";
        }
        return EnglishDirective;
    }
}
