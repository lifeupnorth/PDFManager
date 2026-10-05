using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PDFManager.Services
{
    /// <summary>What the app remembers between sessions.</summary>
    public class SettingsData
    {
        // Sidebar sections the user has collapsed, by name
        public List<string> CollapsedSections { get; set; } = new List<string>();
    }

    /// <summary>
    /// Reads and writes settings.json next to the error logs. Like the error log it never throws:
    /// a missing, unreadable or unwritable file just means the defaults.
    /// </summary>
    public static class AppSettings
    {
        public static string Folder { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PDFManager");

        public static string FilePath => Path.Combine(Folder, "settings.json");

        public static SettingsData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(FilePath)) ?? new SettingsData();
            }
            catch
            {
                // A damaged file falls back to the defaults and is replaced on the next save
            }
            return new SettingsData();
        }

        public static void Save(SettingsData settings)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                // Write a temporary file and move it into place, so a crash cannot leave half a file
                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tempPath, FilePath, overwrite: true);
            }
            catch
            {
                // Not being able to remember a preference is not worth interrupting the user
            }
        }
    }
}
