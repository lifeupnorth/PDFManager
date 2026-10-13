using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Element;

namespace PDFManager.Harness
{
    // A made-up, illustrated report for the README screenshots. Every page looks different,
    // so the page previews show what a real document would look like.
    internal static class SamplePdf
    {
        private static readonly DeviceRgb Green = new(46, 125, 80), LightGreen = new(220, 237, 225),
            Sky = new(170, 210, 240), Hill = new(96, 160, 90), Sun = new(250, 200, 70),
            Gray = new(90, 90, 90), Bar1 = new(46, 125, 80), Bar2 = new(240, 160, 60), Bar3 = new(80, 140, 200);

        private const string Text =
            "The garden had its busiest season yet. Forty-two plots were in use from April to October, " +
            "and the waiting list grew to eighteen families. Volunteers built two new raised beds by the " +
            "gate and repaired the tool shed roof before the autumn rain. ";

        public static string Create(string path)
        {
            using var document = new PdfDocument(new PdfWriter(path));
            using var layout = new Document(document, PageSize.A4);
            PdfFont regular = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            PdfFont bold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            layout.SetFont(regular).SetFontSize(11).SetFontColor(Gray);

            // 1: the cover
            var cover = document.AddNewPage();
            var canvas = new PdfCanvas(cover);
            canvas.SetFillColor(Green).Rectangle(0, 420, 595, 422).Fill();
            Landscape(canvas, 60, 120, 475, 260);
            Title(layout, bold, 1, "Lakeside Community Garden", 34, ColorConstants.WHITE, 640);
            Title(layout, regular, 1, "2026 Season Report", 20, ColorConstants.WHITE, 600);

            // 2-8: one section each, alternating text, charts and pictures
            string[] sections = { "Contents", "Plots and members", "Harvest", "Volunteers", "Events", "Budget", "Next season" };
            for (int i = 0; i < sections.Length; i++)
            {
                int number = i + 2;
                var page = document.AddNewPage();
                canvas = new PdfCanvas(page);
                canvas.SetFillColor(Green).Rectangle(50, 760, 495, 4).Fill();
                Title(layout, bold, number, sections[i], 24, Green, 775);

                if (i == 0)
                {
                    for (int s = 1; s < sections.Length; s++)
                        layout.Add(new Paragraph($"{s + 2}    {sections[s]}").SetFontSize(14).SetFixedPosition(number, 70, 700 - s * 34, 400));
                }
                else
                {
                    layout.Add(new Paragraph(Text + Text).SetFixedPosition(number, 50, 610, 495));
                    if (i % 3 == 1)
                        Chart(canvas, 70, 200, 455, 330, i);
                    else if (i % 3 == 2)
                        Landscape(canvas, 50, 200, 495, 330);
                    else
                    {
                        canvas.SetFillColor(LightGreen).Rectangle(50, 200, 495, 330).Fill();
                        layout.Add(new Paragraph(Text + Text + Text).SetFixedPosition(number, 70, 230, 455));
                    }
                }
                layout.Add(new Paragraph(number.ToString()).SetFontSize(9).SetFixedPosition(number, 290, 30, 20));
            }
            return path;
        }

        private static void Title(Document layout, PdfFont font, int page, string text, float size, Color color, float y) =>
            layout.Add(new Paragraph(text).SetFont(font).SetFontSize(size).SetFontColor(color).SetFixedPosition(page, 50, y, 495));

        // A sky, a sun and two hills: stands in for a photo
        private static void Landscape(PdfCanvas canvas, float x, float y, float width, float height)
        {
            canvas.SaveState().Rectangle(x, y, width, height).Clip().EndPath();
            canvas.SetFillColor(Sky).Rectangle(x, y, width, height).Fill();
            canvas.SetFillColor(Sun).Circle(x + width * 0.75, y + height * 0.72, height * 0.12).Fill();
            canvas.SetFillColor(Hill).Ellipse(x - width * 0.2, y - height * 0.5, x + width * 0.7, y + height * 0.55).Fill();
            canvas.SetFillColor(Green).Ellipse(x + width * 0.35, y - height * 0.6, x + width * 1.3, y + height * 0.45).Fill();
            canvas.RestoreState();
        }

        // A bar chart with three series
        private static void Chart(PdfCanvas canvas, float x, float y, float width, float height, int seed)
        {
            canvas.SetStrokeColor(Gray).SetLineWidth(1).MoveTo(x, y + height).LineTo(x, y).LineTo(x + width, y).Stroke();
            Color[] colors = { Bar1, Bar2, Bar3 };
            float groupWidth = width / 6;
            for (int group = 0; group < 6; group++)
                for (int bar = 0; bar < 3; bar++)
                {
                    float value = 0.25f + ((group * 7 + bar * 5 + seed * 3) % 10) / 13f;
                    canvas.SetFillColor(colors[bar])
                        .Rectangle(x + group * groupWidth + 10 + bar * (groupWidth - 20) / 3, y, (groupWidth - 20) / 3 - 2, value * (height - 10))
                        .Fill();
                }
        }
    }
}
