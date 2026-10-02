using JobPilot.Domain.Tailoring;

namespace JobPilot.Domain.Practice;

/// <summary>A STAR story built only from the master CV: every part cites the CV elements it comes from.</summary>
public sealed class StarStory
{
    public required string Title { get; set; }

    public List<string> Competencies { get; set; } = [];

    public required TraceableText Situation { get; set; }

    public required TraceableText Task { get; set; }

    public required TraceableText Action { get; set; }

    public required TraceableText Result { get; set; }

    /// <summary>Interview questions this story answers.</summary>
    public List<string> Questions { get; set; } = [];

    public IEnumerable<(string Part, TraceableText Text)> Parts()
    {
        yield return ("Situación", Situation);
        yield return ("Tarea", Task);
        yield return ("Acción", Action);
        yield return ("Resultado", Result);
    }
}
