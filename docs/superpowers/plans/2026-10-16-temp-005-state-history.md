# TEMP-005 State History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist historical Active/Inactive periods, make completion registration honor the status for its civil date, and initialize legacy habits safely.

**Architecture:** Add `PeriodoEstadoHabito` to the Habito aggregate with half-open periods and preserve `Habito.Estado` as its current projection. Share the existing root `ConcurrencyVersion` across status and schedule saves. TEMP-005 metadata stores one immutable UTC cutoff. Before any backfill, a controlled helper captures a durable per-legacy-habit snapshot of the owner IANA zone, owner-local cutoff date, FechaInicio, and persisted Estado in a serializable transaction that locks the source rows. Every retry derives periods only from this committed snapshot, never from current Estado/TimeZoneId. A captured/completed marker gates state-dependent writes.

**Tech Stack:** .NET 10, EF Core 10, SQL Server 2022, xUnit, ASP.NET Core controllers, existing IANA timezone service and `TimeProvider`.

**Spec:** User-approved TEMP-005 instructions: `C:\Users\FIO PC\.codex\attachments\a2682045-ed8d-4c6b-a9ea-c2a48c45881b\Texto pegado.txt`; functional sources: `SPECIFICATION.md`, `MVP-SCOPE.md`.

## Global Constraints
- Base: `feature/TEMP-005` from `main` at `db09a4830994ccbdf5532dc6bec188a76ac42aa2`.
- Preserve `feature/TEMP-004`, its untracked files, and all existing worktrees.
- No reactivation, metrics, streaks, or Blazor; no existing completion/schedule row changes.
- Do not run migrations/backfill against persistent databases; SQL tests use disposable CI SQL Server.
- No staging, commit, push, PR, merge, Trello, or Penpot.
- CI must reject missing, skipped, failed, or unexpected TEMP-005 SQL tests.
- Backfill cutover: stop every application instance before applying the migration and keep all old instances stopped until the initial source snapshot is committed; the new app can then resume independent writes behind readiness gates. Persist the cutover UTC instant once; on retry retain it. The snapshot transaction locks Usuario/Habito inputs and atomically stores every legacy habit input. If snapshot capture aborts, no snapshot rows/captured marker commit; application remains in maintenance. After capture, retries consume only immutable snapshot rows, even if current user timezone or habit status changes.
- Initialization barrier: before snapshot capture, block writes that change the snapshot population or inputs (create habit, change owner TimeZoneId, deactivate/status change); completion registration is blocked until initialization is complete because its validity depends on history. Keep unrelated operations that do not affect snapshot inputs/history available when their consistency permits. After snapshot capture, owner timezone changes, metadata/schedule edits, and new-habit creation can proceed: legacy cutoff/state are frozen and new habits create their own initial period. Deactivation and completion registration stay blocked until backfill verification atomically sets CompletedAt. Deployment order: stop old app instances → apply schema migration → persist the explicit UTC cutoff and capture the snapshot transaction → verify immutable snapshot and backfill to completion → verify counts/invariants → deploy/enable current app writes. On failure, leave state-dependent writes disabled and rerun helper against the same cutoff/snapshot.

## Review Focus
1. Pre-history dates remain explicitly unknown (assert no Active/Inactive inference).
2. A timezone change after snapshot cannot alter the local cutoff used on retry (assert stored snapshot is reused).
3. Same-day deactivation creates no empty interval and preserves an existing completion.
4. Concurrent schedule and state edits yield one root-token winner and no partial dependent writes.
5. A partial backfill remains unavailable and resumes without duplicates (assert readiness only after verification).

---

### Task 1: Domain period model

**Files:** Create `src/Constia.Domain/PeriodoEstadoHabito.cs`; modify `src/Constia.Domain/Habito.cs`; test `tests/Constia.Tests/HabitoTests.cs`, `PeriodoEstadoHabitoTests.cs`, and `DesactivarHabitoTests.cs`.

