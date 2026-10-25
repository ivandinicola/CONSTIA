-- Q00.1
SELECT
    TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS AhoraUtc,
    CASE WHEN DB_NAME() = @BaseEsperada THEN 1 ELSE 0 END AS EsBaseEsperada,
    TRY_CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) AS MajorVersion;

-- Q01.1
SELECT Estado, COUNT_BIG(*) AS Cantidad
FROM dbo.EventoBienvenida
GROUP BY Estado
ORDER BY Estado;

-- Q02.1
SELECT
    COUNT_BIG(*) AS EsperandoReintento,
    COUNT_BIG(CASE WHEN ProximaEjecucionEnUtc <= @AhoraUtc THEN 1 END) AS FechaProgramadaAlcanzada,
    MIN(ProximaEjecucionEnUtc) AS PrimeraFechaProgramada
FROM dbo.EventoBienvenida
WHERE Estado = 4;

-- Q03.1
SELECT Estado, COUNT_BIG(*) AS Cantidad, MIN(LeaseHastaUtc) AS PrimerLeaseQueVence
FROM dbo.EventoBienvenida
WHERE Estado IN (2, 3)
  AND LeaseId IS NOT NULL
  AND LeaseHastaUtc > @AhoraUtc
GROUP BY Estado
ORDER BY Estado;

-- Q04.1
SELECT Estado, COUNT_BIG(*) AS Cantidad, MIN(LeaseHastaUtc) AS LeaseVencidoMasAntiguo
FROM dbo.EventoBienvenida
WHERE Estado IN (2, 3)
  AND LeaseId IS NOT NULL
  AND LeaseHastaUtc <= @AhoraUtc
GROUP BY Estado
ORDER BY Estado;

-- Q05.1
SELECT
    COUNT_BIG(*) AS EventosConLimiteProximo,
    MIN(LimiteUtc) AS PrimerLimiteUtc
FROM (
    SELECT
        CASE WHEN HorizonteHttpEnUtc IS NOT NULL AND HorizonteHttpEnUtc < ExpiraEnUtc
             THEN HorizonteHttpEnUtc ELSE ExpiraEnUtc END AS LimiteUtc
    FROM dbo.EventoBienvenida
    WHERE Estado BETWEEN 1 AND 5
) AS Limites
WHERE LimiteUtc > @AhoraUtc
  AND LimiteUtc <= DATEADD(minute, @VentanaMinutos, @AhoraUtc);

-- Q06.1
SELECT COUNT_BIG(*) AS AceptadosPorProveedor
FROM dbo.EventoBienvenida
WHERE Estado = 6;

-- Q07.1
SELECT MotivoFinal, COUNT_BIG(*) AS Cantidad
FROM dbo.EventoBienvenida
WHERE Estado = 7
GROUP BY MotivoFinal
ORDER BY MotivoFinal;

-- Q08.1
SELECT COUNT_BIG(*) AS FinalizadosInciertos
FROM dbo.EventoBienvenida
WHERE Estado = 8;

-- Q08.2
SELECT Estado, COUNT_BIG(*) AS ConAntecedenteDeIncertidumbre
FROM dbo.EventoBienvenida
WHERE HuboIncertidumbre = 1
GROUP BY Estado
ORDER BY Estado;

-- Q09.1
SELECT COUNT_BIG(*) AS ReservasUltimas24Horas
FROM dbo.IntentoBienvenida
WHERE ReservadoEnUtc > DATEADD(hour, -24, @AhoraUtc);

-- Q10.1
WITH Consumo AS (
    SELECT COUNT_BIG(*) AS Cantidad
    FROM dbo.IntentoBienvenida
    WHERE ReservadoEnUtc > DATEADD(hour, -24, @AhoraUtc)
),
UltimasVeinte AS (
    SELECT TOP (20) ReservadoEnUtc
    FROM dbo.IntentoBienvenida
    WHERE ReservadoEnUtc > DATEADD(hour, -24, @AhoraUtc)
    ORDER BY ReservadoEnUtc DESC, Id DESC
)
SELECT
    c.Cantidad AS ReservasConsumidas,
    CASE WHEN c.Cantidad < 20 THEN CONVERT(bigint, 20) - c.Cantidad ELSE CONVERT(bigint, 0) END AS PresupuestoRestante,
    CASE WHEN c.Cantidad >= 20
         THEN DATEADD(hour, 24, (SELECT MIN(ReservadoEnUtc) FROM UltimasVeinte))
         ELSE NULL END AS ProximaCapacidadTeoricaUtc
