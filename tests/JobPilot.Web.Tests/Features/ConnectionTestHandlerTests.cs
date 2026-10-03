using JobPilot.Web.Features.Settings;
using JobPilot.Web.Infrastructure.Llm;
using JobPilot.Web.Tests.TestSupport;

namespace JobPilot.Web.Tests.Features;

public class ConnectionTestHandlerTests
{
    private static ConnectionTestHandler CreateHandler(FakeLlmClient llm) =>
        new(PromptLibrary.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Prompts")), llm);

    [Fact]
    public void Prepared_prompt_contains_only_a_random_verification_word()
    {
        var handler = CreateHandler(new FakeLlmClient());

        var first = handler.Prepare();
        var second = handler.Prepare();

        Assert.Contains(first.Word, first.Prompt.UserMessage, StringComparison.Ordinal);
        Assert.NotEqual(first.Word, second.Word);
        Assert.Equal("connection-test.v1", first.Prompt.PromptId);
    }

    [Fact]
    public async Task Sends_the_prepared_prompt_and_validates_the_echo()
    {
        var llm = new FakeLlmClient();
        var handler = CreateHandler(llm);
        var test = handler.Prepare();
        llm.Returns(new ConnectionTestResponse("ok", test.Word));

        var result = await handler.RunAsync(test, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Same(test.Prompt, Assert.Single(llm.Prompts));
        Assert.Empty(llm.ValidationProblems);
    }

    [Theory]
    [InlineData("ok", "wrong", "echo")]
    [InlineData("fine", null, "status")]
    public void Rejects_responses_that_do_not_follow_the_instructions(string status, string? echo, string field)
    {
        var problems = ConnectionTestHandler.Validate(new ConnectionTestResponse(status, echo ?? "jp1"), "jp1");

        Assert.Contains(problems, p => p.StartsWith(field, StringComparison.Ordinal));
    }
}
