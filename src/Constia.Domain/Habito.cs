namespace Constia.Domain;

public class Habito
{
    public Guid Id { get; private set; }

    public Usuario Usuario { get; private set; }

    public string Nombre { get; private set; }

    public string? Descripcion { get; private set; }

    public DateTimeOffset FechaCreacion { get; private set; }

    public DateOnly FechaInicio { get; private set; }

    public EstadoHabito Estado { get; private set; }

    public IReadOnlyList<DayOfWeek> DiasProgramados { get; private set; }

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

        var dias = diasProgramados.Distinct().ToArray();
        if (dias.Length == 0)
        {
            throw new ArgumentException("Debe seleccionarse al menos un día programado.", nameof(diasProgramados));
        }

        if (dias.Any(dia => !Enum.IsDefined(dia)))
        {
            throw new ArgumentException("Los días programados deben ser días de la semana válidos.", nameof(diasProgramados));
        }

        Id = Guid.NewGuid();
        Usuario = usuario;
        Nombre = nombre;
        Descripcion = descripcion;
        FechaCreacion = DateTimeOffset.UtcNow;
        FechaInicio = fechaInicio;
        Estado = EstadoHabito.Activo;
        DiasProgramados = Array.AsReadOnly(dias);
    }

    public void Desactivar()
    {
        Estado = EstadoHabito.Inactivo;
    }
}
