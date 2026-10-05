using System;
using System.IO;
using System.Text;
using iText.Kernel.Pdf;
using PDFManager.Services;

namespace PDFManager.Tests
{
    public class ProtectionTests : IDisposable
    {
        private readonly PdfService _service = new PdfService();
        private readonly TestPdfs _pdfs = new TestPdfs();

        public void Dispose() => _pdfs.Dispose();

        private static PdfReader ReaderWith(string path, string password) =>
            new PdfReader(path, new ReaderProperties().SetPassword(Encoding.UTF8.GetBytes(password)));

        // --- Protection state ---

        [Fact]
        public void GetProtection_tells_plain_password_and_permissions_only_files_apart()
        {
            Assert.Equal(PdfProtection.None, _service.GetProtection(_pdfs.CreatePages("plain.pdf", 1)));
            Assert.Equal(PdfProtection.PasswordToOpen, _service.GetProtection(_pdfs.CreateEncrypted("locked.pdf")));
            Assert.Equal(PdfProtection.PermissionsOnly, _service.GetProtection(_pdfs.CreateEncrypted("restricted.pdf", userPassword: "")));
        }

        [Fact]
        public void GetProtection_throws_iText_IOException_for_a_file_that_is_not_a_pdf()
        {
            Assert.Throws<iText.IO.Exceptions.IOException>(() => _service.GetProtection(_pdfs.CreateNotAPdf("notes.pdf")));
        }

        // --- Protect ---

        [Fact]
        public void ProtectPdf_requires_the_password_to_open_and_uses_AES_256()
        {
            string source = _pdfs.Create("doc.pdf", 101, 102);
            string output = _pdfs.PathFor("protected.pdf");

            _service.ProtectPdf(source, output, "s3cret");

            Assert.Throws<iText.Kernel.Exceptions.BadPasswordException>(() => _service.GetPageCount(output));
            using (var reader = ReaderWith(output, "s3cret"))
            using (var doc = new PdfDocument(reader))
            {
                Assert.Equal(2, doc.GetNumberOfPages());
                Assert.Equal(102f, doc.GetPage(2).GetMediaBox().GetWidth());
                Assert.True(reader.IsOpenedWithFullPermission());
                Assert.Equal(EncryptionConstants.ENCRYPTION_AES_256, reader.GetCryptoMode() & 7); // the low three bits hold the algorithm (iText's internal ENCRYPTION_MASK)
            }
            Assert.Equal(PdfProtection.None, _service.GetProtection(source));
        }

        [Fact]
        public void ProtectPdf_accepts_a_password_with_any_characters()
        {
            string output = _pdfs.PathFor("protected.pdf");

            _service.ProtectPdf(_pdfs.CreatePages("doc.pdf", 1), output, "pässwörd ✓ 密码");

            using var reader = ReaderWith(output, "pässwörd ✓ 密码");
            using var doc = new PdfDocument(reader);
            Assert.Equal(1, doc.GetNumberOfPages());
        }

        [Theory]
        [InlineData("")]
        [InlineData("user")]
        public void ProtectPdf_refuses_a_file_that_is_already_protected(string userPassword)
        {
            string source = _pdfs.CreateEncrypted("locked.pdf", userPassword: userPassword);

            var ex = Assert.Throws<InvalidOperationException>(() => _service.ProtectPdf(source, _pdfs.PathFor("protected.pdf"), "x"));

            Assert.Equal("This PDF is already protected. Remove its password with Unlock PDF first.", ex.Message);
            Assert.False(File.Exists(_pdfs.PathFor("protected.pdf")));
        }

        [Fact]
        public void ProtectPdf_failing_while_writing_leaves_no_temp_file_and_the_output_untouched()
        {
            string source = _pdfs.CreateWithBrokenPageTree("broken.pdf");
            string output = _pdfs.CreateNotAPdf("protected.pdf");
            byte[] before = File.ReadAllBytes(output);

            Assert.ThrowsAny<iText.Kernel.Exceptions.PdfException>(() => _service.ProtectPdf(source, output, "x"));

            Assert.Equal(before, File.ReadAllBytes(output));
            Assert.Empty(Directory.GetFiles(_pdfs.Folder, "*.tmp"));
        }

        // --- Unlock ---

        [Fact]
        public void UnlockPdf_removes_a_password_this_app_added()
        {
            string original = _pdfs.CreateWithMetadata("doc.pdf", "Kept title", "", "", "");
            string locked = _pdfs.PathFor("locked.pdf");
            string output = _pdfs.PathFor("unlocked.pdf");
            _service.ProtectPdf(original, locked, "s3cret");

            _service.UnlockPdf(locked, output, "s3cret");

            Assert.Equal(PdfProtection.None, _service.GetProtection(output));
            Assert.Equal(new[] { 100f, 101f }, TestPdfs.PageWidths(output));
            Assert.Equal("Kept title", _service.ReadMetadata(output).Title);
        }

        [Fact]
        public void UnlockPdf_with_the_owner_password_removes_both_passwords()
        {
            string output = _pdfs.PathFor("unlocked.pdf");

            _service.UnlockPdf(_pdfs.CreateEncrypted("locked.pdf", 3), output, "owner");

            Assert.Equal(PdfProtection.None, _service.GetProtection(output));
            Assert.Equal(3, _service.GetPageCount(output));
        }

        [Fact]
        public void UnlockPdf_with_only_the_open_password_explains_the_owner_password_is_needed()
        {
            string output = _pdfs.PathFor("unlocked.pdf");

            var ex = Assert.Throws<InvalidOperationException>(() => _service.UnlockPdf(_pdfs.CreateEncrypted("locked.pdf"), output, "user"));

            Assert.Equal("This password opens the PDF, but its owner has restricted changes. Enter the owner password to remove the protection.", ex.Message);
            Assert.False(File.Exists(output));
        }

        [Fact]
        public void UnlockPdf_removes_permission_restrictions_with_the_owner_password()
        {
            string output = _pdfs.PathFor("unlocked.pdf");

            _service.UnlockPdf(_pdfs.CreateEncrypted("restricted.pdf", userPassword: ""), output, "owner");

            Assert.Equal(PdfProtection.None, _service.GetProtection(output));
        }

        [Theory]
        [InlineData("user", "wrong")]
        [InlineData("user", "")]
        [InlineData("", "wrong")]
        [InlineData("", "")]
        public void UnlockPdf_with_a_wrong_password_writes_nothing(string userPassword, string attempt)
        {
            string source = _pdfs.CreateEncrypted("locked.pdf", userPassword: userPassword);
            string output = _pdfs.CreateNotAPdf("unlocked.pdf");
            byte[] before = File.ReadAllBytes(output);

            var ex = Assert.Throws<InvalidOperationException>(() => _service.UnlockPdf(source, output, attempt));

            Assert.Equal("The password is incorrect.", ex.Message);
            Assert.Equal(before, File.ReadAllBytes(output));
            Assert.Empty(Directory.GetFiles(_pdfs.Folder, "*.tmp"));
        }

        [Fact]
        public void UnlockPdf_refuses_a_file_that_is_not_protected()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => _service.UnlockPdf(_pdfs.CreatePages("plain.pdf", 1), _pdfs.PathFor("unlocked.pdf"), "x"));

            Assert.Equal("This PDF is not password-protected.", ex.Message);
        }
    }
}
