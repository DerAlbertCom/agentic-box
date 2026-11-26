# Agentic Box

abox gives every folder its own AI agent sandbox, built from a team's squad, on Docker Sandboxes (`sbx`). This glossary names the concepts of that domain.

## Teams and squads

**Team**:
A group that shares one base image and has several squads.

**Squad**:
A named configuration of a team that a sandbox is created from; it publishes as one kit set.
_Avoid_: profile, flavour

**Squad configuration**:
An `.abox` file: composes a squad from its `base`, `workload`, `kits`, `parameters`, `mcp`, `state` and `secrets`.
_Avoid_: config, abox file

**Workload**:
A `.workload` file: the base image, its tools and scripts that a squad's workload kit is built from.

**Base image**:
The image built from a workload. Maintainers build it; users get it through their squad's kits.

**Kit**:
A v3 kit of the Docker Sandboxes kit spec: a workload (a full root filesystem) or a mixin (layers added on top).

**Kit build step**:
An executable `build` file in a local kit; `abox build` runs it on a staged copy of the kit, and the kit's component image is built from that copy.

**Component image**:
The image of one kit of a squad that `abox build` makes: its workload, a local kit, its state mixin or its configuration mixin. Publishing pushes it unchanged.
_Avoid_: component (alone), local kit image

**Build record**:
What `abox build` keeps about the build of one squad and tag: its component images with their digests and inputs, and the base image and registry kits pinned by digest.
_Avoid_: manifest, build.json

**Kit set**:
The result of one squad: one workload kit plus its mixins. Published, it is a set image in JFrog that composes the pushed component images; locally, the same component images in the host image store.

## Sandboxes

**Sandbox**:
The isolated environment of one folder, created from a squad; there is exactly one per sandbox location.
_Avoid_: container, VM

**Sandbox location**:
The host folder a sandbox works on: the `--location` given on the command line, otherwise the `defaultLocation` of the user configuration, otherwise the current folder.
_Avoid_: effective location, folder (alone), workspace

**Workspace**:
The files of the sandbox location as the sandbox sees them.

**State**:
Directories of a sandbox that persist on the host when the sandbox is replaced.

**Launcher**:
What `abox run` starts for a sandbox: a shell, or a desktop app on the host that works with it.

## Configuration

**User configuration**:
The personal settings of one user in the abox config directory (`config.yaml`), such as the default team, location and tag.
_Avoid_: global config, settings

**Source root**:
The repository folder with the team sources that `abox build` and `abox publish` search from when the current folder has none; the `sourceRoot` user setting.

**Folder configuration**:
The runtime opt-ins of one sandbox location, in `abox.yaml` and `abox.user.yaml` there, such as its squad, port offset and user-secret selection; never a build input.
_Avoid_: abox JSON, folder config, local config

## Credentials

**Secrets**:
The host-side credentials that a squad's kits declare and sbx's proxy injects; distinct from user-secrets.

**User-secrets**:
.NET user-secret stores that are copied from the host into a sandbox, explicitly selected per sandbox location.

## Inside the sandbox

**sbox**:
The command line inside a sandbox for user-facing actions, such as switching the theme. It routes `sbox <command>` to an installed command kit.
_Avoid_: confusing it with `sbx`, the Docker Sandboxes CLI on the host

**Command kit**:
A kit that installs one `sbox` command. A squad chooses the commands of its sandboxes by choosing command kits.

**Theme**:
A named palette (`colors.toml`) that `sbox themes <name>` applies to every themed app in the sandbox.

**App template**:
The definition of how one app is themed: an `app.yaml` and the templates it renders with the theme's colors.

**Terminal color script**:
The script the `themes` kit renders to recolor the current terminal to the theme. See [ADR 0002](docs/adr/0002-terminal-colors-from-the-sandbox.md).

## Code

**Command slice**:
The code of one `abox` command and its subcommands, in `Features/<Command>`.

**Shared feature**:
A use case shared by commands, in `Features/Shared/<UseCase>`, named by what it does, never by the tool it calls.

**External tool**:
A program abox runs (`sbx`, `docker`, `dotnet dev-certs`, `herdr`, `bash`, a kit's `build` script) or a file another tool owns (sbx's `credentials.yaml`, the .NET user-secrets store).

**Use-case interface**:
A small interface in a shared feature through which features use an external tool, in domain terms. See [ADR 0003](docs/adr/0003-external-tools-behind-use-case-interfaces.md).

**Infrastructure adapter**:
A class in `Infrastructure/<Tool>` that implements a use-case interface with one external tool.
