using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PDFManager.Services;
using PDFManager.ViewModels;

namespace PDFManager.Tests
{
    // Reads the palettes and views as text, so it needs no running application
    public class ThemeTests
    {
        private static readonly string RepoRoot = FindRepoRoot();

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PDFManager.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("PDFManager.csproj not found above the test output.");
        }

        private static string Read(string relativePath) => File.ReadAllText(Path.Combine(RepoRoot, relativePath));

        private static Dictionary<string, string> Palette(string name) =>
            Regex.Matches(Read($"Themes/{name}.xaml"), "x:Key=\"(\\w+)\" Color=\"(#[0-9A-Fa-f]{6})\"")
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        // WCAG 2 relative luminance and contrast ratio
        private static double Luminance(string hex)
        {
            double Channel(int i)
            {
                double c = Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16) / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
        }

        private static double Contrast(string a, string b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        [Theory]
        [InlineData(AppTheme.Dark, true, true)]
        [InlineData(AppTheme.Dark, false, true)]
        [InlineData(AppTheme.Light, true, false)]
        [InlineData(AppTheme.Light, false, false)]
        [InlineData(AppTheme.System, true, false)]
        [InlineData(AppTheme.System, false, true)]
        public void System_follows_Windows_and_the_others_ignore_it(AppTheme choice, bool windowsUsesLightApps, bool dark)
        {
            Assert.Equal(dark, ThemeManager.ResolveIsDark(choice, windowsUsesLightApps));
        }

        [Fact]
        public void Both_palettes_define_the_same_colours()
        {
            Assert.Equal(Palette("Dark").Keys.OrderBy(k => k), Palette("Light").Keys.OrderBy(k => k));
        }

        [Theory]
        [InlineData("App.xaml")]
        [InlineData("MainWindow.xaml")]
        public void Views_use_palette_colours_dynamically_and_only_ones_that_exist(string view)
        {
            string xaml = Read(view);
            var palette = Palette("Dark").Keys.ToHashSet();

            var dynamicKeys = Regex.Matches(xaml, @"\{DynamicResource (\w+)\}").Select(m => m.Groups[1].Value).ToHashSet();
            Assert.NotEmpty(dynamicKeys);
            Assert.All(dynamicKeys, key => Assert.Contains(key, palette));

            // A StaticResource would be resolved once, at load, and miss every later theme switch
            var staticPaletteKeys = Regex.Matches(xaml, @"\{StaticResource (\w+)\}").Select(m => m.Groups[1].Value).Where(palette.Contains).ToList();
            Assert.Empty(staticPaletteKeys);

            // Colours written straight into the views would not change with the theme either.
            // White is allowed: it is only used on the red accent, in both themes
            var literals = Regex.Matches(xaml, "(?:Foreground|Background|Fill|Stroke|BorderBrush|Value)=\"(#[0-9A-Fa-f]{3,8})\"").Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(view == "MainWindow.xaml" ? new[] { "#737373" } : Array.Empty<string>(), literals); // the watermark preview's grey text
        }

        [Theory]
        [InlineData("Dark")]
        [InlineData("Light")]
        public void Text_colours_meet_WCAG_AA_contrast_on_their_backgrounds(string theme)
        {
            var c = Palette(theme);
            var pairs = new (string Text, string Background)[]
            {
                ("TextBrush", "WindowBrush"), ("TextBrush", "SidebarBrush"), ("TextBrush", "PanelBrush"), ("TextBrush", "CardBrush"),
                ("TextSecondaryBrush", "WindowBrush"), ("TextSecondaryBrush", "PanelBrush"), ("TextSecondaryBrush", "CardBrush"),
                ("TextMutedBrush", "WindowBrush"), ("TextMutedBrush", "SidebarBrush"), ("TextMutedBrush", "PanelBrush"),
                ("ErrorTextBrush", "CardBrush"), ("ErrorTextBrush", "PanelBrush"), ("SuccessBrush", "CardBrush"),
                ("TextBrush", "AccentTintBrush")
            };
            Assert.All(pairs, p => Assert.True(Contrast(c[p.Text], c[p.Background]) >= 4.5,
                $"{theme}: {p.Text} on {p.Background} is {Contrast(c[p.Text], c[p.Background]):0.00}:1"));
            Assert.True(Contrast("#FFFFFF", c["AccentBrush"]) >= 4.0, "button text on the accent"); // 15 px semibold
        }

        [Theory]
        [InlineData("Dark")]
        [InlineData("Light")]
        public void Tool_icons_stand_out_from_their_tiles(string theme)
        {
            var c = Palette(theme);
            foreach (string category in new[] { "Organize", "Edit", "Security", "Batch", "Convert" })
                Assert.True(Contrast(c[category + "IconBrush"], c[category + "TileBrush"]) >= 3,
                    $"{theme}: {category} icon is {Contrast(c[category + "IconBrush"], c[category + "TileBrush"]):0.00}:1 on its tile");
        }

        [Fact]
        public void Light_input_borders_are_visible_against_the_input()
        {
            var c = Palette("Light");
            Assert.True(Contrast(c["BorderStrongBrush"], c["InputBrush"]) >= 3);
        }
    }

    // AppSettings.Folder is static, so the classes that change it share a collection: xUnit runs
    // collections one at a time
    [Collection("Settings file")]
    public class AppearanceTests : IDisposable
    {
        private readonly string _originalFolder = AppSettings.Folder;
        private readonly TestPdfs _temp = new TestPdfs();
        private readonly List<AppTheme> _applied = new();

        public AppearanceTests() => AppSettings.Folder = Path.Combine(_temp.Folder, "settings");

        public void Dispose()
        {
            AppSettings.Folder = _originalFolder;
            _temp.Dispose();
        }

        [Fact]
        public void Dark_is_the_default_so_the_app_looks_as_it_always_did()
        {
            Assert.Equal(AppTheme.Dark, new AppearanceViewModel(_applied.Add).Theme);
            Assert.Empty(_applied);
        }

        [Fact]
        public void A_new_choice_is_applied_at_once_and_remembered()
        {
            var appearance = new AppearanceViewModel(_applied.Add);

            appearance.Theme = AppTheme.Light;
            appearance.Theme = AppTheme.Light; // no change, nothing applied
            appearance.Theme = AppTheme.System;

            Assert.Equal(new[] { AppTheme.Light, AppTheme.System }, _applied);
            Assert.Equal(AppTheme.System, new AppearanceViewModel(_applied.Add).Theme);
            Assert.Contains("\"Theme\": \"System\"", File.ReadAllText(AppSettings.FilePath));
        }

        [Fact]
        public void Choosing_a_theme_keeps_the_other_settings()
        {
            new SidebarViewModel { EditExpanded = false };

            new AppearanceViewModel(_applied.Add).Theme = AppTheme.Light;

            Assert.Equal(new[] { "Edit" }, AppSettings.Load().CollapsedSections);
            Assert.Equal(AppTheme.Light, AppSettings.Load().ThemeChoice);
        }

        [Theory]
        [InlineData("Purple")]
        [InlineData("7")]
        public void An_unknown_stored_theme_falls_back_to_dark(string stored)
        {
            AppSettings.Save(new SettingsData { Theme = stored });

            Assert.Equal(AppTheme.Dark, new AppearanceViewModel(_applied.Add).Theme);
        }
    }
}
