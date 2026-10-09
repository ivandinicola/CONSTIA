namespace Constia.Domain;

public class Habito
{
    private readonly List<ProgramacionHabito> _programaciones = [];

    public Guid Id { get; private set; }

    public Usuario Usuario { get; private set; }

    public string Nombre { get; private set; }

    public string? Descripcion { get; private set; }

    public DateTimeOffset FechaCreacion { get; private set; }

    public DateOnly FechaInicio { get; private set; }

    public EstadoHabito Estado { get; private set; }

    public IReadOnlyCollection<ProgramacionHabito> Programaciones => _programaciones.AsReadOnly();

    public IReadOnlyCollection<DiaProgramado> DiasProgramados => ProgramacionActual.DiasProgramados;

    private ProgramacionHabito ProgramacionActual =>
        _programaciones.SingleOrDefault(programacion => programacion.VigenteHasta is null)
        ?? throw new InvalidOperationException("El hábito debe tener una única programación vigente.");

    private Habito()
    {
        Usuario = null!;
        Nombre = null!;
    }

    public Habito(
        Usuario usuario,
        string nombre,
        string? descripcion,
        DateOnly fechaInicio,
        IEnumerable<DayOfWeek> diasProgramados)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentNullException.ThrowIfNull(diasProgramados);

        Id = Guid.NewGuid();
        Usuario = usuario;
        Nombre = nombre;
        Descripcion = descripcion;
        FechaCreacion = DateTimeOffset.UtcNow;
        FechaInicio = fechaInicio;
        Estado = EstadoHabito.Activo;
        _programaciones.Add(new ProgramacionHabito(fechaInicio, null, diasProgramados));
    }

    public void Desactivar()
    {
        Estado = EstadoHabito.Inactivo;
    }

    public void ActualizarConfiguracion(
        string nombre,
        string? descripcion,
        IEnumerable<DayOfWeek> diasProgramados)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentNullException.ThrowIfNull(diasProgramados);

        var dias = diasProgramados.Distinct().ToArray();
        if (dias.Length == 0)
        {
            throw new ArgumentException("Debe seleccionarse al menos un día programado.", nameof(diasProgramados));
        }

        if (dias.Any(dia => !Enum.IsDefined(dia)))
        {
            throw new ArgumentException("Los días programados deben ser días de la semana válidos.", nameof(diasProgramados));
        }

        var diasActuales = DiasProgramados.Select(dia => dia.Dia).ToHashSet();
        if (!diasActuales.SetEquals(dias))
        {
            throw new ArgumentException(
                "Los días programados no se pueden modificar hasta que esté disponible la edición versionada.",
                nameof(diasProgramados));
        }

        Nombre = nombre;
        Descripcion = descripcion;
    }

    public ProgramacionHabito? ObtenerProgramacionPara(DateOnly fecha)
    {
        if (fecha < FechaInicio)
        {
            return null;
        }

        var programacionesAplicables = _programaciones
            .Where(programacion => programacion.AplicaA(fecha))
            .Take(2)
            .ToArray();

        if (programacionesAplicables.Length != 1)
        {
            throw new InvalidOperationException(
                "Desde la fecha de inicio debe existir exactamente una programación aplicable.");
        }

        return programacionesAplicables[0];
    }
}
