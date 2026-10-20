using Iderma.Core.Models;

namespace Iderma_Launcher;

internal static class MeetingIcons
{
    public const string Agenda = "agenda.png";
    public const string Virtual = "virtual.png";
    public const string Long = "reunion-larga.png";
    public const string Yes = "si.png";
    public const string No = "no.png";
    public const string Reschedule = "reprogramacion.png";

    public static string UriFor(string fileName, bool past)
    {
        var folder = past ? "disabled" : "colored";
        var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Meetings", folder);
        var path = Path.Combine(dir, fileName);
        if (File.Exists(path))
        {
            return new Uri(path).AbsoluteUri;
        }

        if (Directory.Exists(dir))
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var match = Directory.EnumerateFiles(dir, "*.png")
                .FirstOrDefault(f =>
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    return string.Equals(name, stem, StringComparison.OrdinalIgnoreCase)
                        || name.Contains(stem.Replace("-", " "), StringComparison.OrdinalIgnoreCase)
                        || stem.Replace("-", " ").Contains(name, StringComparison.OrdinalIgnoreCase);
                });
            if (match is not null)
            {
                return new Uri(match).AbsoluteUri;
            }
        }

        return $"ms-appx:///Assets/Meetings/{folder}/{fileName}";
    }

    public static string ForMeeting(Meeting meeting, bool past) =>
        ForMeeting(
            meeting.IsStreaming,
            meeting.IsLong,
            meeting.DurationMinutes,
            past);

    public static string ForMeeting(bool isVirtual, bool isLong, int durationMinutes, bool past)
    {
        if (isVirtual)
        {
            return UriFor(Virtual, past);
        }

        if (isLong || durationMinutes >= 120)
        {
            return UriFor(Long, past);
        }

        return UriFor(Agenda, past);
    }

    public static string ForCard(Meeting meeting, MeetingResponse response, bool past)
    {
        if (!past)
        {
            if (response == MeetingResponse.Accepted)
            {
                return UriFor(Yes, past: false);
            }

            if (response == MeetingResponse.Declined)
            {
                return UriFor(No, past: false);
            }
        }

        return ForMeeting(meeting, past);
    }
}
