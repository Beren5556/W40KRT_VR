using System;
using System.Collections.Generic;
using System.Globalization;

namespace RTMaquetaXR
{
    // Only mod-owned source strings enter this catalogue. Never translate native
    // character, item, ability or dialogue text through this dictionary.
    internal static partial class ModLocalization
    {
        static readonly Dictionary<string, string> spanish = CreateCatalogue();
        static readonly CultureInfo spanishCulture = CultureInfo.GetCultureInfo("es-ES");
        static readonly CultureInfo englishCulture = CultureInfo.GetCultureInfo("en-GB");
        internal static int Language { get; private set; }
        internal static int Revision { get; private set; }
        internal static CultureInfo Culture => Language == 1 ? spanishCulture : englishCulture;
        internal static IEnumerable<KeyValuePair<string, string>> Entries => spanish;

        static Dictionary<string, string> CreateCatalogue()
        {
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            AddCore(entries); AddOverlay(entries); AddGuides(entries); AddSurfaces(entries); AddSpace(entries); AddContextHints(entries); AddRelease48(entries); AddEngineCadence(entries);
            AddRelease50Input(entries); AddRelease50Radials(entries); AddRelease50Performance(entries); AddRelease50Neural(entries); AddRelease51(entries); AddRelease52(entries); AddRelease55(entries); AddRelease56(entries); AddRelease57(entries);
            AddRelease58(entries);
            AddRelease59(entries); AddGuide59(entries);
            AddRelease61(entries); AddRelease62(entries); AddRelease63(entries); AddGuide64(entries); AddRelease65(entries); AddRelease71(entries); AddRelease72(entries); AddRelease73(entries); AddRelease74(entries);
            AddRelease77(entries); AddRelease80(entries); AddRelease81(entries);
            return entries;
        }
        static partial void AddCore(Dictionary<string, string> strings);
        static partial void AddOverlay(Dictionary<string, string> strings);
        static partial void AddGuides(Dictionary<string, string> strings);
        static partial void AddSurfaces(Dictionary<string, string> strings);
        static partial void AddSpace(Dictionary<string, string> strings);
        static partial void AddContextHints(Dictionary<string, string> strings);
        static partial void AddRelease48(Dictionary<string, string> strings);
        static partial void AddEngineCadence(Dictionary<string, string> strings);
        static partial void AddRelease50Input(Dictionary<string, string> strings);
        static partial void AddRelease50Radials(Dictionary<string, string> strings);
        static partial void AddRelease50Performance(Dictionary<string, string> strings);
        static partial void AddRelease50Neural(Dictionary<string, string> strings);
        static partial void AddRelease51(Dictionary<string, string> strings);
        static partial void AddRelease52(Dictionary<string, string> strings);
        static partial void AddRelease55(Dictionary<string, string> strings);
        static partial void AddRelease56(Dictionary<string, string> strings);
        static partial void AddRelease57(Dictionary<string, string> strings);
        static partial void AddRelease58(Dictionary<string, string> strings);
        static partial void AddRelease59(Dictionary<string, string> strings);
        static partial void AddGuide59(Dictionary<string, string> strings);
        static partial void AddRelease61(Dictionary<string, string> strings);
        static partial void AddRelease62(Dictionary<string, string> strings);
        static partial void AddRelease63(Dictionary<string, string> strings);
        static partial void AddGuide64(Dictionary<string, string> strings);
        static partial void AddRelease65(Dictionary<string, string> strings);
        static partial void AddRelease71(Dictionary<string, string> strings);
        static partial void AddRelease72(Dictionary<string, string> strings);
        static partial void AddRelease73(Dictionary<string, string> strings);
        static partial void AddRelease74(Dictionary<string, string> strings);
        static partial void AddRelease77(Dictionary<string, string> strings);
        static partial void AddRelease80(Dictionary<string, string> strings);
        static partial void AddRelease81(Dictionary<string, string> strings);
        static void Add(Dictionary<string, string> entries, string source, string translation)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(translation))
                throw new ArgumentException("Empty mod translation");
            if (entries.TryGetValue(source, out string existing) && existing != translation)
                throw new ArgumentException("Conflicting mod translation: " + source);
            entries[source] = translation;
        }
        internal static string Text(string source) => Language == 1 && source != null && spanish.TryGetValue(source, out string value) ? value : source;
        internal static string Format(string source, params object[] args) => string.Format(Culture, Text(source), args);
        // Explicit boundary for cached mod/native diagnostics only. Preserve raw
        // backend detail and numbers; never use this for game-provided UI names.
        internal static string DiagnosticText(string source)
        {
            if (Language != 1 || string.IsNullOrEmpty(source)) return source;
            source = source.Replace(". Select the same GPU for the game and headset application, then restart.", ". Selecciona la misma GPU para el juego y la aplicación del visor y reinicia.")
                .Replace(". Restart the game on the required GPU.", ". Reinicia el juego usando la GPU requerida.")
                .Replace(". This device cannot satisfy the runtime graphics requirement.", ". Esta GPU no satisface el requisito gráfico del runtime.");
            string translated = Text(source);
            if (translated != source) return translated;
            if (source.StartsWith("VDXR: pairs=", StringComparison.Ordinal) || source.StartsWith("Meta Quest Link: pairs=", StringComparison.Ordinal))
                return source.Replace("pairs=", "pares=").Replace("flat=", "plano=").Replace("errors=", "errores=");
            int reason = source.IndexOf(" (native reason ", StringComparison.Ordinal);
            if (reason > 0)
                return DiagnosticText(source.Substring(0, reason)) + " (" + Text("native reason") + " " + source.Substring(reason + 16);
            int split = source.IndexOf(": ", StringComparison.Ordinal);
            if (split > 0)
                return Text(source.Substring(0, split)) + ": " + DiagnosticText(source.Substring(split + 2));
            return source;
        }
        internal static void SetLanguage(int language)
        {
            language = language == 1 ? 1 : 0;
            if (Language == language) return;
            Language = language; unchecked { ++Revision; }
        }
        // The game uses Locale.esES. Accept the equivalent locale spelling too;
        // unknown/missing locales and every other game language use English.
        internal static int DefaultLanguageForGameLocale(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale)) return 0;
            string code = locale.Trim();
            return code.Equals("esES", StringComparison.OrdinalIgnoreCase) ||
                code.Equals("es-ES", StringComparison.OrdinalIgnoreCase) ||
                code.Equals("es_ES", StringComparison.OrdinalIgnoreCase) ||
                code.Equals("es", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        internal static int ResolveLanguage(int preference, string gameLocale) => preference == -1 ? DefaultLanguageForGameLocale(gameLocale) : preference == 1 ? 1 : 0;
    }
}
