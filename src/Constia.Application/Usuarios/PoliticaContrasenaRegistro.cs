using System.Buffers;
using System.Text;

namespace Constia.Application.Usuarios;

/// <summary>
/// Política exclusiva de nuevas altas: entre 8 y 128 valores escalares Unicode.
/// Mayúscula: UppercaseLetter; número: DecimalDigitNumber; especial: Punctuation o Symbol.
/// Las marcas combinantes cuentan individualmente. No normaliza, recorta ni transforma.
/// UTF-16 malformado se rechaza: nunca se sustituye por el carácter de reemplazo.
/// No aplicar a Login ni usar para reevaluar hashes o contraseñas históricas.
/// </summary>
public static class PoliticaContrasenaRegistro
{
    public static void Validar(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var longitud = 0;
        var tieneMayuscula = false;
        var tieneDigito = false;
        var tieneEspecial = false;
        var caracteres = password.AsSpan();

        while (!caracteres.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(caracteres, out var rune, out var consumidos) != OperationStatus.Done)
            {
                throw new ContrasenaInvalidaException("Debe contener una secuencia Unicode válida.");
            }

            longitud++;
            if (longitud > 128)
            {
                throw new ContrasenaInvalidaException("Debe tener entre 8 y 128 caracteres.");
            }

            tieneMayuscula |= Rune.IsUpper(rune);
            tieneDigito |= Rune.IsDigit(rune);
            tieneEspecial |= Rune.IsPunctuation(rune) || Rune.IsSymbol(rune);
            caracteres = caracteres[consumidos..];
        }

        List<string> errores = [];
        if (longitud < 8)
        {
            errores.Add("Debe tener entre 8 y 128 caracteres.");
        }
        if (!tieneMayuscula)
        {
            errores.Add("Debe incluir al menos una letra mayúscula.");
        }
        if (!tieneDigito)
        {
            errores.Add("Debe incluir al menos un número decimal.");
        }
        if (!tieneEspecial)
        {
            errores.Add("Debe incluir al menos un carácter de puntuación o símbolo; los espacios no cuentan.");
        }
        if (errores.Count > 0)
        {
            throw new ContrasenaInvalidaException(errores.ToArray());
        }
    }
}
