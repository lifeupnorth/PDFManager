using System;
using System.IO;
using System.Linq;
using PDFManager.Services;
using PDFManager.ViewModels;

namespace PDFManager.Tests
{
    // AppSettings.Folder is static, so the classes that change it share a collection: xUnit runs
    // collections one at a time
    [Collection("Settings file")]
    public class SidebarTests : IDisposable
    {
        private readonly string _originalFolder = AppSettings.Folder;
        private readonly TestPdfs _temp = new TestPdfs();

        public SidebarTests() => AppSettings.Folder = Path.Combine(_temp.Folder, "settings");

        public void Dispose()
        {
            AppSettings.Folder = _originalFolder;
            _temp.Dispose();
        }

        [Fact]
        public void Every_section_starts_expanded()
        {
            var sidebar = new SidebarViewModel();

            Assert.True(sidebar.OrganizeExpanded && sidebar.EditExpanded && sidebar.SecurityExpanded && sidebar.ConvertExpanded && sidebar.BatchExpanded);
            Assert.False(File.Exists(AppSettings.FilePath)); // nothing to remember yet
        }

        [Fact]
        public void Collapsed_sections_are_remembered_for_the_next_session()
        {
            var sidebar = new SidebarViewModel();
            string? changed = null;
            sidebar.PropertyChanged += (_, e) => changed = e.PropertyName;

            sidebar.SecurityExpanded = false;
            sidebar.BatchExpanded = false;

            Assert.Equal(nameof(SidebarViewModel.BatchExpanded), changed);
            Assert.Equal(new[] { "Batch", "Security" }, AppSettings.Load().CollapsedSections);
            var nextSession = new SidebarViewModel();
            Assert.False(nextSession.SecurityExpanded);
            Assert.False(nextSession.BatchExpanded);
            Assert.True(nextSession.EditExpanded);

            nextSession.SecurityExpanded = true;
            Assert.Equal(new[] { "Batch" }, AppSettings.Load().CollapsedSections);
        }

        [Theory]
        [InlineData(1, "Organize")]
        [InlineData(2, "Organize")]
        [InlineData(4, "Organize")]
        [InlineData(12, "Organize")]
        [InlineData(3, "Edit")]
        [InlineData(5, "Edit")]
        [InlineData(8, "Edit")]
        [InlineData(6, "Security")]
        [InlineData(7, "Security")]
        [InlineData(10, "Convert")]
        [InlineData(11, "Convert")]
        [InlineData(9, "Batch")]
        public void Opening_a_tool_expands_its_section(int toolIndex, string section)
        {
            var sidebar = new SidebarViewModel
            {
                OrganizeExpanded = false, EditExpanded = false, SecurityExpanded = false, ConvertExpanded = false, BatchExpanded = false
            };

            sidebar.ExpandSectionOf(toolIndex);

            Assert.Equal(new[] { section }, new[] { "Organize", "Edit", "Security", "Convert", "Batch" }.Where(name =>
                (bool)typeof(SidebarViewModel).GetProperty(name + "Expanded")!.GetValue(sidebar)!));
        }

        [Fact]
        public void Opening_Home_changes_nothing()
        {
            var sidebar = new SidebarViewModel { EditExpanded = false };

            sidebar.ExpandSectionOf(0);

            Assert.False(sidebar.EditExpanded);
        }

        [Fact]
        public void A_damaged_settings_file_falls_back_to_the_defaults()
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.WriteAllText(AppSettings.FilePath, "{ not json");

            var sidebar = new SidebarViewModel();
            Assert.True(sidebar.EditExpanded);

            sidebar.EditExpanded = false; // replaces the damaged file
            Assert.Equal(new[] { "Edit" }, AppSettings.Load().CollapsedSections);
        }

        [Fact]
        public void A_settings_folder_that_cannot_be_written_is_ignored()
        {
            string file = Path.Combine(_temp.Folder, "a-file");
            File.WriteAllText(file, "");
            AppSettings.Folder = Path.Combine(file, "settings"); // a folder inside a file cannot exist

            var sidebar = new SidebarViewModel();
            sidebar.ConvertExpanded = false; // must not throw

            Assert.False(sidebar.ConvertExpanded);
        }
    }
}
