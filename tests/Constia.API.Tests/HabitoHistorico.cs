using System.Reflection;
using Constia.Domain;

namespace Constia.API.Tests;

// Solo fixtures de entidades históricas. No representa una operación de alta.
// Invoca el constructor privado de materialización y restaura valores históricos explícitos.
internal static class HabitoHistorico
{
    internal static Habito Crear(Usuario usuario, string nombre, string? descripcion,
        DateOnly fechaInicio, IEnumerable<DayOfWeek> diasProgramados)
    {
        var habit = (Habito)Activator.CreateInstance(typeof(Habito), nonPublic: true)!;
        Set(habit, nameof(Habito.Id), Guid.NewGuid());
        Set(habit, nameof(Habito.Usuario), usuario);
        Set(habit, nameof(Habito.Nombre), nombre);
        Set(habit, nameof(Habito.Descripcion), descripcion);
        Set(habit, nameof(Habito.FechaCreacion), new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        Set(habit, nameof(Habito.FechaInicio), fechaInicio);
        Set(habit, nameof(Habito.Estado), EstadoHabito.Activo);
        var schedules = (List<ProgramacionHabito>)typeof(Habito)
            .GetField("_programaciones", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(habit)!;
        schedules.Add(new ProgramacionHabito(fechaInicio, null, diasProgramados));
        var periods = (List<PeriodoEstadoHabito>)typeof(Habito)
            .GetField("_periodosEstado", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(habit)!;
        var period = new PeriodoEstadoHabito(EstadoHabito.Activo, fechaInicio);
        typeof(PeriodoEstadoHabito).GetProperty(nameof(PeriodoEstadoHabito.HabitoId))!.SetValue(period, habit.Id);
        periods.Add(period);
        return habit;
    }

    private static void Set(Habito habit, string property, object? value) =>
        typeof(Habito).GetProperty(property)!.SetValue(habit, value);
}
