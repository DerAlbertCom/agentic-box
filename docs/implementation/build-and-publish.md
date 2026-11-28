# Build and publish

`abox build` makes one component image per kit of a squad and a build record.
`abox publish` pushes those images unchanged and builds only the kit set.
[ADR 0006](../adr/0006-build-produces-images-publish-pushes-them.md) records why.

## Finding the team sources

`build` and `publish` look for the `teams` folder in a start directory and its parents.
The start directory is, in this order:

1. `--root`,
2. the current directory, when team sources are found from there,
3. the user setting `sourceRoot`,
4. on a terminal, a prompt that validates and saves `sourceRoot`.

Without a terminal the command fails and names `sourceRoot` and `--root`. The handler
resolves this, not the option binding, because the build is the first point that needs
the sources. The discovered team carries its project root into kit and shared-workload
lookup, so a nested invocation directory never becomes a kit root.

`--team` can be repeated; without it `defaultTeam` is used. The user settings
`teamsDirectory` and `kitsDirectory` rename the two folders.

## What build does

For every squad of the team that no other squad names as its `base`:

1. **Resolve the chain.** Read the squad's `.abox` and follow `base`, base first. A base
   is looked up in the squad's own team folder, then in the other team folders; one
   that several other teams hold is an error.
2. **Check it.** Errors: a cycle; a kit, MCP server, parameter, state directory or
   secret declared twice; overlapping state directories; an undeclared parameter; a
   workload without a valid `publishRepository`; a local kit argument that is not a
   create-phase argument. The error names the duplicate and the files declaring it.
   All squads are resolved and checked before Docker is used.
3. **Pin.** Resolve the workload's `template` and every registry kit to the digest its
   tag points to now (`docker buildx imagetools inspect`). A reference that already
   carries a digest keeps it.
4. **Generate the workload kit.** `workload.yaml` and `workload.dockerfile`
   (`FROM <repo>@<digest>`), with the workload's scripts and the bundled CA
   certificates.
5. **Run kit build steps.** A local kit with a `build` file is copied to
   `kits/<kit>/` in the build directory, and `build <stagedKitDir>` runs from the kit's
   repository folder ([ADR 0001](../adr/0001-kit-build-step.md)). Only a kit that is
   built again is staged again.
6. **Generate the state mixin**, when the squad declares state: one `host-mount@1` per
   state directory. Its image creates the directories owned by the sandbox user,
   because sbx would create a missing mount point as root.
7. **Generate the configuration mixin**: `squad.json` and a recipe that copies it to
   `/usr/share/abox/squad.json`. See [artifact-format.md](../artifact-format.md).
8. **Build one image per component** with the `docker/sandbox-kit:3` frontend:
   `docker buildx build --platform linux/amd64 --provenance=false --load
   --metadata-file`. The digest comes from `containerimage.digest`. Only the workload
   gets the `GITHUB_TOKEN` build secret. Nothing is pushed.
9. **Write the build record.**

The components are `workload`, `kits/<kit name lowercased>` for each local kit, `state`
and `config`. Each image carries its final JFrog name:

```text
rwest.jfrog.io/<publishRepository>/agentic-box-rwest-<team>/<squad>/components/<name>:<input hash>
```

### The generated workload image

The workload generator turns a `.workload` into a Dockerfile:

- The bundled RWE CA certificates are installed first, before any download, into
  `/usr/local/share/ca-certificates/abox/`. They are embedded in `Abox.Core`
  (`Features/Build/Assets/certificates`), so an installed tool does not need the
  repository. `ENV` lines point the tools that ignore the system store at it. Both are
  described in [Corporate CA certificates](../corporate-certificates.md). The template
  must provide `update-ca-certificates` and `curl`.
- `apt` packages are installed in one step, as root, with `--no-install-recommends`.
- mise is installed as `agent` when tools or scripts are requested. The installer is
  downloaded before it is executed, so a failed download fails that step.
- There is one install step per tool, in workload order, so anonymous GitHub rate
  limits stall one step, not the build. Several versions of one tool share a step and
  are passed to mise reversed: mise activates the first entry, and the last version in
  the workload must be the active one.
- The GitHub token is a BuildKit secret (`github_token`), so it never lands in a layer.
- Generated files and copied scripts use LF line endings on every host. Two scripts
  with the same file name (case-insensitive) are rejected.

## Incremental builds

A component is built again only when:

- its input hash changed,
- its image is no longer in the image store with the recorded image ID, or
- `--rebuild` is given.

