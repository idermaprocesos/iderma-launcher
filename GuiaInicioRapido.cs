namespace Iderma_Launcher;

public enum PaginaGuia
{
    Inicio,
    Configuracion
}

public enum LugarGuia
{
    Abajo,
    Arriba,
    Automatico
}

public sealed record PasoGuia(
    string Titulo,
    string Texto,
    PaginaGuia Pagina,
    string Nombre,
    LugarGuia Lugar = LugarGuia.Automatico,
    bool Opcional = false);

public static class GuiaInicioRapido
{
    public static IReadOnlyList<PasoGuia> Pasos { get; } =
    [
        new(
            "Guía de inicio rápido",
            "Te vamos a mostrar el inicio y cada bloque de esta configuración. Pulsa Siguiente para avanzar o Salir para cortar la guía cuando quieras.",
            PaginaGuia.Configuracion,
            "TarjetaGuia"),
        new(
            "Inicio",
            "Desde aquí abres las aplicaciones de tu cuenta. Arriba están tu nombre y tu foto.",
            PaginaGuia.Inicio,
            "GreetingText",
            LugarGuia.Abajo),
        new(
            "Aplicaciones",
            "Cada tarjeta es un programa. Pulsa una para abrirla. Si aún no está en este equipo, el launcher la descarga antes. Un candado indica que no tienes permiso.",
            PaginaGuia.Inicio,
            "AppsScroll",
            LugarGuia.Arriba),
        new(
            "Vista",
            "Cambia cómo se ven las aplicaciones: cuadrícula, lista o compacta.",
            PaginaGuia.Inicio,
            "ViewButton",
            LugarGuia.Abajo),
        new(
            "Descargar todo",
            "Instala de una vez todas las aplicaciones de tu cuenta.",
            PaginaGuia.Inicio,
            "DownloadAllButton",
            LugarGuia.Abajo),
        new(
            "Actualizaciones",
            "Busca versiones nuevas. Si hay alguna, puedes instalarla desde aquí.",
            PaginaGuia.Inicio,
            "UpdatesButton",
            LugarGuia.Abajo,
            Opcional: true),
        new(
            "Reuniones",
            "Aquí aparecen las próximas reuniones del equipo. Programar crea una nueva y Historial muestra las anteriores. Un clic abre el detalle; el clic derecho sirve para indicar si participas.",
            PaginaGuia.Inicio,
            "MeetingsHost",
            LugarGuia.Arriba),
        new(
            "Cerrar sesión",
            "Termina la sesión en este equipo. Conviene usarlo al dejar de trabajar.",
            PaginaGuia.Inicio,
            "LogoutButton",
            LugarGuia.Abajo),
        new(
            "Configuración",
            "El engranaje abre tu cuenta, la comprobación de la base de datos y la protección de la sesión. Siguiente recorre cada bloque.",
            PaginaGuia.Inicio,
            "SettingsButton",
            LugarGuia.Abajo),
        new(
            "Cuenta",
            "Tu nombre, usuario y la hora de esta sesión. Pulsa la foto para ampliarla, o cámbiala con el botón de al lado.",
            PaginaGuia.Configuracion,
            "TarjetaCuenta"),
        new(
            "Troubleshooting",
            "Comprueba si este equipo entra a la base de datos. Si falla, verás el error y qué hacer para corregirlo.",
            PaginaGuia.Configuracion,
            "TarjetaTroubleshooting"),
        new(
            "Flujo de trabajo",
            "Este esquema resume cómo se relacionan las áreas. Pulsa la imagen para verla en grande.",
            PaginaGuia.Configuracion,
            "TarjetaFlujo"),
        new(
            "Protección de la sesión",
            "Puedes bloquear la sesión tras unos minutos sin uso y tras varios intentos fallidos. Desactivar una protección pide tu contraseña.",
            PaginaGuia.Configuracion,
            "TarjetaProteccion"),
        new(
            "Registro de seguridad",
            "Aquí queda lo que ocurre con la sesión: inicios, bloqueos y cambios de protección. No se guardan contraseñas.",
            PaginaGuia.Configuracion,
            "TarjetaRegistro"),
        new(
            "Inicios de sesión",
            "Cada acceso guarda un código interno, la hora y el equipo. Si tu perfil lo permite, puedes exportar el registro.",
            PaginaGuia.Configuracion,
            "TarjetaInicios"),
        new(
            "Listo",
            "Ya viste el inicio y la configuración. Puedes repetir esta guía cuando quieras desde esta misma tarjeta.",
            PaginaGuia.Configuracion,
            "TarjetaGuia")
    ];
}
