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
        if (vigenteHasta is DateOnly fechaFin && fechaFin <= vigenteDesde)
        {
            throw new ArgumentException(
                "La fecha final de vigencia debe ser posterior a la fecha inicial.",
                nameof(vigenteHasta));
        }

        var dias = NormalizarDias(diasProgramados);
        VigenteDesde = vigenteDesde;
        VigenteHasta = vigenteHasta;
        _diasProgramados.AddRange(dias.Select(dia => new DiaProgramado(dia)));
    }

    public bool AplicaA(DateOnly fecha) =>
        fecha >= VigenteDesde && (VigenteHasta is null || fecha < VigenteHasta.Value);

    internal static DayOfWeek[] NormalizarDias(IEnumerable<DayOfWeek> diasProgramados)
    {
        ArgumentNullException.ThrowIfNull(diasProgramados);

        var dias = diasProgramados.Distinct().OrderBy(dia => dia).ToArray();
        if (dias.Length == 0)
        {
            throw new ArgumentException("Debe seleccionarse al menos un día programado.", nameof(diasProgramados));
        }

        if (dias.Any(dia => !Enum.IsDefined(dia)))
        {
            throw new ArgumentException("Los días programados deben ser días de la semana válidos.", nameof(diasProgramados));
        }

        return dias;
    }

    internal bool TieneDias(IEnumerable<DayOfWeek> diasProgramados)
    {
        var dias = NormalizarDias(diasProgramados);
        return _diasProgramados.Select(dia => dia.Dia).ToHashSet().SetEquals(dias);
    }

    internal void ReemplazarDias(IEnumerable<DayOfWeek> diasProgramados)
    {
        if (VigenteHasta is not null)
        {
            throw new InvalidOperationException("No se puede modificar una programación histórica cerrada.");
        }

        var dias = NormalizarDias(diasProgramados);
        if (_diasProgramados.Select(dia => dia.Dia).ToHashSet().SetEquals(dias))
        {
            return;
        }

        var diasDeseados = dias.ToHashSet();
        _diasProgramados.RemoveAll(dia => !diasDeseados.Contains(dia.Dia));

        var diasExistentes = _diasProgramados.Select(dia => dia.Dia).ToHashSet();
        _diasProgramados.AddRange(
            dias.Where(dia => !diasExistentes.Contains(dia))
                .Select(dia => new DiaProgramado(dia)));
    }

    internal void CerrarEn(DateOnly vigenteHasta)
    {
        if (VigenteHasta is not null)
        {
            throw new InvalidOperationException("La programación ya está cerrada.");
        }

        if (vigenteHasta <= VigenteDesde)
        {
            throw new ArgumentException(
                "La fecha final de vigencia debe ser posterior a la fecha inicial.",
                nameof(vigenteHasta));
        }

        VigenteHasta = vigenteHasta;
    }
}

public sealed class ConflictoVigenciaProgramacionException(string message) : InvalidOperationException(message);
