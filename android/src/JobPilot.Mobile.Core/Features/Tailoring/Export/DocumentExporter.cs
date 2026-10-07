using JobPilot.Domain.Cv;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Tailoring;

namespace JobPilot.Mobile.Core.Features.Tailoring.Export;

public enum ExportFormat
{
    Docx,
    Pdf,
}

public sealed record ExportedFile(string FileName, string ContentType, byte[] Content);

public sealed class DocumentExporter(TimeProvider time)
{
    public ExportedFile ExportCv(MasterCv master, TailoredCv cv, JobPosting posting, ExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(posting);
        var content = DocumentContentBuilder.Resume(master, cv);
        return format == ExportFormat.Docx
            ? new ExportedFile(DocumentContentBuilder.FileName(content.Name, "CV", posting.Company, "docx"), DocxContentType, DocxWriter.Resume(content))
            : new ExportedFile(DocumentContentBuilder.FileName(content.Name, "CV", posting.Company, "pdf"), "application/pdf", PdfWriter.Resume(content));
    }

    public ExportedFile ExportLetter(MasterCv master, CoverLetter letter, JobPosting posting, ExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(posting);
        var content = DocumentContentBuilder.Letter(master, posting, letter, time.GetLocalNow().DateTime);
        return format == ExportFormat.Docx
            ? new ExportedFile(DocumentContentBuilder.FileName(content.Name, "Cover letter", posting.Company, "docx"), DocxContentType, DocxWriter.Letter(content))
            : new ExportedFile(DocumentContentBuilder.FileName(content.Name, "Cover letter", posting.Company, "pdf"), "application/pdf", PdfWriter.Letter(content));
    }

    private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
}
