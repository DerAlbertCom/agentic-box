# abox

`abox` gives each project folder its own AI agent sandbox, built on
[Docker Sandboxes](https://docs.docker.com/ai/sandboxes/). Create it from your team's
**squad** (a shared set of tools and configuration), then open a shell or the Herdr
desktop app to work in it. You do not need this repository or a local image build
to use a published squad.

The sandbox works on your project files: **agent edits to the workspace affect
the host folder**. Only grant it access to files, services and credentials you
trust it to use. A sandbox is not a guarantee against data loss or secret exposure.

## Before you start

- Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
  to install the tool; it includes the runtime abox needs.
- Install [Docker Sandboxes (`sbx`)](https://docs.docker.com/ai/sandboxes/install/)
  **0.54.0 or later**, on a host supported by Docker Sandboxes, with `sbx` on `PATH`.
  abox checks runtime health and sign-in; interactive use can open Docker sign-in.
  For noninteractive use, run `sbx login` beforehand.
- For **creation, including published squads**, have host Docker with **Buildx**
  and a running Docker Engine. Installing sbx alone does not satisfy this requirement.
- Ask your maintainer for a **published team, squad, tag and pull repository**, plus
  JFrog read access to the squad and its referenced configuration component.
  The current registry host is `rwest.jfrog.io`; its availability is not public or
  guaranteed by this repository.
- For discovered app forwarding, the squad must already provide `python3` **3.8+**.
  ASP.NET Core forwarding also needs an existing trusted host development certificate,
  a compatible installed `dotnet dev-certs` CLI and a supported sandbox CA store.
  See [HTTPS prerequisites](docs/https-development-certificates.md#prerequisites-and-host-export).

abox does not install these dependencies. Optional launchers can have additional
requirements listed below.

## Install

The NuGet package is **`RwestAgenticBox.Cli`**; the installed command is **`abox`**.
It is published to the JFrog NuGet feed `mtiae-cs-nuget-prod`, not to nuget.org.
If that feed is not yet a NuGet source of yours, add it once with your JFrog user and
identity token (on Linux/macOS also pass `--store-password-in-clear-text`):

```sh
dotnet nuget add source https://rwest.jfrog.io/artifactory/api/nuget/v3/mtiae-cs-nuget-prod/index.json --name mtiae-cs-nuget-prod --username <user> --password <identity token>
```

Then install it:

```sh
dotnet tool install --global RwestAgenticBox.Cli
abox --help
```

Installing needs the .NET 10 SDK or newer; it picks the package built for your platform.
If installation cannot find the package, ask your maintainer for read access to the
feed, or use the
[source-build instructions](docs/development.md#building-and-running-the-command-line).
Ensure the global tools directory is on `PATH`: `$HOME/.dotnet/tools` on Linux/macOS
or `%USERPROFILE%\.dotnet\tools` on Windows. The installer reports any setup needed.

```sh
dotnet tool update --global RwestAgenticBox.Cli
dotnet tool uninstall --global RwestAgenticBox.Cli
```

## Quickstart

1. Get the published **team**, **squad** and **tag** from your maintainer.
   `teams/sample` in this repository is example configuration, not a promise of a
   published sample. Remote creation cannot discover squads for you.
2. If the pull repository is not `mtiae-docker-release`, set `pullRepository` in
   your [user settings](#user-settings) first. It is a repository name, not a URL.
3. Sign in to the registry on the host:

   ```sh
   docker login rwest.jfrog.io
   ```

4. Review any existing `abox.yaml` and `abox.user.yaml` in the project, particularly
   their [user-secret selections](#copying-net-user-secrets). Then create:

   ```sh
   abox create --team <team> --squad <squad> --tag <tag> --location "/path/to/project"
   ```

   Replace all `<...>` placeholders and the project path before running these
   commands; on Windows, for example, use `"C:\src\my-project"`. An existing folder
   `squad` setting must match `--squad`. Creation pulls the published kit set,
   copies any opted-in user-secrets, and configures discovered app ports/HTTPS.
   It does **not** start your apps or open a shell. After creation succeeds, open it:

   ```sh
   abox run --location "/path/to/project"
   ```

Remote creation may persistently add `rwest.jfrog.io/<pullRepository>/` to sbx's
allowed kit sources. This trusts that repository, not the whole registry; other
sbx policies still apply. See [source trust and remote creation](docs/cli-reference.md#remote-only-workflow).

**Already have a sandbox for this folder?** `create` asks before replacing it;
`--yes` skips that question, not the data loss. Replacement deletes sandbox-only
files, installed changes and worktrees under `~/worktrees`. The host workspace and
squad-declared persistent state are kept, but that is not a backup of everything
inside. Save anything else you need before consenting. Updating the CLI or running
`run` does not refresh a published squad: explicitly recreate to pull its tag again.

## Everyday use

These commands select `--location`, then the user setting `defaultLocation`, then
the current folder. Use an explicit location when in doubt.

| Task | Command |
|---|---|
| Open the default shell | `abox run --location "/path/to/project"` |
| Run a command instead of the shell | `abox run --location "/path/to/project" -- git status` |
| Open Herdr | `abox run herdr-app --location "/path/to/project"` |
| Keep your terminal's colors | `abox run --terminal-theme false` |
| Inspect required host/proxy credentials | `abox secrets list --location "/path/to/project"` |
| Enter a requested credential | `abox secrets set --location "/path/to/project" --service <service>` |
| Resync selected .NET stores | `abox user-secrets --location "/path/to/project"` |
| Rescan app ports and configure HTTPS | `abox ports --location "/path/to/project"` |

`run` starts a stopped sandbox but **never creates a missing one**. Ordinary runs
do not resync user-secrets or redo app setup. For local builds only, `run` can offer
replacement when a newer build exists; the same replacement warnings apply.

The launchers currently supported are `shell` (default) and `herdr-app`. Herdr
needs `herdr-app`, `hc` and `abox` on the host `PATH`, plus `hc` in the squad's
sandbox. Install the desktop app from [herdr.dev](https://herdr.dev).

## Open your apps in a browser

Start the app **inside the sandbox**, using its normal profile. Creation already
sets up forwarding for explicit .NET and Angular ports. For example, an app on
`http://localhost:8203` is reachable on the host at **`http://localhost:18203`**
with the default **10000** offset.

After changing app ports, or to finish failed setup:

```sh
abox ports --location "/path/to/project"
```

Discovery reads .NET `Properties/launchSettings.json`, Angular serve ports in
`angular.json`, and supported direct `ng` commands with literal ports in
`package.json`'s `scripts.start`. It does not infer Angular's default 4200, evaluate
environment variables, or discover arbitrary servers such as Vite/Next.
Forwarding binds to **host loopback only**. The printed table is a mapping plan,
not evidence that the app is running.

Use folder `portOffset` for a different default, or `abox ports --offset <n>` for
one invocation. **Mappings persist after the command exits.** Changing the offset
does not move existing bindings; conflicts require deliberate removal of the exact
old binding. For a partial failure, retry the printed command with the **same
offset**, rather than recreating the sandbox. See [ports and offset recovery](docs/application-port-offsets.md).

For ASP.NET Core, setup exports the existing trusted host dev certificate into
protected sandbox runtime files and installs its public leaf in sandbox trust.
It does not create host trust or restart apps/agents; explicit restarts may be
needed. See [HTTPS setup and recovery](docs/https-development-certificates.md).

## Configuration and credentials

### Folder configuration

Put shared runtime settings in the project's `abox.yaml`. Put personal overrides
in `abox.user.yaml` and **add that file to `.gitignore` yourself**:

```yaml
# abox.yaml — use the squad supplied by your maintainer
squad: your-squad
portOffset: 10000
```

Both files are optional and accept `squad`, `portOffset`, `terminalTheme`,
`userSecrets`, `memory` (GB) and `cpus`. Personal values override shared ones; **lists
replace, not append**. Unknown keys and invalid YAML fail. abox never writes
`abox.yaml` or `.gitignore`; `abox create --memory <GB> --cpus <n>` saves the sandbox
size in `abox.user.yaml`, and later creates use it when the options are left out.
See the [full folder configuration rules](docs/cli-reference.md#folder-configuration).

### User settings

Personal defaults live in `config.yaml`: `$XDG_CONFIG_HOME/abox/config.yaml` when
`XDG_CONFIG_HOME` is absolute; otherwise `~/.config/abox/config.yaml` on Linux/macOS
or `<home>\.abox\config\config.yaml` on Windows. `HOME` takes precedence over
`USERPROFILE`, including in Windows Git Bash.

```yaml
# Optional defaults; replace these with your team's values.
defaultTeam: your-team
defaultTag: latest
pullRepository: mtiae-docker-release
terminalTheme: true
```

The pull repository defaults to `mtiae-docker-release`, on `rwest.jfrog.io`.
See [all user settings](docs/cli-reference.md#user-settings), including
`defaultLocation` and `userSecretsDenyList`.

### Copying .NET user secrets

This is **opt-in** and different from `abox secrets`. A `userSecrets` selection
in the folder configuration copies host .NET stores/keys into the sandbox on
creation/replacement or an explicit `abox user-secrets` call. Ordinary `run` does
not copy them. Start without this setting unless your application needs it.

**Review the current selection before every copy or replacement.** Both folder
configuration files are writable by the sandbox agent, so an agent edit can change
which host secrets the next operation reads. There is **no separate host-side
approval mechanism**. The deny list defaults to empty; it is not production-secret
detection and cannot make an untrusted workspace safe.

Copying replaces each **entire destination store**, removing old/unselected keys;
an empty key selection writes `{}`. Copied secrets are readable by the sandbox
application user/root, not encrypted. Transfers are atomic **per file**, not across
stores: a later failure can leave earlier stores replaced, with no rollback.
Review [selection, policy and recovery](docs/cli-reference.md#copying-net-user-secrets)
before enabling this feature. `--force` only bypasses the configured deny list;
it is not a general repair flag.

`abox secrets` instead manages host-side kit/proxy credentials. Values are global
on the host, so removing one can affect other sandboxes. It does not configure
Docker's registry sign-in. See [credential behavior](docs/implementation.md#abox-secrets-listsetremove---location-path).

## Troubleshooting

- **Missing sandbox:** use the printed `abox create` hint; `run`, `ports` and
  `user-secrets` do not create one.
- **sbx health/sign-in failure:** follow the diagnostic hint; use `sbx login` if
  sign-in is needed. A registry pull failure also needs Docker/JFrog credentials
  and a real published team/squad/tag, not just sbx sign-in.
- **Creation reports a copy/ports/HTTPS failure:** the new sandbox and record may
  already exist, with partial setup. Fix the cause and use the printed
  folder-qualified retry commands: user-secrets first if copying failed, then
  ports with the original offset. Do not recreate just to retry setup.
  If replacement creation itself failed after removal, the old sandbox is already
  gone; save any retained host data and follow the printed create retry.
- **HTTPS bootstrap mismatch after a CLI upgrade:** run
  `abox ports --location "/path/to/project" --refresh-https-bootstrap`, then retry
  the launch. This only repairs the reader, not full HTTPS readiness.
- **Need more detail:** use `abox <command> --help` or add `--verbose` before any
  `--` separator. Captured tool output goes to stderr; review it for sensitive
  data before sharing. User-secret transfer output stays suppressed even then.

## More documentation

- [CLI reference](docs/cli-reference.md): options, configuration, legacy sandboxes,
  user-secret selection and detailed recovery.
- [App setup lifecycle](docs/application-ports-lifecycle.md): creation, partial
  failure and retries; [HTTPS details](docs/https-development-certificates.md).
- **For maintainers/contributors:** [development and local builds](docs/development.md),
  [authoring squads/kits/workloads](docs/authoring.md),
  [implementation status and code map](docs/implementation.md),
  [concept and proposals](docs/concept.md), and [coding standards](CODING_STANDARDS.md).
