using JobPilot.Domain.Jobs;

namespace JobPilot.Domain.Mail;

/// <summary>An application a "Te esperan" thread may belong to.</summary>
public sealed record MailApplicationCandidate(Guid Id, string Title, string Company, IReadOnlyList<string> ContactEmails);

/// <summary>
/// Suggests the pipeline applications a waiting thread belongs to, strongest evidence first: a contact's address, the
/// company name in the sender's domain, then the company name in the subject or the sender's name. Only a suggestion: the
/// owner links the thread with one click.
/// </summary>
public static class MailApplicationMatcher
{
    public static IReadOnlyList<MailApplicationCandidate> Match(string senderAddress, string senderName, string subject, IReadOnlyList<MailApplicationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var address = senderAddress.Trim();
        var at = address.LastIndexOf('@');
        var domainLabels = at >= 0 ? address[(at + 1)..].ToLowerInvariant().Split('.') : [];
        var text = MailText.Normalize($"{subject} {senderName}");

        var byContact = candidates.Where(c => c.ContactEmails.Any(e => string.Equals(e.Trim(), address, StringComparison.OrdinalIgnoreCase))).ToList();
        if (byContact.Count > 0)
        {
            return byContact;
        }

        var byDomain = candidates.Where(c => CompanyKey(c) is { Length: >= 3 } key && domainLabels.Contains(key.Replace(" ", string.Empty, StringComparison.Ordinal))).ToList();
        if (byDomain.Count > 0)
        {
            return byDomain;
        }

        return candidates.Where(c => CompanyKey(c) is { Length: >= 3 } key && TermSet.Compile([key], "company").IsMatch(text)).ToList();
    }

    private static string CompanyKey(MailApplicationCandidate candidate) => JobFingerprint.CompanyKey(candidate.Company);
}
