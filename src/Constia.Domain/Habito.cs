namespace Constia.Domain;

public class Habito
{
    private readonly List<ProgramacionHabito> _programaciones = [];
    private readonly List<PeriodoEstadoHabito> _periodosEstado = [];

    public Guid Id { get; private set; }

    public Usuario Usuario { get; private set; }

    public string Nombre { get; private set; }

    public string? Descripcion { get; private set; }

    public Guid? CategoriaId { get; private set; }

    public string? IconoId { get; private set; }

    public DateTimeOffset FechaCreacion { get; private set; }

    public DateOnly FechaInicio { get; private set; }

    public EstadoHabito Estado { get; private set; }

    public IReadOnlyCollection<ProgramacionHabito> Programaciones => _programaciones.AsReadOnly();

    public IReadOnlyCollection<PeriodoEstadoHabito> PeriodosEstado => _periodosEstado.AsReadOnly();


    public IReadOnlyCollection<DiaProgramado> DiasProgramados => ProgramacionActual.DiasProgramados;

    private ProgramacionHabito ProgramacionActual =>
        _programaciones.SingleOrDefault(programacion => programacion.VigenteHasta is null)
        ?? throw new InvalidOperationException("El hábito debe tener una única programación vigente.");

    private Habito()
    {
        Usuario = null!;
        Nombre = null!;
    }

    public Habito(
        Usuario usuario,
        string nombre,
        string? descripcion,
        DateOnly fechaInicio,
        IEnumerable<DayOfWeek> diasProgramados)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentNullException.ThrowIfNull(diasProgramados);

