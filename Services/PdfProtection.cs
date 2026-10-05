#nullable enable
namespace PDFManager.Services
{
    public enum PdfProtection
    {
        // Not encrypted
        None,
        // A password is needed to open the file
        PasswordToOpen,
        // Opens without a password, but an owner password restricts printing, copying or editing
        PermissionsOnly
    }
}
