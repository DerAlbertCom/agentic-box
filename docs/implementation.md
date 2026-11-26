# How abox is built

This is the entry point for contributors: how the pieces fit, which decisions were
taken, and what is missing. Read it before the code. For using abox, start with the
[README](../README.md) and the [CLI reference](cli-reference.md).

## How the pieces fit

Maintainers turn team sources into images; everyone else creates sandboxes from them.

1. **`abox build`** reads a team's `.abox` and `.workload` files and builds one
   component image per kit of each squad into the host image store. It keeps a build
   record.
2. **`abox publish`** pushes those component images unchanged to JFrog and builds only
   the kit set that composes them.
3. **`abox create`** creates the sandbox of a sandbox location with `sbx`, from the
   published kit set, or with `--local` from the local build. It then copies selected
   user-secrets and sets up application ports and HTTPS.
4. **`abox run`** starts a launcher (a shell or the Herdr app) for the existing sandbox.
5. **`abox secrets`**, **`abox user-secrets`**, **`abox ports`** and **`abox prune`**
   work on existing sandboxes and never create one.

Only `build` and `publish` read team sources. Everything a sandbox command needs
travels with the kit set, in its bundled `squad.json`.

| Topic | Where |
|---|---|
| Build, component images, build record, publish | [implementation/build-and-publish.md](implementation/build-and-publish.md) |
| Finding a folder's sandbox, create, run, launchers, secrets, prune | [implementation/create-and-run.md](implementation/create-and-run.md) |
| Internals of user-secrets copying, application ports and HTTPS | [implementation/user-secrets-and-ports.md](implementation/user-secrets-and-ports.md) |
| Where things live in the code | [implementation/code-map.md](implementation/code-map.md) |
| `.abox` and `.workload` files, kits, state, secret declarations | [authoring.md](authoring.md) |
| The published artifact format and `squad.json` | [artifact-format.md](artifact-format.md) |
| Architecture rules and conventions | [coding standards](../CODING_STANDARDS.md), [ADRs](adr/) |

## External tools

abox installs nothing. It runs these programs, each behind use-case interfaces
([ADR 0003](adr/0003-external-tools-behind-use-case-interfaces.md)):

- **`sbx`** 0.54.0 or later, for every sandbox operation. Every command except
  `--help`, `--version` and `publish` runs a preflight first: `sbx` is on `PATH`, new
  enough, healthy (`sbx diagnose --json`) and signed in.
- **Docker with the Buildx plugin** and a running engine, for `build`, `publish` and
  `create`. Build and local create use the docker-driver builder of the current Docker
  context. Publishing the set uses `--builder` when given, otherwise the builder Buildx
  has selected (including `BUILDX_BUILDER`), so configure a BuildKit builder that can
  push to the registry. Registry credentials stay in Docker's credential store
  (`docker login rwest.jfrog.io`).
- **`dotnet dev-certs`**, `herdr-app` and `hc`, `bash`, and a kit's `build` script, for
  the features that need them.

abox never calls `sbx prune` or `sbx reset`: both remove far more than abox owns.

All external programs run through one process runner, from argument lists, never shell
strings. With `--verbose` it reports each tool's captured output to stderr and redacts
known credential values. User-secrets transfers use a sensitive output policy that
discards child output even with `--verbose`. Unexpected exception messages are not
printed as a fallback, because they could contain secrets.

## Decisions

Decisions with their rejected options are in the [ADRs](adr/). These are the others.

- **Published by default, local on request.** `create` pulls the published kit set
  from the user's `pullRepository`. `--local` uses the local build. A failed pull is
  reported; it never falls back to a local build.
- **A team is a folder under `teams`.** It holds the team's `.abox` files, its
  `.workload` and the workload's scripts. Kits live in the project's `kits` folder so
  every team can use them. A shared workload can live there too.
- **A squad is named after its file.** `--squad AC` finds `ac.abox`.
- **A build is of a team.** `build` makes every squad that no other squad of the team
  names as its `base`; a base is built with the squad that inherits from it.
- **Create needs no sources.** State, MCP and secret declarations reach `create`,
  `run` and `secrets` through `squad.json` in the configuration mixin, for published
  and local builds alike.
- **A sandbox keeps its name.** A replacement is created under the same name, so the
  host paths of its state directories do not change.
- **The workspace is where the runtime puts it.** The runtime sets `WORKSPACE_DIR`;
  launchers ask for it and never assume the image's `WORKDIR`.
- **State is mounted from the host.** sbx mounts a host directory only where a kit
  declares `host-mount@1`, so build writes a state mixin with one declaration per
  state directory and create passes one `--mount` per directory.
- **MCP servers are registered on the host.** A server with a URL is registered with
  `sbx mcp add` when it is missing. A server without one must already be registered.
- **Secrets stay on the host.** Kits ask for a service, the squad configuration says
  how its value is obtained, and `sbx secret` keeps it. The value never enters a
  sandbox or a process argument. User-secrets are different: that opt-in flow copies
  selected host values into files the sandbox application user can read.
- **Publishing moves the chosen tag**, `latest` included. Use a dedicated `--tag` to
  test. Whether `latest` should need extra approval is an open product question.
- **Component images are built for `linux/amd64`.**

## What is not built yet

- Discovering published squads when `--squad` is omitted.
- Detecting file collisions between different kits. Duplicate kit repositories are
  rejected.
- Pruning local builds, their component images, thin kit folders and registry
  components. `abox prune` removes only sbx template images.
- Local overlays of a user on top of a published kit set.
- The `orca` launcher, and `openssh-server` in the base image.
- The Claude Code gateway to Copilot on GitHub Enterprise.
- Keeping the agents' `~/worktrees`, which are lost when a sandbox is replaced.
- Agent configuration files. The kits carry credentials and guidance for GitHub
  Copilot, OpenCode, pi and Claude Code, but do not configure the agents yet.

Proposals and open questions are in [concept.md](concept.md).
