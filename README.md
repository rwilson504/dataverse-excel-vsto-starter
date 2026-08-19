# Dataverse Excel VSTO add-in — starter sample

Loading a spreadsheet into Microsoft Dataverse sounds like a solved problem until you try it
in a regulated tenant. You need OAuth that survives MFA and Conditional Access, environment
discovery across commercial *and* government clouds, somewhere safe to keep connection
details, and enough throughput to move twenty thousand rows without an afternoon of waiting.

This is a working add-in that does all of that, and — more usefully — a record of **why each
piece is built the way it is**, including the approaches that were tried and rejected.

Fork it as a starting point. Everything under `src/` except `DataverseAddIn.ExcelHost` is
host-agnostic, so the interesting parts move to a console app, a service, or an Azure Function
unchanged.

Targets **.NET Framework 4.6.2**, matching the Dataverse SDK.

---

## Where this came from

A friend, Tim, asked a deceptively simple question: **did I know anything about Entra ID app
registrations, and are they the same across clouds?** He was building something along these
lines himself.

The short answer is no, and the long answer is the reason this repository exists:

- **GCC** authenticates against **public** Microsoft Entra ID. Same registration as commercial,
  same sign-in. Only the Dataverse discovery endpoint and token audience differ.
- **GCC High** and **DoD** authenticate against **Microsoft Entra Government**, a physically
  separate directory. A commercial registration is invisible there — "multitenant" does not
  bridge the two.
- So the answer to "one registration or two?" is *two*, and which two depends on the clouds
  you target rather than on how many tenants you have.

That distinction is easy to state and easy to get wrong, because GCC shares the
`dynamics.com` suffix with commercial and is told apart only by a `crm9` region label. Sending
GCC users to `login.microsoftonline.us` is the classic mistake.

