using System.ComponentModel;
using System.Text;
using System.Text.Json;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Practice;
using JobPilot.Domain.Prompts;
using JobPilot.Domain.Tailoring;
using JobPilot.Web.Features.Jobs.Analysis;
using JobPilot.Web.Features.Tailoring;
using JobPilot.Web.Features.Transfer;
using JobPilot.Web.Infrastructure.Llm;
using JobPilot.Web.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Web.Features.Interviews;

public enum QuestionCategory { Behavioral, Technical, SystemDesign, RoleAndCompany }

public sealed record InterviewQuestionResponse(
    QuestionCategory Category,
    [property: Description("The question, in English.")] string Question,
    [property: Description("In Spanish: why this interviewer would ask it, one sentence.")] string WhyLikely,
    [property: Description("Sentence of the posting that motivates the question, copied exactly; empty string for generic questions.")] string PostingQuote,
    [property: Description("In Spanish: how to approach the answer, one or two sentences.")] string Tips);

public sealed record InterviewQuestionsResponse(
    [property: Description("10 to 16 likely questions.")] List<InterviewQuestionResponse> Questions,
    [property: Description("3 to 5 questions the candidate can ask the interviewers, in English.")] List<string> QuestionsToAsk);

/// <param name="QuoteVerified">True when the quote appears in the posting; false when there is a quote that does not.</param>
public sealed record InterviewQuestion(QuestionCategory Category, string Question, string WhyLikely, string? PostingQuote, bool QuoteVerified, string Tips);

public sealed record InterviewQuestionSet(IReadOnlyList<InterviewQuestion> Questions, IReadOnlyList<string> QuestionsToAsk);

public sealed record StoredQuestionSet(long Id, DateTime CreatedAtUtc, string Model, InterviewQuestionSet Set);

public sealed record StarStoryResponse(
    [property: Description("Short title of the story.")] string Title,
    [property: Description("Competencies it shows, in English, short.")] List<string> Competencies,
    TracedTextResponse Situation,
    TracedTextResponse Task,
    TracedTextResponse Action,
    TracedTextResponse Result,
    [property: Description("Numbers of the behavioral questions it answers; empty if none.")] List<int> QuestionNumbers);

public sealed record StarStoriesResponse(
    [property: Description("3 to 5 stories.")] List<StarStoryResponse> Stories,
    [property: Description("In Spanish (Rioplatense): which experiences you chose and why, 1-2 sentences.")] string Notes);

public sealed record StoredStory(long Id, Guid? PostingId, int MasterCvVersion, DateTime UpdatedAtUtc, StarStory Story);

public sealed record PreparedInterviewQuestions(JobPosting Posting, RenderedPrompt Prompt);

public sealed record PreparedStarStories(TailoringContext Context, IReadOnlyList<string> BehavioralQuestions, RenderedPrompt Prompt);

/// <summary>Question banks per offer and the STAR story library.</summary>
public sealed class InterviewPrepStore(IDbContextFactory<JobPilotDbContext> dbFactory, TimeProvider time)
{
    public async Task<StoredQuestionSet?> LatestQuestionsAsync(Guid postingId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.InterviewQuestionSets.AsNoTracking().Where(q => q.JobPostingId == postingId)
            .OrderByDescending(q => q.CreatedAtUtc).ThenByDescending(q => q.Id).FirstOrDefaultAsync(cancellationToken);
        return record is null ? null : new StoredQuestionSet(record.Id, record.CreatedAtUtc, record.Model, JsonSerializer.Deserialize<InterviewQuestionSet>(record.Json, DomainJson.Options)!);
    }

