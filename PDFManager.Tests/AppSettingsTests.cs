using System;
using System.IO;
using System.Linq;
using PDFManager.Services;
using PDFManager.ViewModels;

namespace PDFManager.Tests
{
    // settings.json is plain text that people can edit, so it can hold null where the types say
    // a value is never null. Load puts the defaults back, and the app still starts.
    [Collection("Settings file")]
    public class AppSettingsTests : IDisposable
    {
        private readonly string _originalFolder = AppSettings.Folder;
        private readonly TestPdfs _temp = new TestPdfs();

        public AppSettingsTests() => AppSettings.Folder = Path.Combine(_temp.Folder, "settings");

        public void Dispose()
        {
            AppSettings.Folder = _originalFolder;
            _temp.Dispose();
        }

        private static void WriteSettings(string json)
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.WriteAllText(AppSettings.FilePath, json);
        }

        [Theory]
        [InlineData("""{ "CollapsedSections": null, "BatchPresets": null, "Theme": null }""")]
        [InlineData("""{ "CollapsedSections": [null], "BatchPresets": [null] }""")]
        [InlineData("null")]
        public void Nulls_in_the_file_become_the_defaults(string json)
        {
            WriteSettings(json);

            var settings = AppSettings.Load();

            Assert.NotNull(settings.CollapsedSections);
            Assert.DoesNotContain(null, settings.CollapsedSections);
            Assert.NotNull(settings.BatchPresets);
            Assert.DoesNotContain(null, settings.BatchPresets);
            Assert.Equal(AppTheme.Dark, settings.ThemeChoice);
        }

        [Fact]
        public void The_entries_that_are_there_are_kept()
        {
            WriteSettings("""{ "CollapsedSections": [null, "Edit"], "BatchPresets": [null, { "Name": "Weekly" }], "Theme": "Light" }""");

            var settings = AppSettings.Load();

            Assert.Equal(new[] { "Edit" }, settings.CollapsedSections);
            Assert.Equal("Weekly", Assert.Single(settings.BatchPresets).Name);
            Assert.Equal(AppTheme.Light, settings.ThemeChoice);
        }

        [Fact]
        public void The_app_still_starts_with_nulls_in_the_file()
        {
            WriteSettings("""{ "CollapsedSections": [null, "Edit"], "BatchPresets": [null, { "Name": "Weekly", "Action": null, "WatermarkText": null, "PageSpec": null, "OutputFolder": null }] }""");

            var sidebar = new SidebarViewModel();
            var batch = new BatchViewModel(new FakePdfService(), new FakeDialogService());
            var appearance = new AppearanceViewModel(_ => { });

            Assert.False(sidebar.EditExpanded);
            var preset = Assert.Single(batch.Presets).Preset;
            Assert.Equal(("Weekly", "Rotate", "CONFIDENTIAL", "1", ""), (preset.Name, preset.Action, preset.WatermarkText, preset.PageSpec, preset.OutputFolder));
            Assert.Equal(AppTheme.Dark, appearance.Theme);
        }

        // Another window, or a test in another collection, can be reading the file just as it is
        // replaced. That lost about one save in four, or made the read return the defaults. The
        // reader pauses a millisecond between reads, far busier than anything real; with no pause
        // a Debug build keeps the file open so much that even the retries run out
        [Fact]
        public void Saves_land_and_reads_never_fall_back_while_another_thread_reads()
        {
            AppSettings.Save(new SettingsData { CollapsedSections = { "start" } });
            int readsWithoutTheSetting = 0;
            bool stop = false;
            var reader = new System.Threading.Thread(() =>
            {
                while (!System.Threading.Volatile.Read(ref stop))
                {
                    if (AppSettings.Load().CollapsedSections.Count == 0)
                        System.Threading.Interlocked.Increment(ref readsWithoutTheSetting);
                    System.Threading.Thread.Sleep(1);
                }
            });
            reader.Start();

            int lost = 0;
            for (int i = 0; i < 200; i++)
            {
                AppSettings.Save(new SettingsData { CollapsedSections = { "s" + i } });
                if (AppSettings.Load().CollapsedSections.SingleOrDefault() != "s" + i)
                    lost++;
            }
            System.Threading.Volatile.Write(ref stop, true);
            reader.Join();

            Assert.Equal(0, lost);
            Assert.Equal(0, readsWithoutTheSetting);
        }
    }
}
