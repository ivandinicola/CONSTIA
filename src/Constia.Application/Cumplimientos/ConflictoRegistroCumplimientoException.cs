namespace Constia.Application.Cumplimientos;

public sealed class ConflictoRegistroCumplimientoException()
    : InvalidOperationException("The habit cannot accept this completion.");
