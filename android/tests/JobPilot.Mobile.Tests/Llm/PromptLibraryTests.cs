using JobPilot.Domain.Prompts;
using JobPilot.Mobile.Core.Infrastructure.Llm;

namespace JobPilot.Mobile.Tests.Llm;

public class PromptLibraryTests
{
    [Fact]
    public void Every_use_case_has_a_valid_embedded_prompt()
    {
        var library = PromptLibrary.LoadEmbedded();

        Assert.All(LlmUseCases.All, useCase => Assert.Equal(useCase.Name, library.Get(useCase.Name).UseCase));
    }

    [Fact]
    public void The_embedded_prompts_are_the_web_app_files()
    {
        var webPrompts = Directory.GetFiles(Path.Combine(AndroidFolder.RepositoryRoot, "src", "JobPilot.Web", "Prompts"), "*.md");
        var library = PromptLibrary.LoadEmbedded();

        Assert.NotEmpty(webPrompts);
        Assert.All(webPrompts, path =>
        {
            var file = PromptTemplate.Parse(Path.GetFileName(path), File.ReadAllText(path));
            var embedded = library.Get(file.UseCase);
            Assert.Equal((file.Id, file.System, file.User), (embedded.Id, embedded.System, embedded.User));
        });
    }

    [Fact]
    public void Android_use_cases_keep_the_web_names_and_labels()
    {
        // The web catalog is the reference: same name and label, so call logs and settings mean the same in both apps.
        var web = File.ReadAllText(Path.Combine(AndroidFolder.RepositoryRoot, "src", "JobPilot.Web", "Infrastructure", "Llm", "LlmUseCases.cs"));

        // Quotes inside the description appear escaped in the web source ("Acerca de").
        Assert.All(LlmUseCases.All, useCase => Assert.Contains(
            $"\"{useCase.Label}\", \"{useCase.Description.Replace("\"", "\\\"", StringComparison.Ordinal)}\"", web, StringComparison.Ordinal));
    }
}
