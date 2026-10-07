using System.Net;
using JobPilot.Mobile.Core.Infrastructure.Google;
using JobPilot.Mobile.Tests.Google;

using static JobPilot.Mobile.Tests.Features.Mail.MailFixtures;

namespace JobPilot.Mobile.Tests.Features.Mail;

public class GmailFullThreadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Thread_ids_are_listed_with_the_query_and_truncation()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();
        host.Handler.On("GET", "/gmail/v1/users/me/threads", HttpStatusCode.OK, new { threads = new[] { new { id = "t1" }, new { id = "t2" } }, nextPageToken = "p2" });

        var page = (await host.Gmail.ListThreadIdsAsync("in:inbox newer_than:1d", 2, Ct)).Value!;

        Assert.Equal(["t1", "t2"], page.Ids);
        Assert.True(page.Truncated);
        Assert.Contains("q=in%3Ainbox%20newer_than%3A1d&maxResults=2&includeSpamTrash=false", host.Handler.Requests[^1].Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Full_threads_carry_headers_both_bodies_own_messages_and_invites_from_attachments()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();
        var start = new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero);
        host.Handler.On("GET", "/gmail/v1/users/me/messages/m2/attachments/a1", HttpStatusCode.OK, new { data = B64(Ics("REQUEST", start)) });
        host.Handler.On("GET", "/gmail/v1/users/me/threads/t1", HttpStatusCode.OK, new
        {
            id = "t1",
            messages = new[]
            {
                GmailMessage("m2", Received.AddHours(1), "Ana Recruiter <ana@example.com>", "Invitation: Technical interview", text: "See you there",
                    extraParts: [new { mimeType = "application/ics", filename = "invite.ics", body = new { attachmentId = "a1", size = 400 } }]),
                GmailMessage("m1", Received, "\"Ana Recruiter\" <ana@example.com>", "Interview", text: "Are you available? ñ", html: "<p>ignored</p>",
                    extraHeaders: [Header("X-LinkedIn-Template", "email_member_message_01"), Header("List-Id", "<list.example.com>")]),
                GmailMessage("m3", Received.AddHours(2), "Owner <owner@example.com>", "Re: Interview", text: "Yes", labels: ["SENT"]),
            },
        });

        var thread = (await host.Gmail.GetFullThreadAsync("t1", "owner@example.com", TimeZoneInfo.Utc, Ct)).Value!;

        Assert.Equal(["m1", "m2", "m3"], thread.Messages.Select(m => m.Id));
        var first = thread.First;
        Assert.Equal(("Ana Recruiter", "ana@example.com"), (first.From.Name, first.From.Address));
        Assert.Equal("Are you available? ñ", first.BodyText);
        Assert.Equal("<p>ignored</p>", first.HtmlBody);
        Assert.Equal("email_member_message_01", first.Header("x-linkedin-template"));
        Assert.Equal(Received, first.Received);
        var invite = Assert.Single(thread.Messages[1].Invites);
        Assert.Equal(("REQUEST", "uid-1@example.com", start, "NEEDS-ACTION"), (invite.Method, invite.Uid, invite.Start, invite.MyResponse));
        Assert.Equal("ana@example.com", invite.Organizer!.Address);
        Assert.True(thread.Latest.IsFromMe);
        Assert.False(first.IsFromMe);
    }

    [Fact]
    public async Task Html_only_messages_are_matched_on_their_text()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();
        host.Handler.On("GET", "/gmail/v1/users/me/threads/t1", HttpStatusCode.OK, new
        {
            id = "t1",
            messages = new[] { GmailMessage("m1", Received, "jobs@example.com", "Next steps", html: "<p>Complete the <b>assessment</b><script>x()</script></p>") },
        });

        var message = (await host.Gmail.GetFullThreadAsync("t1", null, TimeZoneInfo.Utc, Ct)).Value!.First;

        Assert.Null(message.TextBody);
        Assert.Contains("Complete the assessment", message.BodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("x()", message.BodyText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ana Pérez <ana@example.com>", "Ana Pérez", "ana@example.com")]
    [InlineData("\"Pérez, Ana\" <ana@example.com>", "Pérez, Ana", "ana@example.com")]
    [InlineData("ana@example.com", "", "ana@example.com")]
    [InlineData("<ana@example.com>", "", "ana@example.com")]
    public void Addresses_are_split_into_name_and_address(string value, string name, string address)
    {
        var person = GmailClient.ParseAddress(value);

        Assert.Equal((name, address), (person.Name, person.Address));
    }

    [Fact]
    public void Invalid_calendars_yield_no_invites()
    {
        Assert.Empty(IcsInviteParser.Parse("not a calendar", null, TimeZoneInfo.Utc));
    }
}
