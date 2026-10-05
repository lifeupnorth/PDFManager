#nullable enable
using System;
using System.Linq;

namespace PDFManager.Services
{
    /// <summary>
    /// A named set of Batch settings, kept in settings.json. Passwords are deliberately not part
    /// of it: the settings file is plain text, so Protect and Unlock presets ask for theirs each time.
    /// </summary>
    public class BatchPreset
    {
        public const int MaxNameLength = 40;

        public string Name { get; set; } = string.Empty;
        // A BatchAction name: Rotate, Watermark, Extract, Protect or Unlock
        public string Action { get; set; } = "Rotate";
        public int RotationAngle { get; set; } = 90;
        public string WatermarkText { get; set; } = "CONFIDENTIAL";
        public bool WatermarkDiagonal { get; set; } = true;
        public int WatermarkOpacity { get; set; } = 30;
        public string PageSpec { get; set; } = "1";
        // Empty means next to each original
        public string OutputFolder { get; set; } = string.Empty;

        private static readonly int[] Angles = { 90, 180, 270 };
        private static readonly int[] Opacities = { 15, 30, 50 };

        // A copy with anything a hand-edited or damaged file could get wrong put back in range,
        // so a preset can only ever choose options the Batch tool offers
        public BatchPreset Normalized() => new BatchPreset
        {
            Name = CleanName(Name),
            Action = Action ?? "Rotate",
            RotationAngle = Angles.Contains(RotationAngle) ? RotationAngle : 90,
            WatermarkText = WatermarkText ?? "CONFIDENTIAL",
            WatermarkDiagonal = WatermarkDiagonal,
            WatermarkOpacity = Opacities.Contains(WatermarkOpacity) ? WatermarkOpacity : 30,
            PageSpec = PageSpec ?? "1",
            OutputFolder = OutputFolder ?? string.Empty
        };

        public static string CleanName(string? name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            return trimmed.Length > MaxNameLength ? trimmed.Substring(0, MaxNameLength).TrimEnd() : trimmed;
        }

        public bool SameSettingsAs(BatchPreset other) =>
            string.Equals(Action, other.Action, StringComparison.OrdinalIgnoreCase)
            && RotationAngle == other.RotationAngle
            && WatermarkText == other.WatermarkText
            && WatermarkDiagonal == other.WatermarkDiagonal
            && WatermarkOpacity == other.WatermarkOpacity
            && PageSpec == other.PageSpec
            && string.Equals(OutputFolder, other.OutputFolder, StringComparison.OrdinalIgnoreCase);
    }
}