Answering Tim properly meant proving it rather than asserting it — which turned into
[a way to probe an environment with no credentials at all](docs/authentication.md#probing-an-environment-without-credentials),
then a working multi-cloud discovery client, then the rest of this. The
[decision records](decisions) are essentially the long-form version of that reply, and the
cloud/identity split is why authentication is modelled so carefully here rather than being a
single `ClientId` constant.

---

## The three decisions that shape everything

Read these before the code; they explain most of what would otherwise look odd.

### 1. A VSTO add-in, not an Office Web Add-in

Office Web Add-ins are Microsoft's current platform, and VSTO is the older one. For a new
Office project the web add-in is usually the right default — so this needs justifying.

The recommendation here changed twice. Web add-in first, because it is the modern answer.
Then a *web add-in with a C# backend*, because Dataverse throttles per web server and a
browser client can never disable the affinity cookie that pins it to one — a structural
throughput cap. Then the actual volume arrived: **20,000 rows worst case**, roughly 2% of the
documented request budget. The argument that had driven the entire design turned out to be
irrelevant at this scale.

What remained was cost. A web add-in must serve its HTML/JS over HTTPS from somewhere, and in
a GCC High context that hosting lands inside the compliance boundary. VSTO deploys to the
workstation and adds nothing to it. It also keeps the ingestion engine as ordinary testable
C# rather than reimplemented TypeScript.

**What that cost:** Windows-only — no Excel on Mac, iPad or the web — and no central
deployment, since VSTO needs a per-machine install and a signing certificate rather than a
push from the Microsoft 365 admin centre. Both were accepted knowingly against a
Windows-desktop user base. The ingestion engine is deliberately host-agnostic as the hedge.
→ [decision 0005](decisions/0005-platform-choice.md) has the full ledger and the triggers that
would reverse it

### 2. `ExecuteMultiple` is not a throughput tool

The most common way to get Dataverse bulk loading wrong. Its operations are *applied
sequentially on the server*, so batching them buys fewer round trips, not speed. Throughput
comes from `CreateMultiple`/`UpdateMultiple`/`UpsertMultiple` plus parallelism at the
environment's advertised degree.

That inverts an assumption most sample code makes, and it means batch size must differ **per
strategy** — large for bulk messages, deliberately tiny for `ExecuteMultiple`.
→ [docs/ingestion.md](docs/ingestion.md) · [decision 0004](decisions/0004-ingestion-engine.md)

### 3. Authentication is pluggable, because one kind is never enough

Interactive sign-in covers a person at a keyboard. It does not cover a scheduled load, a
locked-down desktop, or an on-premises deployment. Rather than growing a `switch`, the design
puts two interfaces in the middle — one that produces tokens, one that produces connections —
so a new authentication kind is an implementation plus a descriptor entry, and **no existing
consumer changes**.

Interactive and client-secret service principals ship today. The connection dialog renders
itself from the descriptor table, so new kinds appear in the UI for free.
→ [docs/architecture.md](docs/architecture.md) · [decision 0006](decisions/0006-token-source-abstraction.md)

---

## Quick start

```powershell
# Build everything the .NET SDK can build, run 173 offline tests, check the dialog
pwsh tools/build-and-test.ps1

# Try the UI without Office
.\samples\DataverseAddIn.Samples.WinFormsHost\bin\Debug\net462\DataverseAddIn.Samples.WinFormsHost.exe
```

No credentials or network needed for either. To connect to a real environment you need an
Entra ID app registration — or, for local experimentation only, Microsoft's published sample
client ID. See [docs/authentication.md](docs/authentication.md).

Building the Excel add-in itself needs Visual Studio with the Office/SharePoint workload and a
locally generated signing key: [docs/vsto.md](docs/vsto.md). VS 2022 is fine on x64; on
Windows on ARM the workload only installs in VS 2019.

> **Do not run `dotnet build` on the solution.** It fails on the VSTO project and leaves
> artifacts that break the next Visual Studio build. `tools/build-and-test.ps1` exists so you
> never have to remember that.

## Layout

| Project | Purpose |
| --- | --- |
| `src/DataverseAddIn.Discovery` | MSAL auth, Global Discovery, minimal Web API client. One NuGet dependency; 8 DLLs. |
| `src/DataverseAddIn.Connections` | `ServiceClient`, credentials, saved connections, secret store. |
| `src/DataverseAddIn.Ingestion` | Bulk engine and sheet mapper. `Microsoft.Xrm.Sdk` only — testable offline, portable to any host. |
| `src/DataverseAddIn.WinForms` | Connection manager, connection details, discovery picker. Host-agnostic. |
| `src/DataverseAddIn.ExcelHost` | The VSTO add-in — ribbon and `ThisAddIn`. The only Excel-aware project. |
| `samples/…ConsoleHost` | Console harness for discovery and connection flows. |
| `samples/…WinFormsHost` | Exercises the UI without Office. |
| `tests/…Ingestion.Tests` | 18 tests: engine, sheet mapper. |
| `tests/…Connections.Tests` | 155 tests: credential identity, factory, connection manager, store, DPAPI secrets, scopes, cache partitioning. |
| `tools/` | Build/test pipeline, signing-key generation, off-screen dialog check. |

## Documentation

| | |
| --- | --- |
| [docs/architecture.md](docs/architecture.md) | How the pieces fit, the auth abstraction, how to add a credential kind, how this is tested |
| [docs/authentication.md](docs/authentication.md) | App registrations, sovereign clouds, scopes, secrets, reading `AADSTS` failures |
| [docs/vsto.md](docs/vsto.md) | Building, signing, the ribbon, the STA deadlock, Office traps |
| [docs/ingestion.md](docs/ingestion.md) | Throughput model, batching, failure isolation |
| [decisions/](decisions) | Dated decision records — what was chosen, what was rejected, and what changed later |
| [AGENTS.md](AGENTS.md) | Orientation for AI coding agents |

The decision records are the most useful thing here if you are evaluating approaches rather
than copying code. Several document a **reversal** — the platform choice and the batch-size
model were both corrected once real numbers arrived, and the original reasoning is kept
alongside the correction.

## Status

Interactive sign-in, client-secret and certificate service principals work end to end, with
interactive verified against a live GCC High tenant. The ingestion engine is covered by
offline tests but has not yet been exercised against a real environment at volume. Six further
authentication kinds are declared and unimplemented.

CI builds and tests on `windows-latest` — Windows is required, not preferred: net462, WinForms
and DPAPI.
