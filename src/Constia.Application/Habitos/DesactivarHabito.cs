namespace Constia.Application.Habitos;

public sealed class DesactivarHabito(IHabitoRepository habitos)
{
    public async Task<bool> EjecutarAsync(
        Guid habitId,
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        var habito = await habitos.BuscarParaModificarPorIdYUsuarioAsync(
            habitId,
            usuarioId,
            cancellationToken);
        if (habito is null)
        {
            return false;
        }

        habito.Desactivar();
        await habitos.GuardarCambiosAsync(habito, cancellationToken);
        return true;
    }
}
