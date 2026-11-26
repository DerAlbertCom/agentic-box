# Internals of user-secrets, application ports and HTTPS

What these features do for the user is described in [user-secrets.md](../user-secrets.md),
[user-secrets-endpoint-rewriting.md](../user-secrets-endpoint-rewriting.md),
[application-ports.md](../application-ports.md) and
[https-development-certificates.md](../https-development-certificates.md). This file
covers only how the code is put together.

All three are shared features (`Features/Shared/UserSecrets`, `ApplicationPorts`,
`Https`). The `user-secrets` and `ports` commands and the creation flow call the same
code. Each has two phases, so `create` can validate everything before it removes a
sandbox:

| Phase | When | Touches |
|---|---|---|
| Prepare | before any replacement | the host only |
| Copy / publish | after the sandbox exists and its record is saved | the sandbox |

Both phases work from the folder configuration snapshot taken at command entry. The
workspace files are writable by the sandbox agent, so they are never read a second
time.

## User-secrets

**Prepare** (`UserSecretsCopy.PrepareAsync`) makes no sandbox call:

- validates all entries and expands ID prefixes against the host store directories;
- snapshots every selected `secrets.json` in memory;
- rejects symlinks and reparse points in the host store root, stores and files;
- checks the selected original values against the deny list, before any rewriting;
- discovers the projects that name a selected `UserSecretsId` and reads their
  appsettings (`ProjectSettings`). It reads project XML statically and never evaluates
  MSBuild.

The result, `PreparedUserSecrets`, lives in memory only and prints no values.

**Copy** (`UserSecretsCopy.CopyAsync`) prepares every store's target before the first
write: it discovers the authorized host ports, rewrites endpoints, merges generated
overrides for shared stores and validates every destination. Then it writes store by
store. Validation covers all stores; the transfer is atomic per file. A failure reports
how many stores were written and does not promise a rollback.

- **Endpoint rewriting** is pure code (`EndpointAdaptation`): it gets the authorized
  ports and the effective settings, and does no discovery or copying.
- **The port inventory** comes from the sandbox alone: `sbx policy ls <sandbox> --type
  network --json` for the declared `localhost:<port>` rules, then `sbx policy check` per
  port for the current authorization. Kit files, build records and images play no part.
- **Only parsed JSON is serialized** for the transfer, so comments and trailing commas
  cannot carry unscanned text.
- **The receiver** (`Infrastructure/Sbx/SbxUserSecrets`, behind `ISandboxUserSecrets`)
  runs as the sandbox's default user, not root. It verifies the byte count and digest
  of standard input before an atomic replace in the same directory. It refuses
  symlinked ancestors, non-regular files and wrong owners. Directories are 0700, files
  0600. It needs `sh` and GNU-compatible coreutils in the sandbox.
- **Store roots** are resolved separately on each side: `<APPDATA>/Microsoft/UserSecrets`
  when `APPDATA` is set (also on Unix), else `<HOME>/.microsoft/usersecrets`.
- **Sensitive output.** These processes run with `ProcessOutputPolicy.Sensitive`: child
  output is discarded even with `--verbose`. Reports contain IDs, key names and counts,
  never values.

`--force` exists only on the `user-secrets` command and bypasses only deny-list matches.
The warning after a forced copy appears only when a value actually matched.

## Application ports

`AppPorts.PrepareAsync` discovers and validates the whole plan; `PublishAsync` applies
it through `IApplicationPortPublisher`.

- **Discovery** (`AppPortDiscovery`) is static: one bounded file walk, then .NET
  `launchSettings.json`, `angular.json` and the `start` script of `package.json`. No
  project tool runs.
- **The offset** is `--offset`, then `portOffset` of the snapshot, then 10000. The full
  plan is checked for resulting ports in 1..65535 before the sandbox is touched.
- **Relays.** sbx's native port publishing does not reach sandbox loopback. The adapter
  (`Infrastructure/Sbx/SbxApplicationPorts`) sends an embedded Python receiver
  (`application_ports.py`) that starts detached, non-root relays on separate high
  ports. A relay forwards to the application's loopback port and never reserves it. sbx
  then publishes `127.0.0.1:<host port>` to the relay port. Relay state lives under
  `~/.abox-ports-v1` in the sandbox. The receiver needs Python 3.8 or later in the
  sandbox and installs nothing.
- **Startup race.** The adapter runs `sbx exec <sandbox> true` before it reads
  `sbx ports <sandbox> --json`. This waits for the sandbox and its existing bindings.
- **Conflicts.** abox never removes a binding. An existing mapping to a known relay at
  another offset, or a direct binding it cannot prove to be its own, refuses the whole
  change and names the binding. Matching port numbers are not proof of ownership.
- Relays end when the sandbox stops. `abox ports` restores them.

## HTTPS

When discovery finds an ASP.NET Core project, `AppPorts` calls `Https/SandboxHttps`,
which also owns `ports --refresh-https-bootstrap` and the launch-readiness check.

- **Host export** (`Infrastructure/Dotnet`, behind `IHostDevelopmentCertificate`). It
  uses an installed `dotnet dev-certs` to export the existing trusted development
  certificate with a generated password into owner-only staging, validates the
  exported certificate, and removes the staging. It never creates a certificate or
  changes host trust.
- **Activation** (`Infrastructure/Sbx/SbxDevelopmentCertificates`, behind
  `ISandboxDevelopmentCertificates`). The encrypted PFX goes to an owner-only path
  outside the workspace and home. A protected `config.json` fixes the Kestrel path and
  password, so a renewal is one atomic file replacement. Only the public certificate
  reaches root, to be installed in the sandbox's CA store.
- **The exec bootstrap.** `SbxSandboxExec` wraps every `sbx exec` in a small bootstrap
  that sets the Kestrel and Node CA environment for the child. That covers the shell,
  `exec`, `pipe` and agents started through them, not only login shells. It keeps
  streams, argv, working directory, TTY flags and the child's exit code.
- **Running parents.** A process started before activation keeps its old environment.
  When a known agent host (`hc`, OpenCode and others) runs with missing or different
  settings, new agent launches are refused with instructions; ordinary shell, `exec`
  and `pipe` keep working. abox restarts nothing.
