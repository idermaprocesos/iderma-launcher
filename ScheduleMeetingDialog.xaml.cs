using Iderma.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Iderma_Launcher;

public sealed partial class ScheduleMeetingDialog : ContentDialog
{
    private readonly List<UserAccount> _cuentas;
    private readonly List<MeetingParticipant> _participantes = [];
    private readonly List<MeetingObjectiveItem> _objetivos = [];
    private readonly UserAccount _organizador;
    private readonly Meeting? _existente;
    private int _duracionMinutos = 30;
    private bool _esLarga;
    private bool _actualizandoDuracion;

    public Meeting? Resultado { get; private set; }

    public bool EsEdicion => _existente is not null;

    public ScheduleMeetingDialog(
        UserAccount organizador,
        IReadOnlyList<UserAccount> cuentas,
        Meeting? existente = null)
    {
        InitializeComponent();
        _organizador = organizador;
        _existente = existente;
        _cuentas = cuentas
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        LugarCombo.ItemsSource = Meeting.Locations.ToList();
        PersonaCombo.ItemsSource = _cuentas.Select(u => u.DisplayName).ToList();

        if (existente is not null)
        {
            Title = "Editar reunión";
            PrimaryButtonText = "Guardar";
            CargarExistente(existente);
        }
        else
        {
            LugarCombo.SelectedIndex = 0;
            var ahora = DateTime.Now;
            FechaPicker.Date = new DateTimeOffset(ahora.Date);
            HoraPicker.SelectedTime = RedondearA15(ahora);
            ActualizarIcono();
            RefrescarPersonas();
            RefrescarObjetivos();
        }
    }

    private void CargarExistente(Meeting meeting)
    {
        TituloBox.Text = meeting.Title;
        EnlaceBox.Text = meeting.VirtualUrl;

        var lugarIndex = Array.FindIndex(
            Meeting.Locations,
            l => l.Equals(meeting.Location, StringComparison.OrdinalIgnoreCase));
        LugarCombo.SelectedIndex = lugarIndex >= 0 ? lugarIndex : 0;

        FechaPicker.Date = new DateTimeOffset(meeting.StartLocal.Date);
        HoraPicker.SelectedTime = new TimeSpan(meeting.StartLocal.Hour, meeting.StartLocal.Minute, 0);

        _actualizandoDuracion = true;
        try
        {
            _esLarga = meeting.IsLong || meeting.DurationMinutes >= 120;
            _duracionMinutos = _esLarga ? Math.Max(180, meeting.DurationMinutes) : meeting.DurationMinutes;
            Duracion30.IsChecked = !_esLarga && _duracionMinutos == 30;
            Duracion60.IsChecked = !_esLarga && _duracionMinutos == 60;
            Duracion90.IsChecked = !_esLarga && _duracionMinutos == 90;
            DuracionLarga.IsChecked = _esLarga;
            if (!Duracion30.IsChecked.GetValueOrDefault()
                && !Duracion60.IsChecked.GetValueOrDefault()
                && !Duracion90.IsChecked.GetValueOrDefault()
                && !DuracionLarga.IsChecked.GetValueOrDefault())
            {
                Duracion30.IsChecked = true;
                _duracionMinutos = 30;
                _esLarga = false;
            }
        }
        finally
        {
            _actualizandoDuracion = false;
        }

        VirtualSwitch.IsOn = meeting.IsVirtual || meeting.IsStreaming;
        EnlaceBox.Visibility = VirtualSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;

        _participantes.Clear();
        foreach (var p in meeting.Participants)
        {
            if (p.UserId.Equals(meeting.OrganizerId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _participantes.Add(new MeetingParticipant
            {
                UserId = p.UserId,
                DisplayName = p.DisplayName,
                Response = p.Response
            });
        }

        _objetivos.Clear();
        foreach (var o in meeting.Objectives)
        {
            _objetivos.Add(new MeetingObjectiveItem(o.Id, o.Text, o.IsDone));
        }

        ActualizarIcono();
        RefrescarPersonas();
        RefrescarObjetivos();
    }

    private static TimeSpan RedondearA15(DateTime ahora)
    {
        var minutos = ((ahora.Minute / 15) + 1) * 15;
        var hora = new TimeSpan(ahora.Hour, 0, 0).Add(TimeSpan.FromMinutes(minutos));
        return hora.TotalHours >= 24 ? new TimeSpan(23, 45, 0) : hora;
    }

    private void Duracion_Checked(object sender, RoutedEventArgs e)
    {
        if (_actualizandoDuracion || sender is not ToggleButton boton || boton.Tag is not string tag)
        {
            return;
        }

        _actualizandoDuracion = true;
        try
        {
            Duracion30.IsChecked = boton == Duracion30;
            Duracion60.IsChecked = boton == Duracion60;
            Duracion90.IsChecked = boton == Duracion90;
            DuracionLarga.IsChecked = boton == DuracionLarga;
            _esLarga = boton == DuracionLarga;
            _duracionMinutos = _esLarga
                ? 180
                : int.TryParse(tag, out var mins) ? mins : 30;
            ActualizarIcono();
        }
        finally
        {
            _actualizandoDuracion = false;
        }
    }

    private void LugarCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var lugar = LugarCombo.SelectedItem as string ?? string.Empty;
        if (lugar.Equals("Streaming", StringComparison.OrdinalIgnoreCase) && !VirtualSwitch.IsOn)
        {
            VirtualSwitch.IsOn = true;
        }

        ActualizarIcono();
    }

    private void VirtualSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        EnlaceBox.Visibility = VirtualSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;
        ActualizarIcono();
    }

