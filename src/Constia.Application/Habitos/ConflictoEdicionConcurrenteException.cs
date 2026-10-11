namespace Constia.Application.Habitos;

/// <summary>
/// Represents a persistence conflict while updating a habit. Infrastructure can translate
/// provider-specific concurrency failures to this application-level exception.
/// </summary>
public sealed class ConflictoEdicionConcurrenteException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
