using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
namespace RTMaquetaXR {
    internal sealed class ControlIconRun
    {
        internal int Index, RichIndex, BodySize;
        internal string Symbol;
    }
    internal static class ControlIconMarkup
    {
        static readonly Regex richTags=new Regex(@"<[^>]+>",RegexOptions.CultureInvariant);
        static readonly Regex controls = new Regex(
            @"\[((?:LT|RT|LG|RG|L|R|A|B|X|Y|REST|MENU|F[1-4])(?::(?:PRESS|XY|X|Y|CW|CCW|ROTATE))?)\]|\b(?:(?:LEFT|RIGHT)\s+(?:stick|trigger|grip|click)|(?:stick|gatillo|grip)\s+(?:izquierdo|derecho|izq\.?|der\.?)|[LR](?:T|G|GR)|F[1-4])\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // A by itself is also an English article. Only explicit button syntax
        // and unambiguous control labels may become the A button.
        static readonly Regex buttons = new Regex(@"\b((?i:button|botón|press|pulsa|hold|mantén|mantener|release|suelta))\s+([ABXY])\b", RegexOptions.CultureInvariant);
        internal static string Prepare(string source, List<ControlIconRun> runs, int bodySize = 0) => Prepare(source,runs,bodySize,false);
        internal static string Prepare(string source, List<ControlIconRun> runs, int bodySize, bool meshMarkers)
        {
            runs.Clear(); source = source ?? "";
            // Explicit state tokens are stable across languages. Durations and
            // x2 remain readable text; the glyph carries button/axis identity.
            source=Regex.Replace(source,@"\b(LEFT|RIGHT)[- ]stick\s+click\b",m=>m.Groups[1].Value.Equals("LEFT",StringComparison.OrdinalIgnoreCase)?"[L:PRESS]":"[R:PRESS]",RegexOptions.IgnoreCase);
            source=Regex.Replace(source,@"\b(?:clic|pulsación)(?:\s+(?:corta|larga))?\s+(?:del\s+)?stick\s+(izquierdo|derecho)\b",m=>m.Groups[1].Value.Equals("izquierdo",StringComparison.OrdinalIgnoreCase)?"[L:PRESS]":"[R:PRESS]",RegexOptions.IgnoreCase);
            source=Regex.Replace(source,@"\b(?:left thumbrest|zona capacitiva izquierda|apoyo capacitivo izquierdo)\b","[REST]",RegexOptions.IgnoreCase);

            source = Regex.Replace(source, @"\b(LEFT|RIGHT)[- ](stick|trigger|grip)\b", "$1 $2", RegexOptions.IgnoreCase);
            source = Regex.Replace(source, @"\b(LEFT|RIGHT) grip\s*(?:\+|and)\s*(?:the )?trigger\b", m => m.Groups[1].Value.Equals("LEFT",StringComparison.OrdinalIgnoreCase) ? "[LG]+[LT]" : "[RG]+[RT]", RegexOptions.IgnoreCase);
            source = Regex.Replace(source, @"\bgrip\s+(izquierdo|derecho)\s*(?:\+|y)\s*(?:el )?gatillo\b", m => m.Groups[1].Value.Equals("izquierdo",StringComparison.OrdinalIgnoreCase) ? "[LG]+[LT]" : "[RG]+[RT]", RegexOptions.IgnoreCase);
            source = Regex.Replace(source, @"\b(?:Either grip|one grip|cualquiera de los grips|un grip)\b", "[LG]/[RG]", RegexOptions.IgnoreCase);
            source = Regex.Replace(source, @"\b(?:two grips|dos grips)\b", "[LG]+[RG]", RegexOptions.IgnoreCase);
            source = Regex.Replace(source, @"\b([ABXY])\s+(button|botón)\b", "[$1] $2");
            source = Regex.Replace(source, @"(?<!\[)\b([BXY])\b(?!\])", "[$1]");
            source = Regex.Replace(source, @"\bA(?=\s+(?:also clicks|también hace clic))", "[A]");
            source = Regex.Replace(source, @"\b(LEFT|RIGHT)(?=\s+(?:up/down|left/right))", "$1 stick", RegexOptions.IgnoreCase);

            source = Regex.Replace(source, @"\b(?:both grips and triggers|both triggers and both grips|both triggers \+ grips|ambos gatillos y ambos grips|ambos gatillos \+ grips)\b", "[LT]+[LG]+[RT]+[RG]", RegexOptions.IgnoreCase);
            source = Regex.Replace(source, @"\b(?:both grips|ambos grips|los dos grips)\b", "[LG]+[RG]", RegexOptions.IgnoreCase);
            source = Regex.Replace(source, @"\b([ABXY])(?=\s*(?:/|·|\bor\b|\bo\b|enters|returns|cancels|closes|goes|confirms|activa|cierra|vuelve|cancela|confirma))", "[$1]");
            source = Regex.Replace(source,@"\bL-STICK\b","LEFT stick",RegexOptions.IgnoreCase);
            source = Regex.Replace(source,@"\bR-STICK\b","RIGHT stick",RegexOptions.IgnoreCase);
            source = source.Replace("A / TRIGGER", "[A] / [RT]").Replace("A / GATILLO", "[A] / [RT]");
            source = Regex.Replace(source,@"\bRIGHT-STICK\b","RIGHT stick",RegexOptions.IgnoreCase);
            source = Regex.Replace(source,@"\bLEFT-STICK\b","LEFT stick",RegexOptions.IgnoreCase);
            source = buttons.Replace(source, m => m.Groups[1].Value + " [" + m.Groups[2].Value.ToUpperInvariant() + "]");
            if (source == "A" || source == "B" || source == "X" || source == "Y" || source == "L" || source == "R") source = "[" + source + "]";
            var result = new StringBuilder(); int start = 0;
            foreach (Match match in controls.Matches(source))
            {
                result.Append(source, start, match.Index - start);
                string symbol = Symbol(match.Value);
                if (bodySize > 0) result.Append("<size=").Append(ControlIconSizing65.Slot(bodySize)).Append(">");
                runs.Add(new ControlIconRun { Index = result.Length, RichIndex=richTags.Replace(result.ToString(),"").Length, Symbol = symbol, BodySize=bodySize });
                if(meshMarkers) result.Append("<color=#01").Append((runs.Count-1).ToString("X2")).Append("FDFF>M</color>");
                else result.Append('\u00a0', 4);
                if(bodySize>0)result.Append("</size>"); start = match.Index + match.Length;
            }
            result.Append(source, start, source.Length - start); return result.ToString();
        }
        internal static string Symbol(string value)
        {
            value = value.Trim('[', ']').ToUpperInvariant();
            if (value.Contains("LEFT") || value.Contains("IZQ")) return (value.Contains("STICK") || value.Contains("CLICK")) ? "L" : value.Contains("GRIP") ? "LG" : "LT";
            if (value.Contains("RIGHT") || value.Contains("DER")) return (value.Contains("STICK") || value.Contains("CLICK")) ? "R" : value.Contains("GRIP") ? "RG" : "RT";
            return value.Replace("GR", "G");
        }
    }

}
