using Bunit;
using JobPilot.Domain.Cv;
using JobPilot.Web.Features.Profile;
using MudBlazor.Services;

namespace JobPilot.Web.Tests.Features.Profile;

public class MasterCvEditorTests : BunitContext
{
    public MasterCvEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void Shows_import_flags_next_to_the_flagged_elements()
    {
        var cv = new MasterCv
        {
            Experiences = [new CvExperience { Company = "Acme", Title = "Dev", Achievements = [new CvAchievement { Text = "Grew 90%" }] }],
            Skills = [new CvSkill { Name = "Kubernetes" }],
        };
        cv.EnsureIds();
        CvImportFlag[] flags =
        [
            new(cv.Experiences[0].Achievements[0].Id.ToString(), "El logro: los números 90 no aparecen en el documento."),
            new(cv.Skills[0].Id.ToString(), "La skill \"Kubernetes\" no aparece tal cual en el documento."),
        ];

        var cut = Render<MasterCvEditor>(p => p.Add(c => c.Cv, cv).Add(c => c.Flags, flags));

        Assert.Contains("Hay 2 datos", cut.Find("[data-testid=import-flags-summary]").TextContent, StringComparison.Ordinal);
        var rendered = cut.FindAll(".jp-import-flag").Select(e => e.TextContent.Trim()).ToList();
        Assert.Contains(rendered, t => t.Contains("90", StringComparison.Ordinal));
        Assert.Contains(rendered, t => t.Contains("Kubernetes", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_flags_there_is_no_warning()
    {
        var cut = Render<MasterCvEditor>(p => p.Add(c => c.Cv, new MasterCv()));

        Assert.Empty(cut.FindAll("[data-testid=import-flags-summary]"));
        Assert.Empty(cut.FindAll(".jp-import-flag"));
    }
}
