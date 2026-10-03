using System.Reflection;

namespace JobPilot.Web.Tests;

/// <summary>"The app never sends messages": no code path can send e-mails or LinkedIn messages.</summary>
public class NoSendingTests
{
    private static readonly string[] Forbidden =
    [
        "SmtpClient", "System.Net.Mail", "MailKit", "MimeKit", "mailto:", "api.linkedin.com", "/messages/send", "drafts/send", "gmail.send", "auth/gmail.modify", "mail.google.com",
    ];

    [Fact]
    public void The_app_does_not_reference_mail_libraries()
    {
        var references = typeof(Program).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain("System.Net.Mail", references);
        Assert.DoesNotContain(references, r => r!.StartsWith("MailKit", StringComparison.Ordinal) || r.StartsWith("MimeKit", StringComparison.Ordinal));
    }

    [Fact]
    public void Google_access_is_limited_to_reading_mail_drafts_and_the_app_calendar()
    {
        Assert.Equal(
            ["https://www.googleapis.com/auth/gmail.readonly", "https://www.googleapis.com/auth/gmail.compose", "https://www.googleapis.com/auth/calendar.app.created"],
            JobPilot.Web.Infrastructure.Google.GoogleScopes.All);
    }

    [Fact]
    public void No_source_file_contains_a_sending_api()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".js", StringComparison.Ordinal) || f.EndsWith(".json", StringComparison.Ordinal))
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(files);
        var hits = files.SelectMany(f => Forbidden.Where(term => File.ReadAllText(f).Contains(term, StringComparison.OrdinalIgnoreCase)).Select(term => $"{Path.GetRelativePath(root, f)}: {term}")).ToList();
        Assert.Empty(hits);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "JobPilot.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
