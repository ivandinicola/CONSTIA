namespace Constia.Application.Usuarios;

public enum EstadoAutenticacion { CredencialesInvalidas, VerificacionRequerida, Autenticado }

public sealed record ResultadoAutenticacion(EstadoAutenticacion Estado, UsuarioAutenticado? Usuario = null);
