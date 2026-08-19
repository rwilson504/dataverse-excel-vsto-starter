# Agent orientation

Read this before changing anything. It is the short version of what has already been learned
here, and most of it is not inferable from the source.

## What this is

An Excel VSTO add-in that loads spreadsheet data into Microsoft Dataverse, plus four
host-agnostic libraries under `src/`. .NET Framework 4.6.2. Windows-only, deliberately.

- **Why it exists and the three shaping decisions:** [README.md](README.md)
- **How the pieces fit and where the seams are:** [docs/architecture.md](docs/architecture.md)
- **The reasoning, dated, including reversals:** [decisions/](decisions)

## Build and test

```powershell
pwsh tools/build-and-test.ps1                       # everything, ~173 tests, offline
pwsh tools/build-and-test.ps1 -Configuration Release
dotnet test tests/DataverseAddIn.Connections.Tests/DataverseAddIn.Connections.Tests.csproj
```

CI runs the same script on `windows-latest` with `-FailOnVstoArtifacts`.

### Never run `dotnet build` on the solution

The .NET CLI has no OfficeTools targets, so it fails on `DataverseAddIn.ExcelHost` — and the
restore writes `project.assets.json` and `*.nuget.g.*` into `src/DataverseAddIn.ExcelHost/obj`,
after which Visual Studio fails with *"Your project file doesn't list 'win' as a
RuntimeIdentifier"*. Adding `RuntimeIdentifiers`, which the error suggests, does not fix it;
deleting that `obj` folder does.

The **VS Code C# language server restores the solution in the background**, so these artifacts
reappear on their own. `tools/build-and-test.ps1` cleans them locally and fails in CI, where
nothing should be restoring that project.

Build individual projects, never the `.sln`. `dotnet sln add` also writes the **legacy** C#
project type GUID — check it is `{9A19103F-16F7-4668-BE54-9A1E7A4F7556}` afterwards, not
`{FAE04EC0-…}`.

## Conventions

**Decision records.** Anything that changes design, and especially anything that reverses an
earlier choice, goes in `decisions/NNNN-short-title.md` — including what was rejected and why.
Amend an existing record when continuing its line of work rather than opening a new one.

**Session log.** `SESSION_LOG.md` gets an entry per working session: what changed, what was
verified, and what was learned. Wrong turns are recorded, not deleted — several entries exist
purely to stop someone repeating a failed approach.

**Comments** state what the code cannot. No restating the next line.

## Testing discipline

Tests are offline: no network, no credentials, no Office.

**Mutation-check anything important.** A green suite that has never been seen red proves
nothing. Deliberately break the guard, run the tests, confirm the *expected* ones fail, then
revert. Two rules learned by getting them wrong:

- Check **which** tests go red. A mutation failing one test where you expected three means two
  of them are weaker than they look. That is how the `CredentialSpec` `Equals`/`GetHashCode`
  gap was found — a broken `Equals` was invisible to the dictionary tests.
- Confirm the mutation is actually **executed**. A mutation placed after a throwing call is
  unreachable and proves nothing.

**Two things are not unit tested, on purpose.** `ConnectAsync` returns a concrete
`ServiceClient` that needs a live environment — the decision inside it was extracted to
`ConnectionProfile.AdoptOrganizationName` instead. The WinForms dialog is covered by
`tools/verify-connection-dialog.ps1`, which constructs it off screen; run that after touching
the dialog or adding a descriptor.

## Traps that have already cost time

Each of these was a real failure here, not a hypothetical.

**`Control.Visible` is false whenever a control is off screen — including after `ShowDialog`
returns**, which is exactly when callers read the result. Never use it to store intent. This
would have shipped every client-secret connection with null fields.

**Public clients use `<resource>/user_impersonation`; confidential clients use
`<resource>/.default`.** Not interchangeable. `DataverseScope` encodes both.

**Credentials are keyed on the whole `CredentialSpec`**, never on cloud alone. A service
principal and a user can target one cloud; keying on cloud makes them share and overwrite one
MSAL cache. The token-cache filename follows the same key.

**Confidential clients cache in `AppTokenCache`, not `UserTokenCache`.** Attaching to the
wrong one silently re-acquires a token on every call.

**GCC uses *public* Entra ID** despite being a government cloud; GCC High and DoD do not. GCC
also shares the `dynamics.com` suffix with commercial and is distinguished only by the `crm9`
region label — naive suffix matching gets it wrong.

**`ExecuteMultiple` does not improve throughput.** Operations run sequentially server-side.
Use small batches with high parallelism for it, large batches for bulk messages.

**Secrets never go in `connections.json`.** It is plain JSON in the roaming profile and holds
only a reference; the value is a DPAPI blob under the local profile. A test greps the file for
plaintext and fails if anyone adds a secret-bearing property.

**The VSTO project needs `ManifestCertificateThumbprint`.** Removing it fails with `MSB4044`.
The signing key is not in the repository; `tools/new-signing-key.ps1` generates one and patches
the thumbprint locally — that `.csproj` edit must not be committed.

**Never name a namespace segment after a library you alias.** The add-in is
`DataverseAddIn.ExcelHost`, not `….Excel`, because a namespace member shadows a using-alias:
inside `namespace DataverseAddIn.Excel`, `using Excel = Microsoft.Office.Interop.Excel;` is
silently ignored and `Excel.Range` fails with `CS0234` that never mentions aliases. `Office`
is the same trap, since the ribbon uses `using Office = Microsoft.Office.Core;`. Only the
**last** segment matters.

## Adding an authentication kind

The abstraction exists so this costs four small steps and no consumer changes:

1. `IDataverseTokenSource` implementation in `DataverseAddIn.Discovery` (skip if the kind has
   no bearer token — return null from `TokenSource`).
2. `IDataverseCredential` implementation in `DataverseAddIn.Connections`.
3. An `AuthKindDescriptor` entry declaring the fields it needs.
4. A case in `CredentialFactory.Create`.

The dialog, list view and discovery gating all follow from the descriptor. Register a
descriptor **only** when a working credential exists — a test enforces that pairing.

## Verify rather than assert

This repo has a habit of checking claims empirically, and it has repeatedly paid off:
`ServiceClient` was probed for sealed-ness and virtual members before deciding not to wrap it;
the signing-key flow was tested by deleting the key and building; the `.pfx` was inspected
before pronouncing on its risk. When a claim can be tested with one command, test it.