The input hash covers the component's kind, directory content and kit arguments. For a
kit with a build step it covers the kit's repository folder, so the step runs only
when that folder changed. Files outside the folder that the step reads, such as
`sbox-cli/`, are not covered: use `--rebuild` after changing them.

A moved base digest changes the workload's Dockerfile and so rebuilds the workload.
The config mixin carries the build time (`CreatedAt`). It keeps its date when nothing
changed and is built again when any component was built again, a pinned digest moved or
the squad metadata changed.

## The build record and the build store

A build lives in `<state dir>/builds/<team>/<squad>/<tag>/`, with lowercase team and
squad. The tag is `--tag`, then `defaultTag`, then `latest`. The record is `build.json`
(`BuildManifest` in code). It holds:

- per component: name, kind, reference, digest, image ID, input hash, descriptor and
  directory;
- the pinned base image and registry kit digests;
- each local kit's repository folder;
- the image store (the Docker context), the `publishRepository` and the build time;
- SHA-256 fingerprints of the sources: configuration files, the workload file and its
  scripts, and the folders the component images were built from.

MCP servers and state are not in the record. `squad.json` in the configuration image is
their only place.

A record is used only when its squad, tag and team (`Reference`) match the request. A
record of the older layout `<state dir>/builds/<squad>/<tag>/` is usable only when it
names its team unambiguously. Anything else requires a rebuild; abox never infers
ownership from directories or source paths. A build replaces an unreadable or foreign
record of its own squad and tag with a full build.

Teams with the same squad and tag keep separate builds. Two source projects that use
the same team name share that team's build store: the project is not part of the path.

### Tags

Build, create and the build store share one tag grammar: 1 to 128 ASCII letters,
digits, underscores, periods or hyphens, not starting with a period or hyphen. The tag
is checked before it is used in a path or passed to a tool. An explicitly blank
`defaultTag` is an error, not a request for `latest`.

## What publish does

1. **Load** every selected build. A record without components (an older build) is
   refused, as are invalid names and case-insensitive team or squad collisions.
2. **Check freshness before Docker is used.** Compare the source fingerprints and each
   local kit's repository folder with the record.
3. **Check freshness before the first push.** Every component image must still be in
   the image store with its image ID, and the base image and every registry kit tag
   must still resolve to the pinned digest. A stale build fails with
   `Run 'abox build --team <team> --tag <tag>' before publishing.`
4. **Push each component** unchanged (`docker --context <store> push <ref>`). Check that
   the registry digest equals the build digest and that the pushed image kept its v3
   descriptor and kind.
5. **Build and push the set** (`kind: set`) with `docker buildx build --push`. It lists
   the workload, the kits in order (pinned by digest, with their create-phase
   arguments), the state mixin and the config mixin. This is the one build publish
   keeps, because the frontend resolves set components only from the registry.
6. **Promote.** The set is pushed under a candidate tag,
   `agentic-box-rwest-<team>/<squad>:publish-<id>`, and verified: every runnable
   platform manifest must carry its v3 descriptor and kind. Then its digest is tagged
   with `imagetools create --prefer-index=false --tag <target>`, and the target tag is
   inspected again before success is reported.

The destination is `rwest.jfrog.io/<publishRepository>/agentic-box-rwest-<team>/<squad>:<tag>`.
The repository is the one recorded at build time; there is no fallback. Names are
lowercased. Every squad keeps its name when others are added.

### Failure behaviour

- A failed component push or candidate check cannot move the requested tag.
- Publishing is not a transaction across squads or teams. A failure after a promotion
  does not roll that tag back.
- Images already pushed stay in JFrog. Candidates and components are not removed by
  abox; registry retention is separate.
- The temporary set descriptor is removed even on failure. A cleanup failure is a
  warning and does not hide the original error.

Freshness protects local inputs and pinned digests, not downloads inside recipes. Pin
tool versions for reproducible builds.

### Why not `sbx kit push`

Its legacy `spec.yaml` packager does not understand v3 descriptors. See
[artifact-format.md](../artifact-format.md).

### Credentials

`publish` does not call sbx. Docker and Buildx use Docker's host credential store, not
`abox secrets` or the sbx sign-in. The JFrog identity needs read and write access to
the target repository. Before the first push, publish checks the stored sign-in and
starts `docker login` on a terminal when it is missing or rejected (see
[the registry sign-in](create-and-run.md#the-registry-sign-in)). For automation use
`docker login --username <user> --password-stdin rwest.jfrog.io`. abox never passes
credentials as process arguments.
