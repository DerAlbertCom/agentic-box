# CLI reference

Start with the [README](../README.md) to install abox and create your first sandbox.
This page lists every command and its options. Settings files are described in
[configuration.md](configuration.md). `abox <command> --help` prints the same options.

## Overview

| Command | What it does | For |
|---|---|---|
| [`create`](#abox-create) | Creates or replaces the sandbox of a folder from a squad | users |
| [`run`](#abox-run) | Starts a launcher (a shell by default) for the sandbox | users |
| [`ports`](#abox-ports) | Publishes discovered app ports on host loopback | users |
| [`user-secrets`](#abox-user-secrets) | Copies selected .NET user-secret stores into the sandbox | users |
| [`secrets`](#abox-secrets) | Manages the host-side secrets the sandbox's kits ask for | users |
| [`prune`](#abox-prune) | Removes template images sbx no longer uses | users |
| [`build`](#abox-build) | Builds the component images of a team's squads | maintainers |
| [`publish`](#abox-publish) | Pushes a team's built kit sets to JFrog | maintainers |

Two hidden entry points exist for tools such as Herdr and are not in the help:
`abox exec <sandbox> -- <command>` and `abox pipe <sandbox> -- <command>`.

### Sandbox location

`create`, `run`, `ports`, `user-secrets` and `secrets` work on one sandbox location:
`--location <path>`, then the user setting `defaultLocation`, then the current folder.
Only `create` creates a sandbox. The other commands print the `abox create` command to
run when the folder has none.

### Exit codes

`0` on success, `1` on an abox error, `130` on cancellation. `run` passes through the
exit code of the shell or command it starts.

### Seeing tool output

Add the global `--verbose` flag to see the complete stdout and stderr of the external
tools abox runs, such as sbx and Docker, on success and failure:

```sh
abox --verbose ports --location "/path/to/project"
```

The flag works before or after the command name, but must come before a `--`
separator. Output is printed to abox's stderr after each tool exits, with its exit
code. Arguments, environment and stdin are not shown, and known credential values are
redacted. Tool output can still contain other sensitive data; review it before sharing.

Output of `user-secrets` transfers is always discarded, even with `--verbose`.

## `abox create`

```sh
abox create --team <team> --squad <squad> [--tag <tag>] [--location <path>]
```

| Option | Meaning | Default |
|---|---|---|
| `--team <name>` | Team the squad belongs to | `team` in the folder configuration, then `defaultTeam` |
| `--squad <name>` | Squad to create the sandbox from. Required here or in the folder configuration | `squad` in the folder configuration |
| `--location <path>` | The sandbox location | see [sandbox location](#sandbox-location) |
| `--tag <tag>` | Tag of the published set or local build | `defaultTag`, then `latest` |
| `--local` | Use the local build of `abox build` instead of pulling | off |
| `--memory <GB>` | Memory in whole GB, at least 1 | folder configuration, then the sbx default |
| `--cpus <n>` | Number of CPUs, at least 1 | folder configuration, then the sbx default |
| `--yes` | Replace an existing sandbox without asking | off |

After a successful create, abox saves the team, squad and any given size in the
folder's `abox.user.yaml`; see [configuration.md](configuration.md). It then copies
the selected [.NET user-secrets](#abox-user-secrets) and sets up
[app ports and HTTPS](#abox-ports). It does not start your apps or open a shell.

**Replacing a sandbox.** If the folder already has a sandbox, `create` lists what will
be lost and asks first. `--yes` skips the question, not the loss: files that exist only
in the sandbox, installed changes and `~/worktrees` are deleted. The host workspace and
the squad's state directories are kept. Everything that can be checked is checked
before the old sandbox is removed.

**If setup fails after creation**, the new sandbox is kept and the command exits with
`1`. Fix the cause and run the printed `abox user-secrets` or `abox ports` retry
command. Do not recreate the sandbox to retry setup.

See [creating local and published sandboxes](#creating-local-and-published-sandboxes)
for what each source needs. A sandbox created with the older cape-agent-sandbox tool is
reused in place; see [legacy-sandboxes.md](legacy-sandboxes.md).

## `abox run`

```sh
abox run [<launcher>] [--location <path>] [--terminal-theme <true|false>] [-- <argv>...]
```

| Argument or option | Meaning | Default |
|---|---|---|
| `<launcher>` | `shell` or `herdr-app` | `shell` |
| `--location <path>` | The sandbox location | see [sandbox location](#sandbox-location) |
| `--terminal-theme <true\|false>` | Whether the sandbox sets the terminal colors from its theme, for this run | see [configuration.md](configuration.md) |
| `-- <argv>...` | Command to run instead of the shell, or in the launcher's first terminal | none |

`run` starts a stopped sandbox but never creates one. It does not copy user-secrets or
set up ports again. For a sandbox created with `--local`, `run` offers to replace it
when a newer local build exists; the replacement warnings of `create` apply. A sandbox
pulled from a published squad is never refreshed by `run`: run `abox create` again to
pull its tag.

`herdr-app` needs `herdr-app`, `hc` and `abox` on the host `PATH`, and `hc` in the
sandbox.

## `abox ports`

```sh
abox ports [--location <path>] [--offset <n>]
abox ports [--location <path>] --refresh-https-bootstrap
```

| Option | Meaning | Default |
|---|---|---|
| `--location <path>` | The sandbox location | see [sandbox location](#sandbox-location) |
| `--offset <n>` | Port offset for this run, `-65534`..`65534` | `portOffset` in the folder configuration, then `10000` |
| `--refresh-https-bootstrap` | Only refresh the HTTPS reader in the sandbox after a CLI upgrade. Cannot be combined with `--offset` | off |

`ports` scans the folder for explicit .NET and Angular app ports and publishes each on
host loopback at port + offset: an app on sandbox port `8203` is reachable at
`http://localhost:18203`. For ASP.NET Core projects it also sets up the development
HTTPS certificate. Mappings stay after the command exits, and apps are not started.

`create` already runs this setup. Run `ports` after changing app ports or to finish a
failed setup. See [application-ports.md](application-ports.md) for discovery rules,
offsets, conflicts and recovery, and
[https-development-certificates.md](https-development-certificates.md) for HTTPS.

## `abox user-secrets`

```sh
abox user-secrets [--location <path>] [--force]
```

| Option | Meaning | Default |
|---|---|---|
| `--location <path>` | The sandbox location | see [sandbox location](#sandbox-location) |
| `--force` | Copy even when a selected value matches `userSecretsDenyList`. Bypasses nothing else | off |

Copies the .NET user-secret stores selected by `userSecrets` in the folder
configuration from the host into the existing sandbox. It fails when the folder has no
folder configuration. Each copy replaces the whole destination store.

The folder configuration is writable by the sandbox agent, so review the selection
before every copy or create. See [user-secrets.md](user-secrets.md) for selection,
the deny list, endpoint rewriting and recovery.

## `abox secrets`

```sh
abox secrets list   [--location <path>]
abox secrets set    [--location <path>] [--service <name>]...
abox secrets remove [--location <path>] [--service <name>]...
```

| Subcommand | What it does |
|---|---|
| `list` | Lists the services the sandbox's kits ask for and whether a value is stored |
| `set` | Asks for the values and stores them on the host |
| `remove` | Removes the stored values from the host |

`--service <name>` limits `set` and `remove` to the named services and can be repeated.

These are the host-side credentials that sbx's proxy injects, not .NET user-secrets.
The services are read from the folder's existing sandbox, so no team sources are
needed. Values are stored globally on the host: removing one affects every sandbox
that uses it. `secrets` does not configure Docker's registry sign-in.

## `abox prune`

```sh
abox prune [--full] [--sbx]
```

| Option | Meaning |
|---|---|
| `--full` | Try to remove everything sbx holds, not only stale sets. sbx keeps what a sandbox uses |
| `--sbx` | Stop the running sandboxes first |

Without options, `prune` removes the template images of sbx that are no longer used and
keeps the newest kit set and the kit sources it is built from. It never removes
secrets or images of other repositories.

## `abox build`

```sh
abox build [--team <name>]... [--root <dir>] [--tag <tag>] [--rebuild]
```

| Option | Meaning | Default |
|---|---|---|
| `--team <name>` | Team to build; can be repeated | `defaultTeam` |
| `--root <dir>` | Folder to start searching for team sources | see [team sources](#team-sources) |
| `--tag <tag>` | Tag the build is stored under | `defaultTag`, then `latest` |
| `--rebuild` | Build every component image again, also unchanged ones | off |

Builds one component image per workload, local kit, state mixin and configuration
mixin of each squad into the host image store, and rebuilds only what changed. Nothing
is pushed. Needs host Docker with Buildx. See [development.md](development.md) and
[authoring.md](authoring.md).

## `abox publish`

```sh
abox publish [--team <name>]... [--root <dir>] [--tag <tag>] [--builder <name>]
```

| Option | Meaning | Default |
|---|---|---|
| `--team <name>` | Team to publish; can be repeated | `defaultTeam` |
| `--root <dir>` | Folder to start searching for team sources | see [team sources](#team-sources) |
| `--tag <tag>` | Tag of the local build and of the published set | `defaultTag`, then `latest` |
| `--builder <name>` | Buildx builder for the set | Docker's selected builder, including `BUILDX_BUILDER` |

Pushes the component images of the build unchanged and publishes the kit set of each
squad to `rwest.jfrog.io/<publishRepository>`, the repository named in the squad's
workload. A stale build is refused; run `abox build` again. **Publishing moves the
chosen tag**, `latest` by default, so use a dedicated `--tag` for tests. Needs Docker
with Buildx and `docker login rwest.jfrog.io`; it does not need sbx. See
[development.md](development.md) and [artifact-format.md](artifact-format.md).

### Team sources

Only `build` and `publish` read team sources (the `teams` and `kits` folders). They
search from the first of these that applies, upwards through its parent folders:

1. `--root <dir>`
2. the current folder, when team sources are found from there
3. the user setting `sourceRoot`

If none applies, an interactive run asks for the repository folder and saves it as
`sourceRoot`; without a terminal the command fails. Inside the repository, the first
interactive run offers to save that folder as `sourceRoot`.

## Creating local and published sandboxes

Both ways need a healthy, signed-in sbx and host Docker with Buildx and a running
engine. Neither reads team sources.

| | Published squad (default) | Local build (`--local`) |
|---|---|---|
| Source | Kit set pulled from `rwest.jfrog.io/<pullRepository>` | Component images of `abox build` in the host image store |
| Needs | `docker login rwest.jfrog.io` with read access | A matching local build for the team, squad and tag |
| State and MCP settings | From the published set | From the local build |
| Refresh | Run `abox create` again | `abox run` offers it after a newer build |

For example, `abox create --team sample --squad ac --tag latest` pulls
`rwest.jfrog.io/mtiae-docker-release/agentic-box-rwest-sample/ac:latest`.

**Local builds.** `create --local` checks that every component image of the build is
still in the image store before it replaces a sandbox. If one is missing or replaced,
or the build was made by an older abox, run `abox build` again; `create` never builds.
Keep the Docker context that was used for the build available. No recipe of the squad
runs again, but the first create can take time, and registry kits may still be
fetched, so `--local` does not work fully offline.

### Remote-only workflow

A published squad needs no checkout, local build or abox user configuration:

```sh
docker login rwest.jfrog.io
abox create --team <team> --squad <squad> --tag <tag> --location "/path/to/project"
```

Ask your maintainer for the published team, squad and tag; abox cannot discover them.
`teams/sample` in this repository is example configuration and may not be published.
Give `--squad` or set `squad` in the folder configuration.

Remote create only inspects and pulls: nothing is built or pushed, and no local kits
are applied on top of the published set. If the set's metadata is missing or invalid,
creation fails before an existing sandbox is removed, even with `--yes`, and a
maintainer must build and publish the squad again.

### Source trust

sbx only accepts kits from the sources listed in its `kit.allowedSources` setting.
Before pulling, remote create adds `rwest.jfrog.io/<pullRepository>/` to that list
unless an existing entry already covers it.

- The addition is persistent and trusts that one repository, not the whole registry.
  Existing entries are kept and no wildcard is added.
- `--local` never reads or changes the setting.
- If the setting is invalid, restricted by an administrator or fixed by an environment
  override that excludes the repository, creation stops before any registry work or
  sandbox replacement.
- The first addition copies sbx's default list into an override, which then no longer
  follows sbx upgrades. `sbx settings unset kit.allowedSources` restores the defaults
  and removes the added trust; a later remote create adds its repository again.
