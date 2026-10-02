using System.ComponentModel;

namespace JobPilot.Web.Features.Profile.Import;

// Response schema of the cv-import use case. Descriptions are copied into the JSON schema sent to Gemini.

public sealed record ImportedCv(
    ImportedContact Contact,
    [property: Description("Professional title or headline exactly as written in the document, or null.")] string? Headline,
    [property: Description("Profile or summary paragraph copied verbatim, or null.")] string? Summary,
    [property: Description("Work experiences in the document's order.")] List<ImportedExperience> Experiences,
    [property: Description("Skills explicitly listed in the document.")] List<ImportedSkill> Skills,
    List<ImportedCertification> Certifications,
    List<ImportedLanguage> Languages,
    List<ImportedEducation> Education);

public sealed record ImportedContact(
    string? FullName,
    string? Email,
    string? Phone,
    [property: Description("City and/or country as written, or null.")] string? Location,
    string? LinkedInUrl,
    string? GitHubUrl,
    string? WebsiteUrl);

public sealed record ImportedExperience(
    string Company,
    [property: Description("Job title as written.")] string Title,
    string? Location,
    [property: Description("Start date as \"YYYY-MM\", or \"YYYY\" when only the year is stated, or null.")] string? Start,
    [property: Description("End date as \"YYYY-MM\" or \"YYYY\"; null when ongoing or not stated.")] string? End,
    [property: Description("True when the document says the role is ongoing (e.g. \"Present\").")] bool IsCurrent,
    [property: Description("Descriptive paragraph of the role copied verbatim (not the bullet points), or null.")] string? Description,
    [property: Description("Each bullet point / achievement copied verbatim, one item per bullet.")] List<string> Achievements,
    [property: Description("Technologies explicitly named in this experience.")] List<string> Technologies);

public sealed record ImportedSkill(
    string Name,
    [property: Description("Category used by the document (e.g. \"Languages\", \"Cloud\"), or null.")] string? Category);

public sealed record ImportedCertification(
    string Name,
    string? Issuer,
    [property: Description("\"YYYY-MM\" or \"YYYY\", or null.")] string? Date,
    string? CredentialUrl);

public sealed record ImportedLanguage(
    [property: Description("Language name as written.")] string Name,
    [property: Description("Proficiency as written (e.g. \"Native\", \"C1\"), or null.")] string? Level);

public sealed record ImportedEducation(
    string Institution,
    string? Degree,
    string? Field,
    [property: Description("\"YYYY-MM\" or \"YYYY\", or null.")] string? Start,
    [property: Description("\"YYYY-MM\" or \"YYYY\", or null.")] string? End);
