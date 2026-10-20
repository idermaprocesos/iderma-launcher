using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iderma_Launcher;

public sealed partial class MeetingHistoryDialog : ContentDialog
{
    public MeetingHistoryDialog(IReadOnlyList<MeetingCard> cards)
    {
        InitializeComponent();
        HistorialLista.ItemsSource = cards;
        VacioTexto.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistorialLista.Visibility = cards.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
