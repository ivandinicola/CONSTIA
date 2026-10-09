namespace Constia.Domain;

public class ProgramacionHabito
{
    private readonly List<DiaProgramado> _diasProgramados = [];

    public DateOnly VigenteDesde { get; private set; }

    public DateOnly? VigenteHasta { get; private set; }

    public IReadOnlyCollection<DiaProgramado> DiasProgramados => _diasProgramados.AsReadOnly();

    private ProgramacionHabito()
    {
    }

    public ProgramacionHabito(
        DateOnly vigenteDesde,
        DateOnly? vigenteHasta,
        IEnumerable<DayOfWeek> diasProgramados)
    {
        ArgumentNullException.ThrowIfNull(diasProgramados);

        if (vigenteHasta is DateOnly fechaFin && fechaFin <= vigenteDesde)
        {
            throw new ArgumentException(
                "La fecha final de vigencia debe ser posterior a la fecha inicial.",
                nameof(vigenteHasta));
        }

        var dias = diasProgramados.Distinct().ToArray();
        if (dias.Length == 0)
        {
            throw new ArgumentException("Debe seleccionarse al menos un día programado.", nameof(diasProgramados));
        }

        if (dias.Any(dia => !Enum.IsDefined(dia)))
        {
            throw new ArgumentException("Los días programados deben ser días de la semana válidos.", nameof(diasProgramados));
        }

        VigenteDesde = vigenteDesde;
        VigenteHasta = vigenteHasta;
        _diasProgramados.AddRange(dias.Select(dia => new DiaProgramado(dia)));
    }

    public bool AplicaA(DateOnly fecha) =>
        fecha >= VigenteDesde && (VigenteHasta is null || fecha < VigenteHasta.Value);
}
