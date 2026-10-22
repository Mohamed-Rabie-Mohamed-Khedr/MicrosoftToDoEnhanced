# AGENTS.md

## Build and verify

- Build: `dotnet build MicrosoftToDoEnhanced.slnx` from the repo root. Expected **0 errors / 0 warnings** (zero-warning policy — treat warnings as failures).
- Run: `dotnet run --project MicrosoftToDoEnhanced\MicrosoftToDoEnhanced.csproj` (WPF, Windows only; app self-provisions its DB and shows the Sign-in window).
- There is **no test/lint project**. To verify data-layer changes, write a throwaway console project that references `Core.csproj` and sets `MICROSOFTTODOENHANCED_CONNECTIONSTRING` to a temp DB so you don't touch the real one. Assert under `CultureInfo("ar-SA")`.

## PowerShell 5.1 + Arabic (hard rule)

- The shell is Windows PowerShell 5.1: `Set-Content`/`echo`/`Out-File` corrupt UTF-8 (no-BOM) files containing Arabic. Edit source files **only** with the file Edit/Write tools (or `[IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))`). `rg` is unavailable in the shell; use the grep tool.
- The app must run correctly under `ar-SA` culture (RTL + UmAlQura calendar). Never rely on `Convert.ToDateTime(string)` for SQLite date text — it throws under ar-SA.

## Architecture (3 projects, one solution)

- `General/` — data models only (`DataModels.cs`, ctor from `DataRow`).
- `Core/` — entire data layer:
  - `Repositories/Repositories.cs` — one ~1760-line file containing ALL raw SQL for all 8 repository classes (Task/Group/Planned/Attachment/Post/User/AccessChecks + helpers). This is the file you almost always want when changing queries or business rules.
  - `Data/SqlHelper.cs` — query execution (`QueryAsync`/`ExecuteAsync`/`ExecuteReturnAsync`/`InTransactionAsync`/`QueryMultipleAsync`) + `Translate` + `BuildTable`.
  - `Data/DatabaseProvisioner.cs` — startup provisioning; `SQL/CreateSqliteDatabase.sql` — schema, embedded resource in `Core.csproj`.
- `MicrosoftToDoEnhanced/` — WPF UI (Views, ViewModels, Services, Themes). References Core; no SQL here.
- No ORM, no stored procedures — inline SQL strings only. Keep public repository signatures stable (ViewModels depend on them).

## SQLite specifics (easy to get wrong)

- DB file: `%LOCALAPPDATA%\MicrosoftToDoEnhanced\todo.db` (WAL, FKs on every open). Created automatically on first run. Reset = close app, delete `todo.db` + `-wal` + `-shm`.
- Connection override: env var `MICROSOFTTODOENHANCED_CONNECTIONSTRING` wins over config (SqlHelper.cs:12) — use it for tests.
- **SqliteParameter literal-0 trap**: `new SqliteParameter("@x", 0)` binds to the `(string, SqliteType)` overload (int literal implicitly converts to enum) → runtime `InvalidOperationException: Value must be set`. Use `(object)0` for literal zero/nullable values. Int variables and non-zero literals are safe.
- Dates are stored as TEXT; `BuildTable` converts them to `DateTime` (invariant culture) **only for columns listed in `DateColumnNames` (SqlHelper.cs:19)**. A new date column is silently returned as `string` until you add it to that set.
- Unique/PK constraint violations (SQLite 1555/2067) are translated by `Translate` to `InvalidOperationException("That record already exists.")`.
- Self-referencing `Tasks.TaskParentID`: deletes must be children-first (see `DeleteTaskAsync`).

## Conventions

- No code comments anywhere in the repo.
- Repository/user-facing error strings must stay **verbatim** (they mirror the original SQL Server procs; e.g. `UpdateTaskStatus: A pending task cannot be completed before it is approved.`). Don't reword them.
- Keep `SQLitePCLRaw.bundle_e_sqlite3` at **2.1.14+** (2.1.11 and below pull SQLite with CVE-2025-6965 → NU1903). Do not downgrade.
- User-visible docs (`README.md`) are written in Arabic.