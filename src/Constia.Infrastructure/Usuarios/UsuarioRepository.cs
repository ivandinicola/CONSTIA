using Constia.Application.Usuarios;
using Constia.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Constia.Infrastructure.Usuarios;

public sealed class UsuarioRepository(ConstiaDbContext dbContext) : IUsuarioRepository
{
    private const string EmailUniqueIndexName = "IX_Usuario_Email";

    public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken cancellationToken)
    {
        return dbContext.Set<Usuario>()
            .AsNoTracking()
            .SingleOrDefaultAsync(usuario => usuario.Email == email, cancellationToken);
    }

    public Task<bool> ExistePorEmailAsync(string email, CancellationToken cancellationToken)
    {
        return dbContext.Set<Usuario>()
            .AsNoTracking()
            .AnyAsync(usuario => usuario.Email == email, cancellationToken);
    }

    public async Task<bool> IntentarAgregarAsync(Usuario usuario, CancellationToken cancellationToken)
    {
        dbContext.Set<Usuario>().Add(usuario);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (EsDuplicadoDeEmail(exception))
        {
            return false;
        }
    }

    private static bool EsDuplicadoDeEmail(DbUpdateException exception)
    {
        return exception.InnerException is SqlException sqlException
            && sqlException.Errors.Cast<SqlError>().Any(error =>
                error.Number is 2601 or 2627
                && error.Message.Contains(EmailUniqueIndexName, StringComparison.OrdinalIgnoreCase));
    }
}
