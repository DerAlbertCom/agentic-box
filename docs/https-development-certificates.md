# Development HTTPS certificates

When a sandbox location holds an ASP.NET Core project with launch ports, abox copies the
host's trusted development certificate into the sandbox. Apps started through abox then
serve HTTPS with the certificate your host browser already trusts.

This is part of [application-port setup](application-ports.md): it runs on create and
replace and on `abox ports`. It never starts or restarts apps, and it never changes trust
on the host.

**The sandbox receives the private key of the host's localhost development certificate.**
See the [trust boundary](#trust-boundary).

## When it runs

A project counts as ASP.NET Core when its `.csproj`, `.fsproj` or `.vbproj` explicitly
declares the `Microsoft.NET.Sdk.Web` SDK or a
`FrameworkReference Include="Microsoft.AspNetCore.App"`. Project files are read as text;
MSBuild is not run, so an SDK that only arrives through an import is not detected.

Setup runs only when such a project exists and ports were published. Folders with only
Angular or non-web .NET projects cause no certificate work. A project file that cannot be
read, or a malformed one that mentions ASP.NET, fails the setup.

## Prerequisites and host export

On the host:

- An existing ASP.NET Core development certificate that is valid, exportable and fully
  trusted (`CN=localhost`). abox does not create, trust, clean or import certificates;
  fix a missing or untrusted certificate yourself with `dotnet dev-certs https`.
- An installed .NET SDK whose `dotnet dev-certs` can export that certificate. An older
  certificate format needs an SDK that supports it. abox installs nothing.

In the sandbox:

- Linux with `python3` 3.8 or later, for the sandbox user.
- A Debian-style CA store with `/usr/sbin/update-ca-certificates`. Other images are not
  supported.

abox exports the certificate as a PFX with a generated password into a temporary folder
that only you can read, checks that the export is the trusted certificate, transfers it
and deletes the export. Export output is never printed, not even with `--verbose`.

Do not renew or delete host development certificates while setup runs. The dev-certs CLI
cannot select one certificate atomically, and it receives the generated password as an
argument, visible to privileged process inspection on the host during the export.

## What setup does in the sandbox

| What | Where |
|---|---|
| The certificate with its private key | `/tmp/abox-https-<uid>/active.pfx` |
| Its path and password | `/tmp/abox-https-<uid>/config.json` |
| The reader that every launch runs | `/tmp/abox-https-<uid>/bootstrap.py` |
| The public certificate and Node CA bundles | `/tmp/abox-https-<uid>/public.pem`, `node-<digest>.pem` |
| System trust, public certificate only | `/usr/local/share/ca-certificates/abox-development-https-<uid>/` |

The runtime folder belongs to the sandbox user and only that user can read it. Root
receives the public certificate only. The files are not in the workspace, the home
directory or any image, and they are lost when the sandbox is replaced; the replacement
runs setup again with the host certificate of that moment.

The path and password stay the same for the life of the sandbox, so a renewed certificate
replaces one file.

Every process started through abox (`abox run`, including the shell and Herdr) gets:

```text
ASPNETCORE_Kestrel__Certificates__Default__Path
ASPNETCORE_Kestrel__Certificates__Default__Password
NODE_EXTRA_CA_CERTS
```

`NODE_EXTRA_CA_CERTS` points to a bundle of the certificate plus whatever your own
`NODE_EXTRA_CA_CERTS` file already held, because Node does not read the system store.
That file must be a readable PEM file of at most 1 MiB with `CERTIFICATE` blocks.

A process started with raw `sbx exec` gets none of this. An app that configures its own
endpoint certificate overrides these defaults.

## Restarting apps and agents

A running process keeps the environment it started with. After the first setup, or after
a renewal, restart it through abox:

- **Apps:** restart them.
- **Agents and Herdr's core:** abox recognizes running `hc`, Copilot, OpenCode, pi and
  Claude Code processes that lack the settings. While one is running, setup does not
  report success and new agent launches are refused. The shell and other ordinary
  commands keep working, with a warning, so you can save your work.

abox restarts nothing itself. Save your work, then:

```sh
# If the installed hc supports --fresh:
abox run shell -- hc server restart --fresh

# Older hc (0.1.17 has no --fresh): stop, then start.
# Stop can interrupt the sessions the core manages.
abox run shell -- hc server stop
abox run shell -- hc server start

# OpenCode's service, not just its client, needs a fresh environment:
abox run shell -- opencode service stop
# Then reopen OpenCode through abox's shell or Herdr.
```

Exit other agents yourself and open new ones, then run `abox ports` again. A plain
`hc server restart` can keep the old environment, and closing the OpenCode window does
not stop its service. Renamed or custom wrappers are not recognized; start them fresh
through abox as well.

## Recovery

Setup has no rollback. A failure can leave trust or the certificate half configured, and
success is printed only when everything is ready. Fix the cause and run
`abox ports --location "<folder>"` again.

**After a CLI upgrade** the installed reader no longer matches the CLI. Every launch then
fails with exit code 78 instead of running unverified code. Replace only the reader:

```sh
abox ports --location "/path/to/project" --refresh-https-bootstrap
```

This keeps the certificate and password. It needs no host export, project scan, port
change or root access, and it cannot be combined with `--offset`. It does not check that
HTTPS is ready; retry your launch afterwards, or run plain `abox ports` for the full setup.

Exit codes of setup and of launches through abox when HTTPS is configured:

| Code | Meaning | What to do |
|---|---|---|
| 42 | A running agent or core has old settings; new agent launches are refused | Restart it as above |
| 43 | Process state could not be inspected | Check the sandbox; ordinary commands still work |
| 44 | Setup: your `NODE_EXTRA_CA_CERTS` file is missing or invalid, or 16 different ones are already stored | Fix the file; ordinary commands run with a warning and without Node trust, agent launches fail with 78 |
| 75 | Setup is busy | Retry |
| 78 | The protected files are damaged or the reader does not match | Refresh the reader, or run full `abox ports` |

When the protected files are damaged, or the sandbox's `/tmp` is full or read-only, every
launch through abox fails, including the shell. Run full `abox ports` to rebuild them. To
save work or free space first, `sbx exec <sandbox-name> ...` still works, without the
HTTPS environment. Recreating the sandbox is the last resort: it deletes sandbox-only
files, worktrees and running sessions.

## Trust boundary

- The PFX and its password give the sandbox user and root the private key of the host's
  localhost development certificate. Grant this only to a workspace you trust.
- Do not snapshot a configured sandbox as a template.
- The file checks protect against accidents, not against a malicious sandbox user or root
  editing its own files.

For maintainers: `docs/verification/https/RunReceiverChecks.ps1` runs the checks of the
sandbox-side scripts (`ReceiverChecks.py`) in a disposable Docker container.
