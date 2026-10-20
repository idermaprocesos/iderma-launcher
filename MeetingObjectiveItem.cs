using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Iderma_Launcher;

public sealed class MeetingObjectiveItem : INotifyPropertyChanged
{
    private bool _isDone;
    private string _text = string.Empty;

    public MeetingObjectiveItem(string id, string text, bool isDone = false)
    {
        Id = id;
        _text = text;
        _isDone = isDone;
    }

    public string Id { get; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            OnPropertyChanged();
        }
    }

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (_isDone == value)
            {
                return;
            }

            _isDone = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
