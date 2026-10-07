using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using PDFManager.Services;
using PDFManager.ViewModels;

namespace PDFManager.Tests
{
    // Reads the views as text, and WPF's Fluent dictionaries without a running application
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

        // Every string key in one of the Fluent theme's dictionaries ("Light" or "Dark"), merged ones included
        private static HashSet<string> FluentKeys(string variant)
        {
            var keys = new HashSet<string>();
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    _ = Application.Current; // its static constructor registers pack:// URIs; no application is created
                    var theme = new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{variant}.xaml")
                    };
                    void Collect(ResourceDictionary dictionary)
                    {
                        foreach (object key in dictionary.Keys)
                            if (key is string name)
                                keys.Add(name);
                        foreach (var merged in dictionary.MergedDictionaries)
                            Collect(merged);
                    }
                    Collect(theme);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null)
                throw new InvalidOperationException("Could not load the Fluent theme.", failure);
            return keys;
        }

        // The keys the app's own XAML defines
        private static HashSet<string> OwnKeys() =>
            Regex.Matches(Read("App.xaml") + Read("MainWindow.xaml"), "x:Key=\"(\\w+)\"").Select(m => m.Groups[1].Value).ToHashSet();

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
        public void Each_choice_sets_the_matching_Fluent_theme_mode()
        {
            Assert.Equal(ThemeMode.System, ThemeManager.ModeFor(AppTheme.System));
            Assert.Equal(ThemeMode.Light, ThemeManager.ModeFor(AppTheme.Light));
            Assert.Equal(ThemeMode.Dark, ThemeManager.ModeFor(AppTheme.Dark));
        }

        [Fact]
        public void The_app_starts_from_the_Fluent_theme()
        {
            Assert.Contains("Source=\"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml\"", Read("App.xaml"));
        }

        [Theory]
        [InlineData("App.xaml", "Light")]
        [InlineData("App.xaml", "Dark")]
        [InlineData("MainWindow.xaml", "Light")]
        [InlineData("MainWindow.xaml", "Dark")]
        public void Views_use_only_colours_and_styles_that_exist(string view, string variant)
        {
            // A misspelt DynamicResource fails silently: the control just goes without
            var known = FluentKeys(variant);
            known.UnionWith(OwnKeys());
            var dynamicKeys = Regex.Matches(Read(view), @"\{DynamicResource (\w+)\}").Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.NotEmpty(dynamicKeys);
            Assert.All(dynamicKeys, key => Assert.Contains(key, known));
        }

        [Theory]
        [InlineData("App.xaml")]
        [InlineData("MainWindow.xaml")]
        public void Theme_colours_are_looked_up_dynamically(string view)
        {
            string xaml = Read(view);

            // A StaticResource is resolved once, at load, and would miss every later theme switch
            var fluent = FluentKeys("Dark");
            var staticThemeKeys = Regex.Matches(xaml, @"\{StaticResource (\w+)\}").Select(m => m.Groups[1].Value)
                .Where(key => fluent.Contains(key) && key.EndsWith("Brush")).ToList();
            Assert.Empty(staticThemeKeys);

            // Colours written straight into the views would not change with the theme either. White
            // is allowed for paper: page previews are white in both themes, as in ScanManager
            var literals = Regex.Matches(xaml, "(?:Foreground|Background|Fill|Stroke|BorderBrush|Value)=\"(#[0-9A-Fa-f]{3,8})\"").Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(view == "MainWindow.xaml" ? new[] { "#737373" } : Array.Empty<string>(), literals); // the watermark preview's grey text
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
        public void The_default_follows_Windows_as_ScanManager_does()
        {
            Assert.Equal(AppTheme.System, new AppearanceViewModel(_applied.Add).Theme);
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
        public void An_unknown_stored_theme_falls_back_to_following_Windows(string stored)
        {
            AppSettings.Save(new SettingsData { Theme = stored });

            Assert.Equal(AppTheme.System, new AppearanceViewModel(_applied.Add).Theme);
        }
    }
}