    public async Task AddQuestionsAsync(Guid postingId, InterviewQuestionSet set, string promptId, string model, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.InterviewQuestionSets.Add(new InterviewQuestionSetRecord
        {
            JobPostingId = postingId, CreatedAtUtc = time.GetUtcNow().UtcDateTime, PromptId = promptId, Model = model,
            Json = JsonSerializer.Serialize(set, DomainJson.Options),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <param name="postingId">Stories prepared for one offer; null for all.</param>
    public async Task<IReadOnlyList<StoredStory>> StoriesAsync(Guid? postingId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.StarStories.AsNoTracking();
        if (postingId is { } id)
        {
            query = query.Where(s => s.JobPostingId == id);
        }

        var records = await query.ToListAsync(cancellationToken);
        return records.OrderByDescending(r => r.UpdatedAtUtc).Select(ToStored).ToList();
    }

    public async Task<IReadOnlyList<long>> AddStoriesAsync(Guid? postingId, int cvVersion, IEnumerable<StarStory> stories, string promptId, string model, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var records = stories.Select(s => new StarStoryRecord
        {
            JobPostingId = postingId, MasterCvVersion = cvVersion, CreatedAtUtc = now, UpdatedAtUtc = now, PromptId = promptId, Model = model,
            Json = JsonSerializer.Serialize(s, DomainJson.Options),
        }).ToList();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.StarStories.AddRange(records);
        await db.SaveChangesAsync(cancellationToken);
        return records.Select(r => r.Id).ToList();
    }

    public async Task SaveStoryAsync(long id, StarStory story, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.StarStories.FirstAsync(s => s.Id == id, cancellationToken);
        record.Json = JsonSerializer.Serialize(story, DomainJson.Options);
        record.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteStoryAsync(long id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await DeletionLog.DeleteAsync(db, TransferSchema.StarStories, id, ct => db.StarStories.Where(s => s.Id == id).ExecuteDeleteAsync(ct), time.GetUtcNow().UtcDateTime, cancellationToken);
    }

    private static StoredStory ToStored(StarStoryRecord record) =>
        new(record.Id, record.JobPostingId, record.MasterCvVersion, record.UpdatedAtUtc, JsonSerializer.Deserialize<StarStory>(record.Json, DomainJson.Options)!);
}

/// <summary>Generates the question bank of an offer; each question's posting quote is checked against the posting.</summary>
public sealed class InterviewQuestionsHandler(
    TailoringContextFactory contexts,
    JobAnalysisReader analyses,
    InterviewPrepStore store,
    PromptLibrary prompts,
    ILlmClient llm)
{
    public async Task<(PreparedInterviewQuestions? Prepared, string? Error)> PrepareAsync(Guid postingId, CancellationToken cancellationToken)
    {
        var (context, error) = await contexts.CreateAsync(postingId, cvVersion: null, cancellationToken);
        if (context is null)
        {
            return (null, error);
        }

        var prompt = prompts.Get(LlmUseCases.InterviewQuestions).Render(new Dictionary<string, string>
        {
            ["cv"] = context.CvWithReferences(),
            ["analysis"] = await analyses.LatestSummaryAsync(postingId, cancellationToken) ?? "(no analysis yet)",
            ["posting"] = JobAnalysisHandler.PostingText(context.Posting),
        });
        return (new PreparedInterviewQuestions(context.Posting, prompt), null);
    }

    public async Task<(InterviewQuestionSet? Set, LlmError? Error)> RunAsync(PreparedInterviewQuestions prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var result = await llm.GenerateAsync(new LlmRequest<InterviewQuestionsResponse>(prepared.Prompt) { Validate = Validate }, cancellationToken);
        if (!result.IsSuccess)
        {
            return (null, result.Error);
        }

        var set = ToSet(prepared.Posting, result.Value!);
        await store.AddQuestionsAsync(prepared.Posting.Id, set, prepared.Prompt.PromptId, result.Model, cancellationToken);
        return (set, null);
    }

    internal static InterviewQuestionSet ToSet(JobPosting posting, InterviewQuestionsResponse response)
    {
        ArgumentNullException.ThrowIfNull(posting);
        ArgumentNullException.ThrowIfNull(response);
        var text = posting.SearchableText;
        return new InterviewQuestionSet(
            response.Questions.Select(q =>
            {
                var quote = string.IsNullOrWhiteSpace(q.PostingQuote) ? null : q.PostingQuote.Trim();
                return new InterviewQuestion(q.Category, q.Question.Trim(), q.WhyLikely.Trim(), quote, quote is not null && EvidenceCheck.QuoteFound(quote, text), q.Tips.Trim());
            }).ToList(),
            response.QuestionsToAsk.Where(q => !string.IsNullOrWhiteSpace(q)).Select(q => q.Trim()).ToList());
    }

    internal static IReadOnlyList<string> Validate(InterviewQuestionsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var problems = new List<string>();
        if (response.Questions.Count is < 5 or > 25)
        {
            problems.Add("Write between 10 and 16 questions.");
        }

        if (response.Questions.Any(q => string.IsNullOrWhiteSpace(q.Question)))
        {
            problems.Add("A question is empty.");
        }

        return problems;
    }
}

/// <summary>Writes STAR stories from the master CV; every part is checked by the traceability validator.</summary>
public sealed class StarStoriesHandler(
    TailoringContextFactory contexts,
    InterviewPrepStore store,
    PromptLibrary prompts,
    ILlmClient llm)
{
    public async Task<(PreparedStarStories? Prepared, string? Error)> PrepareAsync(Guid postingId, CancellationToken cancellationToken)
    {
        var (context, error) = await contexts.CreateAsync(postingId, cvVersion: null, cancellationToken);
        if (context is null)
        {
            return (null, error);
        }

        var behavioral = (await store.LatestQuestionsAsync(postingId, cancellationToken))?.Set.Questions
            .Where(q => q.Category == QuestionCategory.Behavioral).Select(q => q.Question).ToList() ?? [];
        var questions = new StringBuilder();
        for (var i = 0; i < behavioral.Count; i++)
        {
            questions.AppendLine($"{i + 1}. {behavioral[i]}");
        }

        var prompt = prompts.Get(LlmUseCases.StarStories).Render(new Dictionary<string, string>
        {
            ["cv"] = context.CvWithReferences(),
            ["emphases"] = context.Emphases(),
            ["questions"] = behavioral.Count == 0 ? "(none yet: cover common behavioral competencies)" : questions.ToString().Trim(),
            ["posting"] = JobAnalysisHandler.PostingText(context.Posting),
        });
        return (new PreparedStarStories(context, behavioral, prompt), null);
    }

    public async Task<(IReadOnlyList<long> StoryIds, LlmError? Error, IReadOnlyList<string> Warnings)> RunAsync(PreparedStarStories prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var context = prepared.Context;
        var result = await llm.GenerateAsync(new LlmRequest<StarStoriesResponse>(prepared.Prompt)
        {
            Validate = response => Validate(context, response),
            Review = response => response.Stories.SelectMany(s => Problems(context, ToStory(context, prepared.BehavioralQuestions, s)).Select(p => $"{s.Title}: {p}")).ToList(),
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            return ([], result.Error, []);
        }

        var stories = result.Value!.Stories.Select(s => ToStory(context, prepared.BehavioralQuestions, s)).ToList();
        var ids = await store.AddStoriesAsync(context.Posting.Id, context.CvVersion, stories, prepared.Prompt.PromptId, result.Model, cancellationToken);
        return (ids, null, result.Warnings);
    }

    internal static StarStory ToStory(CvContext context, IReadOnlyList<string> questions, StarStoryResponse response) => new()
    {
        Title = response.Title.Trim(),
        Competencies = response.Competencies.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList(),
        Situation = context.ToTraceable(response.Situation),
        Task = context.ToTraceable(response.Task),
        Action = context.ToTraceable(response.Action),
        Result = context.ToTraceable(response.Result),
        Questions = response.QuestionNumbers.Where(n => n >= 1 && n <= questions.Count).Distinct().Select(n => questions[n - 1]).ToList(),
    };

    /// <summary>Traceability problems of each part (Spanish), keyed by part name.</summary>
    public static IReadOnlyList<string> Problems(CvContext context, StarStory story)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(story);
        return story.Parts().SelectMany(p => context.Validator.Check(p.Text).Select(problem => $"{p.Part}: {problem}")).ToList();
    }

    /// <summary>Problems that block nothing but must be reviewed: flagged parts not confirmed by the owner.</summary>
    public static IReadOnlyList<string> Unconfirmed(CvContext context, StarStory story)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(story);
        return story.Parts().Where(p => !p.Text.OwnerConfirmed).SelectMany(p => context.Validator.Check(p.Text).Select(problem => $"{p.Part}: {problem}")).ToList();
    }

    internal static IReadOnlyList<string> Validate(CvContext context, StarStoriesResponse response)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);
        var problems = new List<string>();
        if (response.Stories.Count is 0 or > 8)
        {
            problems.Add("Write 3 to 5 stories.");
        }

        foreach (var story in response.Stories)
        {
            foreach (var part in new[] { story.Situation, story.Task, story.Action, story.Result })
            {
                if (string.IsNullOrWhiteSpace(part.Text))
                {
                    problems.Add($"{story.Title}: a part is empty.");
                }

                foreach (var reference in part.Sources.Where(r => context.Index.KeyOf(r) is null))
                {
                    problems.Add($"{story.Title}: \"{reference}\" is not a reference in the CV.");
                }
            }
        }

        return problems;
    }
}