FROM Consumo AS c;

-- Q11.1
SELECT
    Id, VersionEsquema, ProtocoloEscritor,
    CapturaHabilitada, EnvioHabilitado, PausaProveedor, MotivoPausa,
    EpocaCaptura, InicioCapturaEnUtc, FinCapturaEnUtc,
    CASE WHEN @EpocaEsperada IS NOT NULL AND EpocaCaptura = @EpocaEsperada THEN 1 ELSE 0 END AS CoincideEpocaEsperada
FROM dbo.ControlBienvenida
WHERE Id = 1;

-- Q12.1
SELECT
    OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') AS HistorialObjeto,
    OBJECT_ID(N'dbo.ControlBienvenida', N'U') AS ControlObjeto,
    OBJECT_ID(N'dbo.EventoBienvenida', N'U') AS EventoObjeto,
    OBJECT_ID(N'dbo.IntentoBienvenida', N'U') AS IntentoObjeto;

-- Q12.2
SELECT MigrationId, ProductVersion
FROM dbo.__EFMigrationsHistory
WHERE MigrationId = N'20261023003711_AddWelcomeEmailOutbox';

-- Q12.3
SELECT
    t.name AS Tabla, c.column_id AS Orden,
    c.name AS Columna, ty.name AS Tipo,
    c.max_length AS LongitudBytes, c.precision, c.scale, c.is_nullable, c.is_identity
FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
JOIN sys.columns AS c ON c.object_id = t.object_id
JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
WHERE s.name = N'dbo'
  AND t.name IN (N'ControlBienvenida', N'EventoBienvenida', N'IntentoBienvenida')
ORDER BY t.name, c.column_id;

-- Q12.4
SELECT t.name AS Tabla, cc.name, cc.is_disabled, cc.is_not_trusted, cc.definition
FROM sys.check_constraints AS cc
JOIN sys.tables AS t ON t.object_id = cc.parent_object_id
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
WHERE s.name = N'dbo'
  AND t.name IN (N'ControlBienvenida', N'EventoBienvenida', N'IntentoBienvenida')
ORDER BY t.name, cc.name;

-- Q12.5
SELECT t.name AS Tabla, i.name AS Indice, i.is_unique, i.is_primary_key,
       i.is_disabled, i.has_filter, i.filter_definition,
       ic.key_ordinal, ic.is_included_column, c.name AS Columna, ic.is_descending_key
FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
JOIN sys.indexes AS i ON i.object_id = t.object_id
JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE s.name = N'dbo'
  AND t.name IN (N'ControlBienvenida', N'EventoBienvenida', N'IntentoBienvenida')
ORDER BY t.name, i.name, ic.key_ordinal, ic.index_column_id;

-- Q12.6
SELECT
    fk.name, OBJECT_SCHEMA_NAME(fk.parent_object_id) AS EsquemaOrigen,
    OBJECT_NAME(fk.parent_object_id) AS TablaOrigen, pc.name AS ColumnaOrigen,
    OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS EsquemaDestino,
    OBJECT_NAME(fk.referenced_object_id) AS TablaDestino, rc.name AS ColumnaDestino,
    fk.is_disabled, fk.is_not_trusted, fk.delete_referential_action_desc
FROM sys.foreign_keys AS fk
JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns AS pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.columns AS rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
WHERE fk.parent_object_id IN (OBJECT_ID(N'dbo.EventoBienvenida'), OBJECT_ID(N'dbo.IntentoBienvenida'))
ORDER BY fk.name, fkc.constraint_column_id;

