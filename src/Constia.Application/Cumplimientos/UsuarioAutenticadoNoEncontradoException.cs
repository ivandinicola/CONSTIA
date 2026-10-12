namespace Constia.Application.Cumplimientos;

public sealed class UsuarioAutenticadoNoEncontradoException()
    : InvalidOperationException("The authenticated user no longer exists.");
