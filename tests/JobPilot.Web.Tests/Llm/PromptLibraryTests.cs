using JobPilot.Domain.Prompts;
using JobPilot.Web.Infrastructure.Llm;

namespace JobPilot.Web.Tests.Llm;

public class PromptLibraryTests
{
    [Fact]
    public void Every_use_case_has_a_valid_prompt_file()
    {
        var library = PromptLibrary.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Prompts"));

        Assert.All(LlmUseCases.All, useCase => Assert.Equal(useCase.Name, library.Get(useCase.Name).UseCase));
    }

    [Fact]
    public void The_highest_version_is_active()
    {
        var library = new PromptLibrary(
        [
            PromptTemplate.Parse("demo.v1.md", "## system\nv1\n## user\nu"),
            PromptTemplate.Parse("demo.v3.md", "## system\nv3\n## user\nu"),
            PromptTemplate.Parse("demo.v2.md", "## system\nv2\n## user\nu"),
        ]);

        Assert.Equal(3, library.Get("demo").Version);
    }

    [Fact]
    public void Unknown_use_cases_fail()
    {
        var library = new PromptLibrary([]);

        Assert.Throws<KeyNotFoundException>(() => library.Get("missing"));
    }
}