-- Q12.7
SELECT
    CASE WHEN e.EpocaCaptura = c.EpocaCaptura THEN N'Vigente' ELSE N'OtraEpoca' END AS GrupoEpoca,
    e.Estado, COUNT_BIG(*) AS Cantidad
FROM dbo.EventoBienvenida AS e
CROSS JOIN dbo.ControlBienvenida AS c
WHERE c.Id = 1
GROUP BY CASE WHEN e.EpocaCaptura = c.EpocaCaptura THEN N'Vigente' ELSE N'OtraEpoca' END, e.Estado;

-- Q13.1
WITH Candidatos AS (
    SELECT TOP (@Lote)
        e.Id, e.Estado, e.DatosDepuradosEnUtc, e.FinalizadoEnUtc,
        CASE WHEN e.Estado BETWEEN 1 AND 5 AND
          ((e.Estado IN (2,3) AND e.LeaseHastaUtc <= @AhoraUtc) OR
           (e.LeaseId IS NULL AND
             (e.ExpiraEnUtc <= DATEADD(second,10,@AhoraUtc) OR e.Reservas >= 3 OR
              e.HorizonteHttpEnUtc <= DATEADD(second,10,@AhoraUtc))))
          THEN 1 ELSE 0 END AS RequiereRecuperacionOVencimiento
    FROM dbo.EventoBienvenida AS e
    WHERE
      (e.Estado BETWEEN 1 AND 5 AND
        ((e.Estado IN (2,3) AND e.LeaseHastaUtc <= @AhoraUtc) OR
         (e.LeaseId IS NULL AND
           (e.ExpiraEnUtc <= DATEADD(second,10,@AhoraUtc) OR e.Reservas >= 3 OR
            e.HorizonteHttpEnUtc <= DATEADD(second,10,@AhoraUtc)))))
      OR
      (e.Estado = 1 AND e.Reservas = 0 AND e.LeaseId IS NULL AND e.ExpiraEnUtc <= @AhoraUtc
       AND NOT EXISTS (SELECT 1 FROM dbo.IntentoBienvenida AS i WHERE i.EventoId = e.Id))
      OR
      (e.Estado BETWEEN 6 AND 8 AND e.LeaseId IS NULL
       AND (e.UltimoLeaseHastaUtc IS NULL OR DATEADD(minute,10,e.UltimoLeaseHastaUtc) <= @AhoraUtc)
       AND (e.DatosDepuradosEnUtc IS NULL OR
         (DATEADD(day,30,e.FinalizadoEnUtc) <= @AhoraUtc AND
          (e.IdProveedor IS NOT NULL OR EXISTS
            (SELECT 1 FROM dbo.IntentoBienvenida AS i
             WHERE i.EventoId = e.Id AND DATEADD(hour,24,i.ReservadoEnUtc) <= @AhoraUtc)))))
    ORDER BY e.CapturadoEnUtc, e.Id
)
SELECT
    COUNT_BIG(*) AS EventosObservados,
    COALESCE(SUM(CONVERT(bigint, RequiereRecuperacionOVencimiento)),0) AS RecuperacionOVencimientoObservados,
    COUNT_BIG(CASE WHEN Estado BETWEEN 6 AND 8 AND DatosDepuradosEnUtc IS NULL THEN 1 END) AS FinalesPorDepurarObservados,
    COUNT_BIG(CASE WHEN Estado BETWEEN 6 AND 8 AND DATEADD(day,30,FinalizadoEnUtc) <= @AhoraUtc THEN 1 END) AS FinalesConDetalleElegibleObservados,
    CASE WHEN COUNT_BIG(*) = @Lote THEN 1 ELSE 0 END AS LimiteAlcanzado
FROM Candidatos;

-- Q14.1
SELECT Estado, COUNT_BIG(*) AS Cantidad
FROM dbo.EventoBienvenida
WHERE Estado BETWEEN 1 AND 5
  AND PayloadUtf8 IS NOT NULL
  AND (PerfilProveedorVersion IS NULL OR PerfilProveedorVersion <> @HuellaPerfil)
GROUP BY Estado
ORDER BY Estado;