**Interfaces:** `Habito.ObtenerEstadoPara(DateOnly)` returns `EstadoHabito?` (null = unknown). `Habito.Desactivar(DateOnly fechaEfectiva)` updates open period and current projection atomically.

- [x] Add tests for initial Active state, half-open boundaries, unknown pre-history, before/exact/after FechaInicio, repeated/same-day deactivation, backwards timezone date, projection consistency, immutable closed periods, and no empty/overlapping intervals.
- [x] Run focused domain tests and verify the model/temporal assertions expose missing behavior.
- [x] Implement the aggregate/period rules, validating before mutation.
- [x] Run domain tests and the unit project (131 passed at final local verification).

### Task 2: EF model and migration

**Files:** modify `ConstiaDbContext.cs` and `HabitoConfiguration.cs`; create `PeriodoEstadoHabitoConfiguration.cs`, migration and Designer; update `ConstiaDbContextModelSnapshot.cs`; add `PeriodoEstadoHabitoModelTests.cs`.

**Interfaces:** composite key `(HabitoId,VigenteDesde)`, required FK, check constraint `VigenteHasta IS NULL OR VigenteHasta > VigenteDesde`, filtered unique index for one open period; initialization metadata records immutable UTC cutoff, snapshot-captured and completed markers; immutable per-habit snapshot rows persist owner IANA zone, local cutoff date, FechaInicio and the persisted Estado captured at cutover. No default may imply snapshot capture or backfill completion.

- [x] Add model tests for mappings and constraints.
- [x] Scaffold migrations/snapshot with EF tooling; do not hand-edit the snapshot.
- [x] Inspect Up/Down: only new TEMP-005 tables/indexes/constraints; no changes to schedules/completions.
- [x] Build and confirm no pending model changes; applying migrations remains limited to the mandatory disposable SQL CI tests.

### Task 3: Controlled, resumable backfill

**Files:** create `src/Constia.Infrastructure/Habitos/EstadoHabitoBackfillService.cs`; create `tools/Constia.StateHistoryBackfill/Constia.StateHistoryBackfill.csproj` and `Program.cs`; update `CONSTIA.slnx`, `DEVELOPMENT.md`.

**Interfaces:** explicit console command reads a loopback-only disposable-test or operator-provided connection from environment, requires `--apply`, never logs credentials. Persist one UTC cutover instant once; transactionally lock Usuario/Habito source inputs, convert the cutoff to each owner IANA local date, and commit a complete immutable snapshot per legacy habit including current persisted Estado and FechaInicio. The migration/deployment runbook keeps every application instance stopped through snapshot capture; SQL locks prevent source writes during capture. Snapshot transaction failure rolls back rows and captured marker; retry reuses the already persisted UTC cutoff. Backfill creates missing periods only from immutable snapshot rows, from `max(FechaInicio, LocalCutoffDate)` and captured state; it never rereads mutable Estado or TimeZoneId. Invalid zones/inconsistent rows fail; no auto-repair. Verify expected counts/invariants before setting readiness. Reruns validate/skip existing periods and resume partial work. New habits created after snapshot capture already carry an initial Active period and are not members of the legacy snapshot.

- [x] Test confirmation guard/no-write mode.
- [x] Add disposable SQL tests for zone-specific cutoff, both captured states across fixtures, empty DB, untouched schedules/completions, frozen-cutoff retry, live-state drift fail-closed, and invalid-zone rollback; unit/API tests cover unknown pre-history and readiness gates.
- [x] Implement helper and operator runbook including maintenance order/recovery.
- [x] Confirm API startup never invokes it automatically.

### Task 4: Atomic persistence and concurrency

**Files:** update `IHabitoRepository.cs` and `HabitoRepository.cs` only as needed; add state/initialization repository only if existing patterns require it; extend `HabitoConcurrencySqlServerTests.cs`.