    private void ActualizarIcono()
    {
        var streaming = VirtualSwitch.IsOn
            || string.Equals(LugarCombo.SelectedItem as string, "Streaming", StringComparison.OrdinalIgnoreCase);
        var uri = MeetingIcons.ForMeeting(streaming, _esLarga, _duracionMinutos, past: false);
        PreviewIcono.Source = new BitmapImage(new Uri(uri));
    }

    private void AgregarObjetivo_Click(object sender, RoutedEventArgs e)
    {
        var texto = ObjetivoBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(texto))
        {
            MostrarError("Escribe un objetivo.");
            return;
        }

        _objetivos.Add(new MeetingObjectiveItem(Guid.NewGuid().ToString("N"), texto));
        ObjetivoBox.Text = string.Empty;
        ErrorTexto.Visibility = Visibility.Collapsed;
        RefrescarObjetivos();
    }

    private void QuitarObjetivo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        _objetivos.RemoveAll(o => o.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        RefrescarObjetivos();
    }

    private void RefrescarObjetivos()
    {
        ObjetivosLista.ItemsSource = null;
        ObjetivosLista.ItemsSource = _objetivos.ToList();
        SinObjetivos.Visibility = _objetivos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AgregarPersona_Click(object sender, RoutedEventArgs e)
    {
        if (PersonaCombo.SelectedItem is not string nombre || string.IsNullOrWhiteSpace(nombre))
        {
            MostrarError("Elige una persona de la lista de Cuentas.");
            return;
        }

        var cuenta = _cuentas.FirstOrDefault(u =>
            u.DisplayName.Equals(nombre, StringComparison.OrdinalIgnoreCase));
        if (cuenta is null)
        {
            MostrarError("Elige una persona de la lista de Cuentas.");
            return;
        }

        if (_participantes.Any(p => p.UserId.Equals(cuenta.Id, StringComparison.OrdinalIgnoreCase))
            || cuenta.Id.Equals(_organizador.Id, StringComparison.OrdinalIgnoreCase)
            || (_existente is not null
                && cuenta.Id.Equals(_existente.OrganizerId, StringComparison.OrdinalIgnoreCase)))
        {
            MostrarError("Esa persona ya está en la reunión.");
            return;
        }

        _participantes.Add(new MeetingParticipant
        {
            UserId = cuenta.Id,
            DisplayName = cuenta.DisplayName,
            Response = MeetingResponse.Pending
        });
        PersonaCombo.SelectedItem = null;
        ErrorTexto.Visibility = Visibility.Collapsed;
        RefrescarPersonas();
    }

    private void QuitarPersona_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string userId })
        {
            return;
        }

        _participantes.RemoveAll(p => p.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
        RefrescarPersonas();
    }

    private void RefrescarPersonas()
    {
        PersonasLista.ItemsSource = null;
        PersonasLista.ItemsSource = _participantes
            .Select(p => new MeetingPersonItem(p.UserId, p.DisplayName))
            .ToList();
        SinPersonas.Visibility = _participantes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Guardar_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var titulo = TituloBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(titulo))
        {
            args.Cancel = true;
            MostrarError("Indica el asunto de la reunión.");
            return;
        }

        if (LugarCombo.SelectedItem is not string lugar || string.IsNullOrWhiteSpace(lugar))
        {
            args.Cancel = true;
            MostrarError("Elige el lugar de la reunión.");
            return;
        }

        if (FechaPicker.Date is null)
        {
            args.Cancel = true;
            MostrarError("Elige una fecha.");
            return;
        }

        var fecha = FechaPicker.Date.Value.Date;
        var hora = HoraPicker.SelectedTime ?? TimeSpan.FromHours(9);
        var inicio = fecha + hora;
        if (_existente is null && inicio < DateTime.Now.AddMinutes(-1))
        {
            args.Cancel = true;
            MostrarError("La reunión debe ser en el futuro.");
            return;
        }

        var organizadorId = _existente?.OrganizerId ?? _organizador.Id;
        var organizadorNombre = _existente?.OrganizerName ?? _organizador.DisplayName;
        var respuestaOrganizador = _existente?.Participants
            .FirstOrDefault(p => p.UserId.Equals(organizadorId, StringComparison.OrdinalIgnoreCase))
            ?.Response ?? MeetingResponse.Accepted;

        var participantes = _participantes
            .Select(p => new MeetingParticipant
            {
                UserId = p.UserId,
                DisplayName = p.DisplayName,
                Response = p.Response
            })
            .ToList();

        if (!participantes.Any(p => p.UserId.Equals(organizadorId, StringComparison.OrdinalIgnoreCase)))
        {
            participantes.Insert(0, new MeetingParticipant
            {
                UserId = organizadorId,
                DisplayName = organizadorNombre,
                Response = respuestaOrganizador
            });
        }

        var esStreaming = lugar.Equals("Streaming", StringComparison.OrdinalIgnoreCase);
        var esVirtual = VirtualSwitch.IsOn || esStreaming;

        Resultado = new Meeting
        {
            Id = _existente?.Id ?? Guid.NewGuid().ToString("N"),
            Title = titulo,
            StartLocal = inicio,
            DurationMinutes = _duracionMinutos,
            IsLong = _esLarga,
            IsVirtual = esVirtual,
            VirtualUrl = esVirtual ? (EnlaceBox.Text?.Trim() ?? string.Empty) : string.Empty,
            Location = lugar,
            OrganizerId = organizadorId,
            OrganizerName = organizadorNombre,
            CreatedAt = _existente?.CreatedAt ?? DateTime.UtcNow,
            Participants = participantes,
            Objectives = _objetivos
                .Select(o => new MeetingObjective
                {
                    Id = o.Id,
                    Text = o.Text,
                    IsDone = o.IsDone
                })
                .ToList()
        };
    }

    private void MostrarError(string mensaje)
    {
        ErrorTexto.Text = mensaje;
        ErrorTexto.Visibility = Visibility.Visible;
    }
}
