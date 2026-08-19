Read [AGENTS.md](../AGENTS.md) at the repository root before making changes. It covers the
build commands, the conventions (decision records, session log, mutation testing), and the
traps that have already cost time here — several of which are not inferable from the source.

Key points, in case that file is not loaded:

- **Never run `dotnet build` on `DataverseExcelAddIn.sln`.** It fails on the VSTO project and
  writes NuGet artifacts into `src/DataverseAddIn.Excel/obj` that break the next Visual Studio
  build. Use `pwsh tools/build-and-test.ps1`.
- Tests are offline and must stay that way — no network, no credentials, no Office.
- Mutation-check important guards: break it, confirm the expected tests fail, revert.
- Design changes and reversals belong in `decisions/`; session outcomes in `SESSION_LOG.md`.