**Interfaces:** tracked aggregate load includes Owner, schedules, and state periods. Existing `GuardarCambiosAsync` bumps root token once and saves current projection plus dependent rows in one EF unit of work.

- [x] Add SQL test with two contexts racing status deactivation and schedule edit; assert one wins, loser conflicts, and no partial periods/schedules.
- [x] Assert existing completion rows remain unchanged.
- [x] Preserve other repository behavior and avoid unrelated query changes.

### Task 5: Application, API, and documentation

**Files:** update `DesactivarHabito.cs`, `RegistrarCumplimiento.cs`, `HabitosController.cs`, `Program.cs`; add application errors for not-initialized/unknown status; update unit/API tests; update `SPECIFICATION.md` and `MVP-SCOPE.md`.

**Behavior:** deactivation calculates one local date through persisted owner TimeZoneId and `IFechaLocalUsuario`. Completion requires current Active and historical Active on requested date. Unknown pre-history maps to `422` code `habit_status_history_unknown`; global initialization incomplete maps to `503` code `habit_status_history_not_initialized`; existing inactive/duplicate `409` behavior remains. Gate all history-dependent writes: deactivation returns 503 before CompletedAt; completion registration returns 503 before CompletedAt. Before SnapshotCapturedAt, also block writes that alter legacy snapshot population/inputs (habit creation and owner timezone changes, and any status mutation). Independent metadata/schedule reads and edits may proceed after snapshot capture; completion remains blocked until full backfill verification. Deployment order is stop all old instances, migrate, capture/verify snapshot, backfill/verify readiness, then enable current app writes. Tests cover each gate and prove unknown history is neither treated as Inactive nor as a valid completion.

- [x] Add tests for timezone/local-date behavior, current and historical inactive, unknown/uninitialized, ownership and duplicate mapping.
- [x] Implement response mappings and readiness wiring; the unknown-history/inactive ordering regression was verified red (409) then green (422).
- [x] Update only status history, cutover, unknown-date and completion precondition text; no metrics/reactivation changes.
- [x] Run unit/API suites locally (131 unit and 80 API passed; SQL-only tests are skipped without the CI service).

### Task 6: Mandatory SQL CI and final verification

**Files:** modify `.github/workflows/ci.yml`; create `tests/Constia.API.Tests/Temp005StateHistorySqlServerTests.cs`.

- [x] Use the existing ephemeral SQL Server service and fake credentials; each test owns and drops its uniquely named database.
- [x] Exclude the TEMP-005 SQL class from ordinary tests; the dedicated step requires exactly five Passed results, rejecting skipped or unexpected tests.
- [x] Run build, focused tests, full ordinary suite, and diff checks. Actual SQL execution remains pending GitHub Actions because no local SQL Server was used.
- [x] Report the final file/status summary without publishing.

## Self-review
All approved rules have a task and named tests: period semantics, no reactivation, cutover, unknown periods, readiness, timezones, same-day behavior, current projection, shared-token concurrency, completion immutability, migration/backfill recovery, API errors and mandatory SQL CI. Execution is inline in this session as authorized; no commit/publication is in scope.

Review rulings:
- Unknown historical state is checked before the current inactive projection, preserving 422 when the requested historical state is unknown; a regression test was observed failing with 409 before the ordering fix and passing afterward.
- Added a SQL Server check constraint for `PeriodoEstadoHabito.Estado IN (0, 1)` and retained domain validation. EF generated the constraint migration and snapshot; `has-pending-model-changes` reports none.
- A live status that diverges from the frozen snapshot remains fail-closed. The immutable snapshot still supplies the only historical input; the service does not overwrite current status or infer a post-cutover effective date. It leaves readiness incomplete for operator reconciliation, because silently applying either value would fabricate or lose a transition.
- SQL integration tests have not run in this local environment; GitHub Actions disposable SQL Server execution remains required before treating database behavior as verified.
