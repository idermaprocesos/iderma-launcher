using System.Globalization;
using Iderma.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Iderma_Launcher;

public sealed partial class MeetingDetailsDialog : ContentDialog
{
    private static readonly CultureInfo EsPe = CultureInfo.GetCultureInfo("es-PE");
    private readonly Meeting _meeting;
    private readonly List<MeetingObjectiveItem> _objetivos;

    public bool Guardado { get; private set; }

    public MeetingDetailsDialog(Meeting meeting, string currentUserId)
    {
        InitializeComponent();
        _meeting = meeting;

        var participant = meeting.Participants.FirstOrDefault(p =>
            p.UserId.Equals(currentUserId, StringComparison.OrdinalIgnoreCase));
        var response = participant?.Response ?? MeetingResponse.Pending;
        var past = meeting.IsPast;

        Icono.Source = new BitmapImage(new Uri(MeetingIcons.ForCard(meeting, response, past)));
        TituloTexto.Text = string.IsNullOrWhiteSpace(meeting.Title) ? "Reunión" : meeting.Title;
        CuandoTexto.Text = $"Cuándo: {meeting.StartLocal.ToString("dddd d MMMM yyyy · HH:mm", EsPe)}";
        LugarTexto.Text = $"Lugar: {(string.IsNullOrWhiteSpace(meeting.Location) ? "—" : meeting.Location)}";
        DuracionTexto.Text = $"Duración: {meeting.DurationLabel}";
        OrganizadorTexto.Text = $"Organiza: {meeting.OrganizerName}";

        if (meeting.IsStreaming && !string.IsNullOrWhiteSpace(meeting.VirtualUrl)
            && Uri.TryCreate(meeting.VirtualUrl, UriKind.Absolute, out var enlace))
        {
            EnlaceBoton.Content = "Abrir enlace de la reunión";
            EnlaceBoton.NavigateUri = enlace;
            EnlaceBoton.Visibility = Visibility.Visible;
        }

        var personas = meeting.Participants
            .Select(p => $"{p.DisplayName} · {RespuestaTexto(p.Response)}")
            .ToList();
        PersonasLista.ItemsSource = personas;
        SinPersonas.Visibility = personas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _objetivos = meeting.Objectives
            .Select(o => new MeetingObjectiveItem(o.Id, o.Text, o.IsDone))
            .ToList();
        ObjetivosLista.ItemsSource = _objetivos;
        SinObjetivos.Visibility = _objetivos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string RespuestaTexto(MeetingResponse response) => response switch
    {
        MeetingResponse.Accepted => "Confirmó",
        MeetingResponse.Declined => "No asiste",
        _ => "Pendiente"
    };

    private void Editar_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args) =>
        GuardarObjetivos();

    private void Cerrar_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args) =>
        GuardarObjetivos();

    private void GuardarObjetivos()
    {
        _meeting.Objectives = _objetivos
            .Select(o => new MeetingObjective
            {
                Id = o.Id,
                Text = o.Text,
                IsDone = o.IsDone
            })
            .ToList();

        Guardado = App.Meetings.Update(_meeting);
    }
}
