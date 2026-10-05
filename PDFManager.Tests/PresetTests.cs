using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PDFManager.Services;
using PDFManager.ViewModels;

namespace PDFManager.Tests
{
    // AppSettings.Folder is static, so the classes that change it share a collection: xUnit runs
    // collections one at a time
    [Collection("Settings file")]
    public class PresetTests : IDisposable
    {
        private readonly string _originalFolder = AppSettings.Folder;
        private readonly TestPdfs _temp = new TestPdfs();

        public PresetTests() => AppSettings.Folder = Path.Combine(_temp.Folder, "settings");

        public void Dispose()
        {
            AppSettings.Folder = _originalFolder;
            _temp.Dispose();
        }

        private static BatchViewModel NewBatch() => new BatchViewModel(new FakePdfService());

        private static BatchViewModel Configured()
        {
            var batch = NewBatch();
            batch.Action = BatchAction.Watermark;
            batch.WatermarkText = "DRAFT";
            batch.WatermarkDiagonal = false;
            batch.WatermarkOpacity = 50;
            batch.OutputFolder = @"C:\Out";
            batch.Password = "secret";
            return batch;
        }

        [Fact]
        public void There_are_no_presets_at_first()
        {
            var batch = NewBatch();

            Assert.Empty(batch.Presets);
            Assert.False(batch.HasPresets);
            Assert.False(batch.SavePresetCommand.CanExecute(null)); // no name typed
        }

        [Fact]
        public void A_saved_preset_keeps_the_settings_but_never_the_password()
        {
            Configured().SaveCurrentAsPreset("  Stamp draft  ");

            string file = File.ReadAllText(AppSettings.FilePath);
            Assert.Contains("\"Name\": \"Stamp draft\"", file);
            Assert.DoesNotContain("secret", file);

            var nextSession = NewBatch();
            var preset = Assert.Single(nextSession.Presets).Preset;
            Assert.Equal(("Watermark", "DRAFT", false, 50, @"C:\Out"), (preset.Action, preset.WatermarkText, preset.WatermarkDiagonal, preset.WatermarkOpacity, preset.OutputFolder));
            Assert.True(nextSession.HasPresets);
        }

        [Fact]
        public void Applying_a_preset_sets_every_option_and_highlights_it_until_one_changes()
        {
            Configured().SaveCurrentAsPreset("Stamp draft");
            var batch = NewBatch();
            var item = Assert.Single(batch.Presets);
            Assert.False(item.IsActive);
            batch.Password = "typed for this run";

            batch.ApplyPresetCommand.Execute(item);

            Assert.Equal((BatchAction.Watermark, "DRAFT", false, 50, @"C:\Out"), (batch.Action, batch.WatermarkText, batch.WatermarkDiagonal, batch.WatermarkOpacity, batch.OutputFolder));
            Assert.Equal("typed for this run", batch.Password); // a preset never touches the password
            Assert.True(item.IsActive);
            Assert.Equal("Apply preset Stamp draft, applied", item.ApplyName);

            batch.WatermarkOpacity = 15;
            Assert.False(item.IsActive);
        }

        [Fact]
        public void Saving_under_an_existing_name_replaces_that_preset_in_place()
        {
            var batch = NewBatch();
            batch.SaveCurrentAsPreset("First");
            batch.SaveCurrentAsPreset("Second");
            batch.RotationAngle = 180;

            batch.SaveCurrentAsPreset("first");

            Assert.Equal(new[] { "first", "Second" }, batch.Presets.Select(p => p.Name));
            Assert.Equal(180, batch.Presets[0].Preset.RotationAngle);
            Assert.Equal(new[] { "first", "Second" }, NewBatch().Presets.Select(p => p.Name));
        }

        [Fact]
        public void Deleting_a_preset_is_remembered()
        {
            var batch = NewBatch();
            batch.SaveCurrentAsPreset("Keep");
            batch.SaveCurrentAsPreset("Remove");

            batch.DeletePreset(batch.Presets[1]);

            Assert.Equal(new[] { "Keep" }, NewBatch().Presets.Select(p => p.Name));
        }

        [Fact]
        public void Presets_from_a_hand_edited_file_are_kept_within_the_options_the_tool_offers()
        {
            AppSettings.Save(new SettingsData
            {
                BatchPresets = new List<BatchPreset>
                {
                    new BatchPreset { Name = "Odd", Action = "Purple", RotationAngle = 45, WatermarkOpacity = 99, WatermarkText = null!, PageSpec = null! },
                    new BatchPreset { Name = "   " },
                    new BatchPreset { Name = "odd" },
                    new BatchPreset { Name = new string('x', 60) }
                }
            });

            var batch = NewBatch();
            Assert.Equal(new[] { "Odd", new string('x', BatchPreset.MaxNameLength) }, batch.Presets.Select(p => p.Name));

            batch.ApplyPreset(batch.Presets[0].Preset);
            Assert.Equal((BatchAction.Rotate, 90, 30, "CONFIDENTIAL", "1"), (batch.Action, batch.RotationAngle, batch.WatermarkOpacity, batch.WatermarkText, batch.PageSpec));
        }

        [Fact]
        public void Presets_leave_the_other_settings_alone()
        {
            new SidebarViewModel { EditExpanded = false };
            new AppearanceViewModel(_ => { }).Theme = AppTheme.Light;

            NewBatch().SaveCurrentAsPreset("Rotate scans");

            var settings = AppSettings.Load();
            Assert.Equal(new[] { "Edit" }, settings.CollapsedSections);
            Assert.Equal(AppTheme.Light, settings.ThemeChoice);
            Assert.Single(settings.BatchPresets);
        }

        [Fact]
        public void The_save_button_needs_a_name()
        {
            var batch = NewBatch();
            batch.NewPresetName = "   ";
            Assert.False(batch.SavePresetCommand.CanExecute(null));

            batch.NewPresetName = "Rotate scans";
            Assert.True(batch.SavePresetCommand.CanExecute(null));
        }
    }
}
