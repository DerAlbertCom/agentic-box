# Agentic Box

abox gives every folder its own AI agent sandbox, built from a team's squad, on Docker Sandboxes (`sbx`). This glossary names the concepts of that domain; [docs/concept.md](docs/concept.md) holds the full concept.

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

**Kit**:
A v3 kit of the Docker Sandboxes kit spec: a workload (a full root filesystem) or a mixin (layers added on top).

**Kit set**:
The published or locally built result of one squad: one workload kit plus its mixins.

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

**Folder configuration**:
The runtime opt-ins of one sandbox location, in `abox.yaml` and `abox.user.yaml` there, such as its squad, port offset and user-secret selection; never a build input.
_Avoid_: abox JSON, folder config, local config

## Credentials

**Secrets**:
The host-side credentials that a squad's kits declare and sbx's proxy injects; distinct from user-secrets.

**User-secrets**:
.NET user-secret stores that are copied from the host into a sandbox, explicitly selected per sandbox location.
