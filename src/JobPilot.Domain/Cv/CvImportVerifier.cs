namespace JobPilot.Domain.Cv;

/// <summary>A warning about an imported value that could not be found in the source document.</summary>
/// <param name="ElementKey">The element's id, or a field key such as <c>contact.email</c>, <c>headline</c>, <c>summary</c>.</param>
public sealed record CvImportFlag(string ElementKey, string Message);

/// <summary>
/// Deterministic check of an LLM-structured CV against the text extracted from the original document: values
/// that do not appear in the source are flagged so the owner reviews them before saving. Never trusts the LLM.
/// </summary>
public static class CvImportVerifier
{
    /// <summary>Minimum share of an achievement's content words that must appear in the source.</summary>
    private const double MinimumWordCoverage = 0.6;

    public static IReadOnlyList<CvImportFlag> Verify(MasterCv cv, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(cv);
        var source = TextNormalizer.Normalize(sourceText);
        var sourceNumbers = TextNormalizer.Numbers(sourceText).ToHashSet();
        var flags = new List<CvImportFlag>();

        void RequireText(string key, string? value, string what)
        {
            if (!string.IsNullOrWhiteSpace(value) && !source.Contains(TextNormalizer.Normalize(value), StringComparison.Ordinal))
            {
                flags.Add(new CvImportFlag(key, $"{what} \"{value}\" no aparece tal cual en el documento."));
            }
        }

        void RequireNumbers(string key, string? value, string what)
        {
            var missing = TextNormalizer.Numbers(value).Where(n => !sourceNumbers.Contains(n)).ToList();
            if (missing.Count > 0)
            {
                flags.Add(new CvImportFlag(key, $"{what}: los números {string.Join(", ", missing)} no aparecen en el documento."));
            }
        }

        void RequireDate(string key, PartialDate? date, string what)
        {
            if (date is { } d && !sourceNumbers.Contains(d.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                flags.Add(new CvImportFlag(key, $"{what}: el año {d.Year} no aparece en el documento."));
            }
        }

        RequireText("contact.fullName", cv.Contact.FullName, "El nombre");
        RequireText("contact.email", cv.Contact.Email, "El email");
        RequireText("contact.location", cv.Contact.Location, "La ubicación");
        RequireNumbers("contact.phone", cv.Contact.Phone, "El teléfono");
        RequireNumbers("summary", cv.Summary, "El resumen");

        foreach (var experience in cv.Experiences)
        {
            var key = experience.Id.ToString();
            RequireText(key, experience.Company, "La empresa");
            RequireText(key, experience.Title, "El puesto");
            RequireDate(key, experience.Start, "Inicio");
            RequireDate(key, experience.End, "Fin");
            RequireNumbers(key, experience.Description, "La descripción");

            foreach (var technology in experience.Technologies)
            {
                RequireText(key, technology, "La tecnología");
            }

            foreach (var achievement in experience.Achievements)
            {
                var achievementKey = achievement.Id.ToString();
                RequireNumbers(achievementKey, achievement.Text, "El logro");

                var words = TextNormalizer.ContentWords(achievement.Text);
                if (words.Count > 0)
                {
                    var coverage = words.Count(w => source.Contains(w, StringComparison.Ordinal)) / (double)words.Count;
                    if (coverage < MinimumWordCoverage)
                    {
                        flags.Add(new CvImportFlag(achievementKey,
                            "El logro está redactado distinto al documento: revisá que no se haya agregado información."));
                    }
                }
            }
        }

        foreach (var skill in cv.Skills)
        {
            RequireText(skill.Id.ToString(), skill.Name, "La skill");
        }

        foreach (var certification in cv.Certifications)
        {
            RequireText(certification.Id.ToString(), certification.Name, "La certificación");
            RequireDate(certification.Id.ToString(), certification.Date, "Fecha");
        }

        foreach (var language in cv.Languages)
        {
            RequireText(language.Id.ToString(), language.Name, "El idioma");
        }

        foreach (var education in cv.Education)
        {
            var key = education.Id.ToString();
            RequireText(key, education.Institution, "La institución");
            RequireDate(key, education.Start, "Inicio");
            RequireDate(key, education.End, "Fin");
        }

        return flags;
    }
}
