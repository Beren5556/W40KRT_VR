using System;
using System.IO;

namespace RTMaquetaXR
{
    // Only diagnostic preferences migrate. The same files keep all other values.
    public static class DiagnosticsDefaults77
    {
        public static string Apply(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                int equals = line.IndexOf('=');
                if (equals > 0 && line.Substring(0, equals).Trim() == "diagnosticsRevision77" &&
                    int.TryParse(line.Substring(equals + 1).Trim(), out int revision) && revision >= 77)
                    return text;
            }
            return SettingsFiles76.Merge(text, "diagnosticsRevision77=77\nallDiagnosticsEnabled=False\ndetailedProfiling=False\n");
        }
        public static void Migrate(string directory, int slots)
        {
            for (int index = -1; index <= slots; index++)
            {
                string suffix = index == -1 ? "settings" : index == 0 ? "custom" : "view" + index;
                string path = SettingsFiles76.PathFor(directory, suffix);
                if (!File.Exists(path)) continue;
                string before = File.ReadAllText(path), after = Apply(before);
                if (before == after) continue;
                if (!SettingsFiles76.Valid(before)) throw new InvalidDataException(path);
                if (!File.Exists(path + ".before77.bak")) File.Copy(path, path + ".before77.bak");
                SettingsFiles76.Save(path, after);
            }
        }
    }
}
