namespace Iderma.Core.Models;

public enum MeetingResponse
{
    Pending = 0,
    Accepted = 1,
    Declined = 2
}

public sealed class MeetingParticipant
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public MeetingResponse Response { get; set; } = MeetingResponse.Pending;
}

public sealed class MeetingObjective
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}

public sealed class Meeting
{
    public static readonly string[] Locations =
    [
        "Clínica",
        "Oficina administrativa",
        "Streaming"
    ];

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public DateTime StartLocal { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public bool IsLong { get; set; }
    public bool IsVirtual { get; set; }
    public string VirtualUrl { get; set; } = string.Empty;
    public string Location { get; set; } = Locations[0];
    public string OrganizerId { get; set; } = string.Empty;
    public string OrganizerName { get; set; } = string.Empty;
    public List<MeetingParticipant> Participants { get; set; } = [];
    public List<MeetingObjective> Objectives { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime EndLocal => StartLocal.AddMinutes(Math.Max(1, DurationMinutes));

    public bool IsPast => EndLocal < DateTime.Now;

    public bool IsUpcoming => !IsPast;

    public bool IsStreaming =>
        Location.Equals("Streaming", StringComparison.OrdinalIgnoreCase)
        || IsVirtual;

    public string DurationLabel
    {
        get
        {
            if (IsLong || DurationMinutes >= 120)
            {
                return "Larga";
            }

            return DurationMinutes switch
            {
                30 => "30 min",
                60 => "1 h",
                90 => "1 h 30",
                _ => $"{DurationMinutes} min"
            };
        }
    }
}
