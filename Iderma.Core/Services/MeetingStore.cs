using System.Text.Encodings.Web;
using System.Text.Json;
using Iderma.Core.Models;

namespace Iderma.Core.Services;

public sealed class MeetingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Mutex FileMutex = new(false, @"Local\Iderma.MeetingsFile");

    private readonly string _filePath;
    private readonly object _sync = new();
    private List<Meeting> _meetings = [];
    private bool _loaded;

    public MeetingStore() : this(DefaultFilePath)
    {
    }

    public MeetingStore(string filePath)
    {
        _filePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
    }

    public event Action? Changed;

    public static string DefaultFilePath => Path.Combine(UserStore.DataDirectory, "meetings.json");

    public IReadOnlyList<Meeting> GetAll()
    {
        lock (_sync)
        {
            EnsureLoaded();
            return _meetings.Select(Clone).OrderBy(m => m.StartLocal).ToList();
        }
    }

    public IReadOnlyList<Meeting> GetUpcoming()
    {
        var now = DateTime.Now;
        return GetAll().Where(m => m.EndLocal >= now).OrderBy(m => m.StartLocal).ToList();
    }

    public IReadOnlyList<Meeting> GetPast()
    {
        var now = DateTime.Now;
        return GetAll().Where(m => m.EndLocal < now).OrderByDescending(m => m.StartLocal).ToList();
    }

    public Meeting? FindById(string id)
    {
        lock (_sync)
        {
            EnsureLoaded();
            var meeting = _meetings.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            return meeting is null ? null : Clone(meeting);
        }
    }

    public Meeting Add(Meeting meeting)
    {
        ArgumentNullException.ThrowIfNull(meeting);
        if (string.IsNullOrWhiteSpace(meeting.Id))
        {
            meeting.Id = Guid.NewGuid().ToString("N");
        }

        if (meeting.CreatedAt == default)
        {
            meeting.CreatedAt = DateTime.UtcNow;
        }

        lock (_sync)
        {
            EnsureLoaded();
            _meetings.Add(Clone(meeting));
            PersistUnlocked();
        }

        Changed?.Invoke();
        return Clone(meeting);
    }

    public bool Update(Meeting meeting)
    {
        ArgumentNullException.ThrowIfNull(meeting);
        lock (_sync)
        {
            EnsureLoaded();
            var index = _meetings.FindIndex(m => m.Id.Equals(meeting.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            _meetings[index] = Clone(meeting);
            PersistUnlocked();
        }

        Changed?.Invoke();
        return true;
    }

    public bool SetResponse(string meetingId, string userId, string displayName, MeetingResponse response)
    {
        lock (_sync)
        {
            EnsureLoaded();
            var meeting = _meetings.FirstOrDefault(m => m.Id.Equals(meetingId, StringComparison.OrdinalIgnoreCase));
            if (meeting is null)
            {
                return false;
            }

            var participant = meeting.Participants.FirstOrDefault(p =>
                p.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
            if (participant is null)
            {
                meeting.Participants.Add(new MeetingParticipant
                {
                    UserId = userId,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? userId : displayName,
                    Response = response
                });
            }
            else
            {
                participant.Response = response;
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    participant.DisplayName = displayName;
                }
            }

            PersistUnlocked();
        }

        Changed?.Invoke();
        return true;
    }

    public bool Delete(string id)
    {
        lock (_sync)
        {
            EnsureLoaded();
            var removed = _meetings.RemoveAll(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed)
            {
                return false;
            }

            PersistUnlocked();
        }

        Changed?.Invoke();
        return true;
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        LoadUnlocked();
        _loaded = true;
    }

    private void LoadUnlocked()
    {
        if (!File.Exists(_filePath))
        {
            _meetings = [];
            return;
        }

        try
        {
            FileMutex.WaitOne();
            try
            {
                var json = File.ReadAllText(_filePath);
                _meetings = string.IsNullOrWhiteSpace(json)
                    ? []
                    : JsonSerializer.Deserialize<List<Meeting>>(json, JsonOptions) ?? [];
            }
            finally
            {
                FileMutex.ReleaseMutex();
            }
        }
        catch
        {
            _meetings = [];
        }
    }

    private void PersistUnlocked()
    {
        var json = JsonSerializer.Serialize(_meetings, JsonOptions);
        FileMutex.WaitOne();
        try
        {
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Copy(temp, _filePath, overwrite: true);
            File.Delete(temp);
        }
        finally
        {
            FileMutex.ReleaseMutex();
        }
    }

    private static Meeting Clone(Meeting source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        StartLocal = source.StartLocal,
        DurationMinutes = source.DurationMinutes,
        IsLong = source.IsLong,
        IsVirtual = source.IsVirtual,
        VirtualUrl = source.VirtualUrl,
        Location = source.Location,
        OrganizerId = source.OrganizerId,
        OrganizerName = source.OrganizerName,
        CreatedAt = source.CreatedAt,
        Participants = source.Participants
            .Select(p => new MeetingParticipant
            {
                UserId = p.UserId,
                DisplayName = p.DisplayName,
                Response = p.Response
            })
            .ToList(),
        Objectives = source.Objectives
            .Select(o => new MeetingObjective
            {
                Id = o.Id,
                Text = o.Text,
                IsDone = o.IsDone
            })
            .ToList()
    };
}
