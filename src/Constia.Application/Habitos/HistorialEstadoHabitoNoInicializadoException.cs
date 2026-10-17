namespace Constia.Application.Habitos;

public sealed class HistorialEstadoHabitoNoInicializadoException()
    : InvalidOperationException("El historial de estados de hábitos todavía no está inicializado.");
