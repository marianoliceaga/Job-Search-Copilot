using System.Text;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using JobPilot.Domain.Pipeline;

namespace JobPilot.Mobile.Core.Features.Pipeline;

/// <summary>Exports interviews and follow-up reminders as an iCalendar (.ics) file for any calendar app.</summary>
public sealed class CalendarExport(ApplicationStore applications, PipelineSettingsStore settings, Jobs.JobListService jobs, TimeProvider time)
{
    public const string ContentType = "text/calendar";

    /// <param name="applicationId">One application, or null for the whole pipeline.</param>
    public async Task<(string FileName, byte[] Content, int Items)> ExportAsync(Guid? applicationId, CancellationToken cancellationToken)
    {
        var policy = await settings.GetAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var all = await applications.GetAllAsync(cancellationToken);
        var items = new List<CalendarItem>();
        foreach (var application in all.Where(a => applicationId is null || a.Id == applicationId))
        {
            var posting = (await jobs.GetItemAsync(application.JobPostingId, cancellationToken))?.Posting;
            var offer = posting is null ? "Oferta" : $"{posting.Title} — {posting.Company}";
            var url = posting?.Url is { } link && link.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? link : null;
            items.AddRange(PipelineCalendar.Items(application, offer, url, policy, now, time.LocalTimeZone));
        }

        var name = applicationId is null ? "jobsearch-copilot-pipeline.ics" : "jobsearch-copilot-postulacion.ics";
        return (name, Encoding.UTF8.GetBytes(Serialize(items, now)), items.Count);
    }

    public static string Serialize(IEnumerable<CalendarItem> items, DateTime stampUtc)
    {
        ArgumentNullException.ThrowIfNull(items);
        var calendar = new Calendar { ProductId = "-//JobSearch Copilot//Pipeline//ES" };
        foreach (var item in items)
        {
            var calendarEvent = new CalendarEvent
            {
                Uid = item.Uid,
                Summary = item.Summary,
                Description = string.IsNullOrEmpty(item.Description) ? null : item.Description,
                DtStamp = new CalDateTime(stampUtc, CalDateTime.UtcTzId, true),
                Url = item.Url is null ? null : new Uri(item.Url),
            };
            if (item.Date is { } date)
            {
                calendarEvent.DtStart = new CalDateTime(date);
                calendarEvent.DtEnd = new CalDateTime(date.AddDays(1));
            }
            else
            {
                calendarEvent.DtStart = new CalDateTime(item.StartUtc!.Value, CalDateTime.UtcTzId, true);
                calendarEvent.DtEnd = new CalDateTime(item.EndUtc!.Value, CalDateTime.UtcTzId, true);
            }

            calendar.Events.Add(calendarEvent);
        }

        return new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;
    }
}
