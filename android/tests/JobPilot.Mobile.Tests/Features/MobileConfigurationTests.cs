using System.Text.Json.Nodes;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;

namespace JobPilot.Mobile.Tests.Features;

public class MobileConfigurationTests
{
    [Fact]
    public void Models_parameters_and_prices_come_from_the_web_app_settings()
    {
        var web = JsonNode.Parse(File.ReadAllText(Path.Combine(AndroidFolder.RepositoryRoot, "src", "JobPilot.Web", "appsettings.json")))!;
        var configuration = MobileConfiguration.Load();

        var llm = configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>()!;
        Assert.Equal((string?)web["Llm"]!["DefaultModel"], llm.DefaultModel);
        Assert.Equal(web["Llm"]!["Pricing"]!.AsObject().Select(p => p.Key).Order(), llm.Pricing.Keys.Order());
        Assert.Equal((string?)web["Gemini"]!["BaseUrl"], configuration["Gemini:BaseUrl"]);
    }

    [Fact]
    public void The_embedded_settings_contain_no_secrets()
    {
        var configuration = MobileConfiguration.Load();

        Assert.Null(configuration["Gemini:ApiKey"]);
        Assert.Null(configuration["Google:ClientSecret"]);
    }
}
