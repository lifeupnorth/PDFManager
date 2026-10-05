using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFManager.Services;
using PDFManager.ViewModels;
using Path = System.IO.Path;

namespace PDFManager.Tests
{
    public class ImagesToPdfViewModelTests : IDisposable
    {
        private readonly TestPdfs _files = new TestPdfs();

        public void Dispose() => _files.Dispose();

        private static ImagesToPdfViewModel NewViewModel() => new ImagesToPdfViewModel(new FakePdfService());

        private string Png(string name, string? folder = null)
        {
            string path = Path.Combine(folder ?? _files.Folder, name);
            var source = BitmapSource.Create(4, 2, 96, 96, PixelFormats.Gray8, null, new byte[8], 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(path))
                encoder.Save(stream);
            return path;
        }

        [Fact]
        public void Adds_images_and_the_images_directly_inside_folders_skipping_other_files_and_repeats()
        {
            string folder = _files.PathFor("photos");
            Directory.CreateDirectory(folder);
            Png("b.png", folder);
            Png("a.png", folder);
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "");
            string loose = Png("cover.png");
            var vm = NewViewModel();

            Assert.Equal(3, vm.AddPaths(new[] { loose, folder, _files.CreateNotAPdf("doc.pdf") }));
            Assert.Equal(0, vm.AddPaths(new[] { loose.ToUpperInvariant() }));

            Assert.Equal(new[] { "cover.png", "a.png", "b.png" }, vm.Images.Select(i => i.FileName));
        }

        [Fact]
        public void The_first_images_choose_the_output_name()
        {
            var one = NewViewModel();
            one.AddPaths(new[] { Png("Receipt.png") });
            Assert.Equal(Path.Combine(_files.Folder, "Receipt.pdf"), one.OutputPath);

            var many = NewViewModel();
            many.AddPaths(new[] { Png("p1.png"), Png("p2.png") });
            Assert.Equal(Path.Combine(_files.Folder, "Images.pdf"), many.OutputPath);

            // A name the user chose is kept when more images are added
            many.OutputPath = @"C:\Out\Album.pdf";
            many.AddPaths(new[] { Png("p3.png") });
            Assert.Equal(@"C:\Out\Album.pdf", many.OutputPath);
        }

        [Fact]
        public async Task Loads_a_preview_for_each_image_and_flags_unreadable_ones()
        {
            var vm = NewViewModel();
            string broken = _files.PathFor("broken.jpg");
            File.WriteAllText(broken, "not an image");

            vm.AddPaths(new[] { Png("ok.png"), broken });
            await vm.Previews;

            Assert.NotNull(vm.Images[0].Thumbnail);
            Assert.Equal("", vm.Images[0].Details);
            Assert.Null(vm.Images[1].Thumbnail);
            Assert.Equal("Cannot be read as an image", vm.Images[1].Details);
        }

        [Fact]
        public void Moves_one_selected_image_and_removes_any_selection()
        {
            var vm = NewViewModel();
            vm.AddPaths(new[] { Png("1.png"), Png("2.png"), Png("3.png") });
            string Order() => string.Concat(vm.Images.Select(i => i.FileName[0]));

            var second = new List<ImageItem> { vm.Images[1] };
            Assert.True(vm.MoveUpCommand.CanExecute(second));
            vm.MoveUpCommand.Execute(second);
            Assert.Equal("213", Order());
            Assert.False(vm.MoveUpCommand.CanExecute(second)); // now first
            Assert.False(vm.MoveDownCommand.CanExecute(new List<ImageItem> { vm.Images[0], vm.Images[1] })); // one at a time

            vm.RemoveImagesCommand.Execute(new List<ImageItem> { vm.Images[0], vm.Images[2] });
            Assert.Equal("1", Order());
        }

        [Fact]
        public void Margins_only_apply_to_paper_sizes()
        {
            var vm = NewViewModel();
            Assert.True(vm.CanHaveMargin); // A4 by default

            vm.PageSize = ImagePageSize.SameAsImage;
            Assert.False(vm.CanHaveMargin);
        }

        [Fact]
        public void Create_needs_an_image_and_an_output_file()
        {
            var vm = NewViewModel();
            Assert.False(vm.RunCreateCommand.CanExecute(null));

            vm.AddPaths(new[] { Png("a.png") });
            Assert.True(vm.RunCreateCommand.CanExecute(null));
        }
    }
}
