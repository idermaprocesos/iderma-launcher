namespace Iderma_Launcher;

public sealed class MeetingPersonItem(string userId, string displayName)
{
    public string UserId { get; } = userId;
    public string DisplayName { get; } = displayName;
}
