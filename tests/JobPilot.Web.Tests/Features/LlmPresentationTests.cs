using Bunit;
using JobPilot.Domain.Llm;
using JobPilot.Domain.Prompts;
using JobPilot.Web.Components.Shared;
using MudBlazor.Services;

namespace JobPilot.Web.Tests.Features;

public class LlmPresentationTests : BunitContext
{
    public LlmPresentationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void Payload_preview_shows_exactly_what_is_sent()
    {
        var prompt = new RenderedPrompt("demo.v1", "demo", 1, "System text.", "User text with <untrusted_data name=\"x\">data</untrusted_data>");

        var cut = Render<LlmPayloadPreview>(p => p
            .Add(c => c.Prompt, prompt)
            .Add(c => c.Model, "gemini-test")
            .Add(c => c.Expanded, true));

        Assert.Equal(prompt.SystemInstruction, cut.Find("[data-testid=payload-system]").TextContent);
        Assert.Equal(prompt.UserMessage, cut.Find("[data-testid=payload-user]").TextContent);
        Assert.Contains("gemini-test", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_error_kind_has_a_spanish_label()
    {
        Assert.All(Enum.GetValues<LlmErrorKind>(), kind => Assert.NotEqual("Error", LlmFormat.ErrorKind(kind)));
    }

    [Fact]
    public void Missing_prices_are_shown_as_such_instead_of_zero()
    {
        Assert.Equal("sin precio configurado", LlmFormat.Cost(null));
    }
}
