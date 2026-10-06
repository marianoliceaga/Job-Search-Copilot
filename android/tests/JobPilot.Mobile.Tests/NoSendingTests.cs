using JobPilot.Mobile.Core.Layout;

namespace JobPilot.Mobile.Tests;

/// <summary>
/// "The app never sends messages", as in the web app (whose NoSendingTests only scan the repo's src/): no code path in
/// android/src can send e-mails, SMS or LinkedIn messages. Sharing a file goes through Android's share sheet, where the
/// owner picks the destination and sends it themselves.
/// </summary>
public class NoSendingTests
{
    private static readonly string[] Forbidden =
    [
        // Same list as the web app
        "SmtpClient", "System.Net.Mail", "MailKit", "MimeKit", "mailto:", "api.linkedin.com", "/messages/send", "drafts/send", "gmail.send", "auth/gmail.modify", "mail.google.com",
        // Android / MAUI ways to compose or send messages
        "smsto:", "SmsManager", "EmailMessage", "SmsMessage", "Email.Default", "Sms.Default", "ACTION_SENDTO", "ActionSendto", "SEND_SMS",
    ];

    [Fact]
    public void The_core_does_not_reference_mail_libraries()
    {
        var references = typeof(AppSections).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.DoesNotContain("System.Net.Mail", references);
        Assert.DoesNotContain(references, r => r.StartsWith("MailKit", StringComparison.Ordinal) || r.StartsWith("MimeKit", StringComparison.Ordinal));
    }

    [Fact]
    public void No_source_file_contains_a_sending_api()
    {
        var files = AndroidFolder.SourceFiles(".cs", ".razor", ".js", ".json", ".html", ".xml", ".xaml", ".csproj");

        Assert.NotEmpty(files);
        var hits = files.SelectMany(f => Forbidden.Where(term => File.ReadAllText(f).Contains(term, StringComparison.OrdinalIgnoreCase)).Select(term => $"{Path.GetRelativePath(AndroidFolder.Root, f)}: {term}")).ToList();
        Assert.Empty(hits);
    }
}
