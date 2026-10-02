using JobPilot.Domain.Practice;

namespace JobPilot.Web.Features.Interviews;

public static class InterviewFormat
{
    public static string Category(QuestionCategory category) => category switch
    {
        QuestionCategory.Behavioral => "Comportamiento",
        QuestionCategory.Technical => "Técnicas",
        QuestionCategory.SystemDesign => "Diseño de sistemas",
        _ => "Rol y empresa",
    };

    public static string Status(PracticeStatus status) => status switch
    {
        PracticeStatus.InProgress => "En curso",
        PracticeStatus.AwaitingFeedback => "Falta el feedback",
        _ => "Con feedback",
    };
}
