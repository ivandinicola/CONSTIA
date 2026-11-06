namespace Constia.Application.Habitos;

public sealed class ConsultarHabitosDiarios(
    IHabitosDiariosReadRepository habitos,
    IEstadoHabitoReadinessRepository readiness)
{
    public async Task<IReadOnlyList<HabitoDiario>> EjecutarAsync(
        Guid usuarioId, DateOnly fecha, CancellationToken cancellationToken)
    {
        if (!await readiness.HistorialCompletoAsync(cancellationToken))
        {
            throw new HistorialEstadoHabitoNoInicializadoException();
        }

        var candidatos = await habitos.ListarCandidatosAsync(usuarioId, fecha, cancellationToken);
        var activos = candidatos.Where(habito => OportunidadHabito.AplicaA(habito, fecha)).ToArray();

        if (activos.Length == 0)
        {
            return [];
        }

        var cumplidos = await habitos.ListarCumplidosAsync(usuarioId, fecha, fecha, cancellationToken);
        return activos.Select(habito => new HabitoDiario(
            habito.Id, habito.Nombre, habito.Descripcion, habito.CategoriaId,
            habito.IconoId, cumplidos.Contains((habito.Id, fecha)))).ToArray();
    }
}

public sealed record HabitoDiario(
    Guid Id, string Nombre, string? Descripcion, Guid? CategoriaId, string? IconoId, bool Cumplido);
