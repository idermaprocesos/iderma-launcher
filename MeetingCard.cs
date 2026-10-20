using System.Globalization;
using Iderma.Core.Models;

namespace Iderma_Launcher;

public sealed class MeetingCard
{
    private static readonly CultureInfo EsPe = CultureInfo.GetCultureInfo("es-PE");

    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string WhenLabel { get; init; }
    public required string DurationLabel { get; init; }
    public required string LocationLabel { get; init; }
    public required string LogoPath { get; init; }
    public required string YesIconPath { get; init; }
    public required string NoIconPath { get; init; }
    public bool IsPast { get; init; }
    public bool CanRespond { get; init; }
    public double CardOpacity { get; init; } = 1;

    public static MeetingCard From(Meeting meeting, string currentUserId)
    {
        var past = meeting.IsPast;
        var participant = meeting.Participants.FirstOrDefault(p =>
            p.UserId.Equals(currentUserId, StringComparison.OrdinalIgnoreCase));
        var response = participant?.Response ?? MeetingResponse.Pending;

        return new MeetingCard
        {
            Id = meeting.Id,
            Title = string.IsNullOrWhiteSpace(meeting.Title) ? "Reunión" : meeting.Title,
            WhenLabel = FormatWhen(meeting.StartLocal, past),
            DurationLabel = meeting.DurationLabel,
            LocationLabel = string.IsNullOrWhiteSpace(meeting.Location) ? "—" : meeting.Location,
            LogoPath = MeetingIcons.ForCard(meeting, response, past),
            YesIconPath = MeetingIcons.UriFor(MeetingIcons.Yes, past: false),
            NoIconPath = MeetingIcons.UriFor(MeetingIcons.No, past: false),
            IsPast = past,
            CanRespond = !past,
            CardOpacity = past ? 0.72 : 1
        };
    }

    private static string FormatWhen(DateTime start, bool past)
    {
        var today = DateTime.Today;
        if (start.Date == today)
        {
            return $"Hoy · {start:HH:mm}";
        }

        if (start.Date == today.AddDays(1) && !past)
        {
            return $"Mañana · {start:HH:mm}";
        }

        if (start.Date == today.AddDays(-1) && past)
        {
            return $"Ayer · {start:HH:mm}";
        }

        return start.ToString("ddd d MMM · HH:mm", EsPe);
    }
}