        Id = Guid.NewGuid();
        Usuario = usuario;
        Nombre = nombre;
        Descripcion = descripcion;
        FechaCreacion = DateTimeOffset.UtcNow;
        FechaInicio = fechaInicio;
        Estado = EstadoHabito.Activo;
        _programaciones.Add(new ProgramacionHabito(fechaInicio, null, diasProgramados));
        var estadoInicial = new PeriodoEstadoHabito(EstadoHabito.Activo, fechaInicio);
        estadoInicial.AsignarHabito(Id);
        _periodosEstado.Add(estadoInicial);
    }

    public EstadoHabito? ObtenerEstadoPara(DateOnly fecha)
    {
        var aplicables = _periodosEstado.Where(periodo => periodo.AplicaA(fecha)).Take(2).ToArray();
        if (aplicables.Length > 1)
        {
            throw new InvalidOperationException("Los períodos de estado se superponen.");
        }

        if (aplicables.Length == 0)
        {
            return null;
        }

        if (!Enum.IsDefined(aplicables[0].Estado))
        {
            throw new InvalidOperationException("El historial de estado contiene un valor desconocido.");
        }

        return aplicables[0].Estado;
    }

    public bool Desactivar(DateOnly fechaEfectiva)
    {
        var abierto = ObtenerPeriodoEstadoAbiertoValidado();
        if (Estado == EstadoHabito.Inactivo)
        {
            if (fechaEfectiva < abierto.VigenteDesde && abierto.VigenteDesde != FechaInicio)
            {
                throw new ConflictoVigenciaEstadoHabitoException(
                    "La fecha efectiva es anterior al inicio del estado inactivo vigente.");
            }

            return false;
        }

        if (abierto.Estado != EstadoHabito.Activo)
        {
            throw new InvalidOperationException("La proyección actual y el período abierto no coinciden.");
        }

        if (fechaEfectiva < abierto.VigenteDesde)
        {
            if (_periodosEstado.Count != 1 || abierto.VigenteDesde != FechaInicio)
            {
                throw new ConflictoVigenciaEstadoHabitoException(
                    "La fecha efectiva es anterior a un período de estado ya vigente.");
            }

            // Antes del inicio del hábito no existe una oportunidad activa. Conservamos un único
            // período inicial, pero hacemos que su estado desde FechaInicio sea el estado final.
            abierto.ReemplazarEstadoAbierto(EstadoHabito.Inactivo);
        }
        else if (fechaEfectiva == abierto.VigenteDesde)
        {
            abierto.ReemplazarEstadoAbierto(EstadoHabito.Inactivo);
        }
        else
        {
            var nuevoPeriodo = new PeriodoEstadoHabito(EstadoHabito.Inactivo, fechaEfectiva);
            nuevoPeriodo.AsignarHabito(Id);
            abierto.CerrarEn(fechaEfectiva);
            _periodosEstado.Add(nuevoPeriodo);
        }

        Estado = EstadoHabito.Inactivo;
        return true;
    }

    public void InicializarHistorialEstadoDesdeCorte(DateOnly fechaInicioHistorial, EstadoHabito estadoCapturado)
    {
        if (_periodosEstado.Count != 0)
        {
            throw new InvalidOperationException("El historial de estado ya contiene períodos.");
        }

        if (fechaInicioHistorial < FechaInicio)
        {
            throw new ArgumentOutOfRangeException(nameof(fechaInicioHistorial));
        }

        if (!Enum.IsDefined(estadoCapturado) || estadoCapturado != Estado)
        {
            throw new InvalidOperationException("El estado capturado no coincide con la proyección actual del hábito.");
        }

        var period = new PeriodoEstadoHabito(estadoCapturado, fechaInicioHistorial);
        period.AsignarHabito(Id);
        _periodosEstado.Add(period);
    }

    public void ActualizarConfiguracion(
        string nombre,
        string? descripcion,
        IEnumerable<DayOfWeek> diasProgramados)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentNullException.ThrowIfNull(diasProgramados);

        var dias = diasProgramados.Distinct().ToArray();
        if (dias.Length == 0)
        {
            throw new ArgumentException("Debe seleccionarse al menos un día programado.", nameof(diasProgramados));
        }

        if (dias.Any(dia => !Enum.IsDefined(dia)))
        {
            throw new ArgumentException("Los días programados deben ser días de la semana válidos.", nameof(diasProgramados));
        }

        var diasActuales = DiasProgramados.Select(dia => dia.Dia).ToHashSet();
        if (!diasActuales.SetEquals(dias))
        {
            throw new ArgumentException(
                "Los días programados no se pueden modificar hasta que esté disponible la edición versionada.",
                nameof(diasProgramados));
        }

        Nombre = nombre;
        Descripcion = descripcion;
    }

    public void ActualizarMetadatos(Guid? categoriaId, string? iconoId)
    {
        if (categoriaId is Guid categoria && !CatalogoCategoriasHabito.EsOficial(categoria))
            throw new ArgumentException("La categoría debe ser oficial.", nameof(categoriaId));
        if (iconoId is not null && !CatalogoIconosHabito.EsOficial(iconoId))
            throw new ArgumentException("El ícono debe ser oficial.", nameof(iconoId));

        if (categoriaId is not null) CategoriaId = categoriaId;
        if (iconoId is not null) IconoId = iconoId;
    }

    public void ActualizarConfiguracionVersionada(
        string nombre,
        string? descripcion,
        IEnumerable<DayOfWeek> diasProgramados,
        DateOnly fechaEfectiva)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        var dias = ProgramacionHabito.NormalizarDias(diasProgramados);
        var programacionAbierta = ObtenerProgramacionAbiertaValidada();

        ProgramacionHabito? programacionAActualizar = null;
        ProgramacionHabito? nuevaProgramacion = null;

        if (!programacionAbierta.TieneDias(dias))
        {
            if (fechaEfectiva < FechaInicio)
            {
                var inicial = _programaciones.Single(programacion => programacion.VigenteDesde == FechaInicio);
                if (inicial.VigenteHasta is not null)
                {
                    throw new ConflictoVigenciaProgramacionException(
                        "La fecha efectiva retrocede antes de una programación histórica ya cerrada.");
                }

                programacionAActualizar = inicial;
            }
            else if (fechaEfectiva < programacionAbierta.VigenteDesde)
            {
                throw new ConflictoVigenciaProgramacionException(
                    "La fecha efectiva es anterior al inicio de la programación vigente.");
            }
            else if (fechaEfectiva == programacionAbierta.VigenteDesde)
            {
                programacionAActualizar = programacionAbierta;
            }
            else
            {
                // Construir la versión nueva antes de cerrar la abierta evita mutaciones parciales
                // ante una entrada inválida.
                nuevaProgramacion = new ProgramacionHabito(fechaEfectiva, null, dias);
            }
        }

        if (programacionAActualizar is not null)
        {
            programacionAActualizar.ReemplazarDias(dias);
        }
        else if (nuevaProgramacion is not null)
        {
            programacionAbierta.CerrarEn(fechaEfectiva);
            _programaciones.Add(nuevaProgramacion);
        }

        Nombre = nombre;
        Descripcion = descripcion;
    }

    public ProgramacionHabito? ObtenerProgramacionPara(DateOnly fecha)
    {
        if (fecha < FechaInicio)
        {
            return null;
        }

        var programacionesAplicables = _programaciones
            .Where(programacion => programacion.AplicaA(fecha))
            .Take(2)
            .ToArray();

        if (programacionesAplicables.Length != 1)
        {
            throw new InvalidOperationException(
                "Desde la fecha de inicio debe existir exactamente una programación aplicable.");
        }

        return programacionesAplicables[0];
    }

    private ProgramacionHabito ObtenerProgramacionAbiertaValidada()
    {
        if (_programaciones.Count == 0)
        {
            throw new InvalidOperationException("El hábito debe tener al menos una programación.");
        }

        var ordenadas = _programaciones.OrderBy(programacion => programacion.VigenteDesde).ToArray();
        if (ordenadas[0].VigenteDesde != FechaInicio)
        {
            throw new InvalidOperationException(
                "La programación inicial debe comenzar en la fecha de inicio del hábito.");
        }

        foreach (var programacion in ordenadas)
        {
            var dias = programacion.DiasProgramados.Select(dia => dia.Dia).ToArray();
            if (dias.Length == 0 || dias.Any(dia => !Enum.IsDefined(dia)) || dias.Distinct().Count() != dias.Length)
            {
                throw new InvalidOperationException(
                    "Cada versión debe contener un conjunto válido y no vacío de días programados.");
            }

            if (programacion.VigenteHasta is DateOnly fin && fin <= programacion.VigenteDesde)
            {
                throw new InvalidOperationException("Una versión contiene un intervalo de vigencia inválido.");
            }
        }

        for (var index = 0; index < ordenadas.Length - 1; index++)
        {
            if (ordenadas[index].VigenteHasta != ordenadas[index + 1].VigenteDesde)
            {
                throw new InvalidOperationException(
                    "Las versiones de programación deben ser continuas y no superponerse.");
            }
        }

        var abiertas = ordenadas.Where(programacion => programacion.VigenteHasta is null).ToArray();
        if (abiertas.Length != 1 || !ReferenceEquals(abiertas[0], ordenadas[^1]))
        {
            throw new InvalidOperationException(
                "El hábito debe tener una única programación abierta al final del historial.");
        }

        return abiertas[0];
    }

    private PeriodoEstadoHabito ObtenerPeriodoEstadoAbiertoValidado()
    {
        if (_periodosEstado.Count == 0)
        {
            throw new InvalidOperationException("El historial de estado del hábito no está inicializado.");
        }

        var ordenados = _periodosEstado.OrderBy(periodo => periodo.VigenteDesde).ToArray();
        for (var index = 0; index < ordenados.Length; index++)
        {
            var periodo = ordenados[index];
            if (!Enum.IsDefined(periodo.Estado) ||
                periodo.VigenteHasta is DateOnly fin && fin <= periodo.VigenteDesde)
            {
                throw new InvalidOperationException("El historial de estado contiene un período inválido.");
            }

            if (index > 0 && ordenados[index - 1].VigenteHasta != periodo.VigenteDesde)
            {
                throw new InvalidOperationException("Los períodos de estado deben ser contiguos y no superponerse.");
            }
        }

        var abiertos = ordenados.Where(periodo => periodo.VigenteHasta is null).ToArray();
        if (abiertos.Length != 1 || !ReferenceEquals(abiertos[0], ordenados[^1]))
        {
            throw new InvalidOperationException("El historial debe tener exactamente un período abierto.");
        }

        return abiertos[0];
    }
}
