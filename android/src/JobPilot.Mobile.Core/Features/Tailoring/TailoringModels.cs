using System.ComponentModel;

namespace JobPilot.Mobile.Core.Features.Tailoring;

// Response schemas of the cv-tailoring and cover-letter use cases. Sources are short references shown in the prompt
// (E1, E1.A2, HEAD, SUM, PROF); the app maps them back to master CV element ids and checks every text.

public sealed record TracedTextResponse(
    [property: Description("The text, in English.")] string Text,
    [property: Description("References of the CV elements the text is based on (e.g. \"E1.A2\", \"E1\", \"SUM\", \"PROF\"). At least one.")] List<string> Sources);

public sealed record TailoredExperienceResponse(
    [property: Description("Reference of the experience, e.g. \"E1\".")] string Experience,
    [property: Description("Bullets for this experience, most relevant first. Each cites only this experience or its achievements.")] List<TracedTextResponse> Bullets);

public sealed record TailoredCvResponse(
    TracedTextResponse Headline,
    TracedTextResponse Summary,
    [property: Description("Experiences to include. Omit only those irrelevant to the offer.")] List<TailoredExperienceResponse> Experiences,
    [property: Description("Skills to show, most relevant first, using only names that appear in the CV.")] List<string> Skills,
    [property: Description("In Spanish (Rioplatense): what you prioritised and why, in 2-4 sentences.")] string Notes);

public sealed record CoverLetterResponse(
    [property: Description("Body paragraphs (3-4), without greeting or sign-off. Each cites the CV elements it relies on.")] List<TracedTextResponse> Paragraphs,
    [property: Description("In Spanish (Rioplatense): what you emphasised and why, in 2-3 sentences.")] string Notes);
