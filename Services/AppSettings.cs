using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Text.Json.Serialization;

namespace PDFManager.Services
{
    public enum AppTheme { System, Light, Dark }

    /// <summary>What the app remembers between sessions.</summary>
    public class SettingsData
    {
        // Sidebar sections the user has collapsed, by name
        public List<string> CollapsedSections { get; set; } = new List<string>();

        // Saved Batch settings, in the order they were created
        public List<BatchPreset> BatchPresets { get; set; } = new List<BatchPreset>();

        // "System", "Light" or "Dark". Dark until the user chooses, which is how the app always looked
        public string Theme { get; set; } = nameof(AppTheme.Dark);

        [JsonIgnore]
        public AppTheme ThemeChoice
        {
            get => Enum.TryParse(Theme, ignoreCase: true, out AppTheme theme) && Enum.IsDefined(theme) ? theme : AppTheme.Dark;
            set => Theme = value.ToString();
        }
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
                {
                    // Shared for writing and deleting too, so a read while Save moves the new file
                    // into place does not fail. A failed read means the defaults, and a caller that
                    // changes one setting would then save those over everything else. The text is
                    // read in one go and the file closed before parsing, because Save cannot
                    // replace the file while it is open
                    string json;
                    using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream))
                        json = reader.ReadToEnd();
                    return Repaired(JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData());
                }
            }
            catch
            {
                // A damaged file falls back to the defaults and is replaced on the next save
            }
            return new SettingsData();
        }

        // Windows refuses to replace a file while it is open, even for a moment's read by another
        // window (or a test running in parallel), so retry briefly rather than lose the save
        private static void MoveIntoPlace(string tempPath)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(tempPath, FilePath, overwrite: true);
                    return;
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 10)
                {
                    Thread.Sleep(10);
                }
            }
        }

        // A hand-edited file can hold null where the types promise a value. Put the defaults back
        // here, so the code that reads the settings can trust them; the presets' own values are
        // put back in range by BatchPreset.Normalized
        private static SettingsData Repaired(SettingsData settings)
        {
            settings.CollapsedSections = settings.CollapsedSections?.Where(name => name != null).ToList() ?? new List<string>();
            settings.BatchPresets = settings.BatchPresets?.Where(preset => preset != null).ToList() ?? new List<BatchPreset>();
            settings.Theme ??= nameof(AppTheme.Dark);
            return settings;
        }

        public static void Save(SettingsData settings)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                // Write a temporary file and move it into place, so a crash cannot leave half a file
                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                MoveIntoPlace(tempPath);
            }
            catch
            {
                // Not being able to remember a preference is not worth interrupting the user
            }
        }
    }
}
