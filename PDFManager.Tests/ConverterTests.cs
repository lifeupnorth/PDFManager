using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PDFManager.ViewModels;

namespace PDFManager.Tests
{
    // The XAML value converters. Bindings pass null before a value exists, so each gets null too
    public class ConverterTests
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        [Fact]
        public void FileName_shows_the_file_name_of_a_path_and_passes_anything_else_through()
        {
            var converter = new FileNameConverter();

            Assert.Equal("Report.pdf", converter.Convert(@"C:\Docs\Report.pdf", typeof(string), null, Culture));
            Assert.Null(converter.Convert(null, typeof(string), null, Culture));
            Assert.Equal(42, converter.Convert(42, typeof(string), null, Culture));
        }

        [Theory]
        [InlineData(null, false, Visibility.Collapsed)]
        [InlineData("", false, Visibility.Collapsed)]
        [InlineData("  ", false, Visibility.Collapsed)]
        [InlineData(@"C:\Docs\Report.pdf", false, Visibility.Visible)]
        [InlineData(null, true, Visibility.Visible)]
        [InlineData(@"C:\Docs\Report.pdf", true, Visibility.Collapsed)]
        public void StringVisibility_follows_whether_the_text_is_empty(string? text, bool visibleWhenEmpty, Visibility expected)
        {
            var converter = new StringVisibilityConverter { VisibleWhenEmpty = visibleWhenEmpty };

            Assert.Equal(expected, converter.Convert(text, typeof(Visibility), null, Culture));
        }

        [Fact]
        public void RadioValue_checks_the_option_whose_parameter_matches_the_value()
        {
            var converter = new RadioValueConverter();

            Assert.Equal(true, converter.Convert(90, typeof(bool?), "90", Culture));
            Assert.Equal(false, converter.Convert(180, typeof(bool?), "90", Culture));
            Assert.Equal(false, converter.Convert(null, typeof(bool?), "90", Culture));
        }

        [Fact]
        public void RadioValue_writes_back_a_checked_options_parameter_as_a_number_or_an_enum()
        {
            var converter = new RadioValueConverter();

            Assert.Equal(270, converter.ConvertBack(true, typeof(int), "270", Culture));
            Assert.Equal(BatchAction.Extract, converter.ConvertBack(true, typeof(BatchAction), "Extract", Culture));
        }

        [Fact]
        public void RadioValue_writes_nothing_back_for_an_unchecked_option_or_one_without_a_parameter()
        {
            var converter = new RadioValueConverter();

            Assert.Same(Binding.DoNothing, converter.ConvertBack(false, typeof(int), "270", Culture));
            Assert.Same(Binding.DoNothing, converter.ConvertBack(null, typeof(int), "270", Culture));
            Assert.Same(Binding.DoNothing, converter.ConvertBack(true, typeof(int), null, Culture));
        }

        [Theory]
        [InlineData(BatchAction.Watermark, "Watermark", Visibility.Visible)]
        [InlineData(BatchAction.Rotate, "Watermark", Visibility.Collapsed)]
        [InlineData(null, "Watermark", Visibility.Collapsed)]
        public void EqualsToVisibility_shows_the_options_of_the_chosen_value(object? value, string parameter, Visibility expected)
        {
            Assert.Equal(expected, new EqualsToVisibilityConverter().Convert(value, typeof(Visibility), parameter, Culture));
        }

        [Theory]
        [InlineData(true, Visibility.Collapsed)]
        [InlineData(false, Visibility.Visible)]
        [InlineData(null, Visibility.Visible)]
        public void InverseBoolToVisibility_hides_on_true(object? value, Visibility expected)
        {
            Assert.Equal(expected, new InverseBoolToVisibilityConverter().Convert(value, typeof(Visibility), null, Culture));
        }
    }
}
