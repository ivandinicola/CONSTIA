namespace Constia.Application.Cumplimientos;

public sealed class EstadoHabitoHistoricoDesconocidoException()
    : InvalidOperationException("No se conoce el estado histórico del hábito para esa fecha.");
