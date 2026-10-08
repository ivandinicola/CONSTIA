namespace Constia.Application.Temporal;

public sealed class ZonaHorariaPersistidaInvalidaException(string timeZoneId, Exception? innerException = null)
    : InvalidOperationException(
        $"The persisted time zone identifier '{timeZoneId}' is not valid or cannot be resolved by this runtime.",
        innerException);
