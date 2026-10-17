using Constia.Application.Temporal;
using Constia.Infrastructure;
using Constia.Infrastructure.Habitos;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

const string connectionVariable = "CONSTIA_STATE_HISTORY_CONNECTION";
const string usage =
    "Usage: capture --cutover-utc <UTC ISO-8601> [--apply] | backfill [--apply] | status";

if (args.Length == 0)
{
    Console.Error.WriteLine(usage);
    return 2;
}

var command = args[0];
var apply = args.Contains("--apply", StringComparer.Ordinal);
if (command is not ("capture" or "backfill" or "status"))
{
    Console.Error.WriteLine(usage);
    return 2;
}

DateTimeOffset? cutoverUtc = null;
if (command == "capture")
{
    var cutoverIndex = Array.IndexOf(args, "--cutover-utc");
    if (cutoverIndex < 0 ||
        cutoverIndex + 1 >= args.Length ||
        !DateTimeOffset.TryParse(args[cutoverIndex + 1], CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsedCutover) ||
        parsedCutover.Offset != TimeSpan.Zero)
    {
        Console.Error.WriteLine("capture requires --cutover-utc with a UTC ISO-8601 value ending in Z.");
        return 2;
    }

    cutoverUtc = parsedCutover;
}

if (command != "status" && !apply)
{
    Console.WriteLine($"No changes applied. Reviewed command: {command}.");
    Console.WriteLine("Repeat with --apply only during the documented maintenance procedure.");
    return 0;
}

var connectionString = Environment.GetEnvironmentVariable(connectionVariable);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"Set {connectionVariable} to the explicitly selected SQL Server connection.");
    return 2;
}

try
{
    var options = new DbContextOptionsBuilder<ConstiaDbContext>()
        .UseSqlServer(connectionString)
        .Options;
    await using var context = new ConstiaDbContext(options);
    var service = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);

    switch (command)
    {
        case "capture":
            await service.CapturarSnapshotAsync(cutoverUtc!.Value);
            Console.WriteLine("The UTC cutover and immutable source snapshot are captured.");
            break;
        case "backfill":
            var applied = await service.AplicarBackfillAsync();
            Console.WriteLine(applied
                ? "The historical status backfill is verified and marked complete."
                : "The historical status initialization is already complete; no changes were made.");
            break;
        case "status":
            var marker = await context.InicializacionesEstadoHabito
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == InicializacionEstadoHabito.RegistroUnicoId);
            var count = await context.SnapshotsEstadoHabito.CountAsync();
            Console.WriteLine(marker is null
                ? "State-history initialization has not started."
                : $"Cutover UTC: {marker.CutoverUtc:O}; snapshot captured: {marker.SnapshotCapturedAtUtc is not null}; expected legacy habits: {marker.ExpectedLegacyHabitCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; snapshot rows: {count}; completed: {marker.CompletedAtUtc is not null}.");
            break;
    }

    return 0;
}
catch (Exception exception)
{
    // Do not print the connection string or provider diagnostics that may contain server details.
    Console.Error.WriteLine($"State-history operation failed ({exception.GetType().Name}): {exception.Message}");
    return 1;
}
