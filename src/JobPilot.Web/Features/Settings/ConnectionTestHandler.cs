using System.ComponentModel;
using System.Security.Cryptography;
using JobPilot.Domain.Prompts;
using JobPilot.Web.Infrastructure.Llm;

namespace JobPilot.Web.Features.Settings;

public sealed record ConnectionTestResponse(
    [property: Description("Must be exactly \"ok\".")] string Status,
    [property: Description("The verification word, copied exactly.")] string Echo);

public sealed record PreparedConnectionTest(RenderedPrompt Prompt, string Word);

/// <summary>Minimal end-to-end check: API key, model, structured output and call logging.</summary>
public sealed class ConnectionTestHandler(PromptLibrary prompts, ILlmClient llm)
{
    public PreparedConnectionTest Prepare()
    {
        var word = "jp" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var prompt = prompts.Get(LlmUseCases.ConnectionTest).Render(new Dictionary<string, string> { ["word"] = word });
        return new PreparedConnectionTest(prompt, word);
    }

    public Task<LlmResult<ConnectionTestResponse>> RunAsync(PreparedConnectionTest test, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(test);
        return llm.GenerateAsync(
            new LlmRequest<ConnectionTestResponse>(test.Prompt) { Validate = response => Validate(response, test.Word) },
            cancellationToken);
    }

    internal static IReadOnlyList<string> Validate(ConnectionTestResponse response, string word)
    {
        var problems = new List<string>();
        if (response.Status != "ok")
        {
            problems.Add($"status must be \"ok\" but was \"{response.Status}\"");
        }

        if (response.Echo != word)
        {
            problems.Add($"echo must be \"{word}\" but was \"{response.Echo}\"");
        }

        return problems;
    }
}
