# Publishing the v3 artifact format

## The legacy push limitation

Sbx's internal template tags (`sandbox-kit-assembled:<build id>`, the `-kit-N`
and `-template` tags, and `sbx-kit-src:<kit>-<hash>`) are not a publishing interface.
They cannot simply be renamed or exported through `sbx template`.

The original experiment with `sbx kit push <directory> <reference>` on sbx 0.51.0
also failed: its `spec.yaml` packager rejects the v3 descriptor's top-level
`capabilities`, `author`, `provides` and `args` fields. Wrapping them under `spec:`
does not fix the different grammar. Sbx 0.50.1's installed help still describes
that legacy packager. This is an artifact-format mismatch, not a registry
reachability problem.

## The supported v3 publishing path

The [official v3 frontend](https://github.com/docker/sandbox-kit-spec#publishing-a-set-as-one-kit)
supports publication independently of that packager:

1. Build each local kit with its descriptor as the Buildx `--file`. The first line
   `# syntax=docker/sandbox-kit:3` selects the frontend. `abox build` loads each
   component image into the host image store (`--load --provenance=false
   --platform linux/amd64`); `abox publish` pushes it unchanged with
   `docker push`, which keeps its manifest digest, and checks that the pushed image
   kept its v3 descriptor.
2. Generate a `kind: set` descriptor whose `kits` list contains published references
   and each kit's create-phase `args`. Local directories cannot appear in that list.
3. Build and push the set with the same frontend. It resolves the component
   references (pinned by digest) from the registry, validates composition, shares
   their layers and produces one ordinary workload kit. Consumers never see
   `kind: set`. This is the one `buildx build` that `abox publish` runs, because
   the frontend resolves set components only from the registry.

`abox publish` uses this path for `rwest.jfrog.io/<publishRepository>`, using the
repository selected in each resolved `.workload` and recorded in its local build
manifest. The included workloads select `mtiae-developer-container-images-local`.
Authentication uses Docker's host credential configuration. It does not depend on
a change to the legacy `sbx kit push` implementation. The full command, naming and credential
expectations are in [implementation.md](implementation.md).

Every squad uses the stable path `agentic-box-rwest-<team>/<squad>:<tag>`, with
components below a separate `/components/` segment
(`components/{workload,kits/<kit>,state,config}`, tagged with their input hash).
Builds must retain matching source fingerprints, component images and pinned
registry digests; all selected builds are checked before the first push. Uploaded artifact digests and every
runnable platform descriptor are inspected remotely; only then is the verified
candidate set promoted to the requested tag. No sbx preflight is needed to publish.

Local verification with Docker Buildx 0.38.1 and frontend 3.0.0-m.11 confirmed that
the generated configuration mixin builds as an OCI image and retains its v3
descriptor in `vnd.docker.sandbox.kit.descriptor`. A declaration-only mixin with a
required create-phase argument also built successfully. Create-phase values are
fixed in the set descriptor, not passed as Buildx arguments; supplied build-phase
arguments are rejected. No JFrog tags were changed during these checks. A live JFrog
push still requires maintainer credentials and an explicitly chosen tag.

Publishing and self-contained pulling in `abox create` are implemented. Remote
creation resolves the selected platform manifest, validates its processed v3
workload descriptor, and reads the one digest-pinned `/components/config` mixin
listed in its retained `kits`. Docker credentials handle these registry reads.
The small mixin is pulled and saved locally; its ordered image layers are read as
tar/gzip streams without extracting files or executing a container. Temporary
archives are deleted; the image stays in Docker's cache. The final set is given to
sbx by immutable platform digest so metadata and declarations cannot drift with
a moving tag. No workload is built and no registry is written.

The bundled `/usr/share/abox/squad.json` contains `SchemaVersion: 1`, `Team`,
`Squad`, `Tag`, `CreatedAt`, `Mcp`, `State`, informational `Kits` and `Secrets`.
`Secrets` holds the resolved `secrets` declarations of the squad chain, each with
`Service`, `Prompt`, `Hosts`, `Env`, `Inputs` (`Name`, `Prompt`), `Separator`,
`Encoding` (`plain` or `base64`), `Placeholder` and `FromCommand` (whether the value
comes from a host command). It holds declarations only, never values or host
commands: `FromCommand` marks a command-backed secret without its command, and abox
never runs a command from a sandbox. Every field but `Service` is optional; anyone who can read the set can read it, so prompts must not be
confidential. `Secrets` is additive and optional: the schema stays at version 1,
remote create accepts metadata with and without it, and older abox versions ignore
it. The original publisher's identical format without `SchemaVersion` is also
accepted as version 1.

`abox build` generates this configuration mixin (`config/` in the build directory:
`squad.json`, a declaration-only `config.yaml` and a `config.dockerfile` copying the
file into place) and builds it into the config component image. `abox create
--local` reads `squad.json` from that image and passes it as an additional kit, so
local sandboxes carry `squad.json` too, and `abox publish` pushes the same image as
`/components/config`.
Identity, required fields and [portable canonical state paths and non-overlapping host mappings](authoring.md#state)
are checked with the same rules as local creation; every mount must have
an exact host-mount declaration in the processed set. Missing or incompatible
metadata requires a maintainer rebuild/republish, never a local-manifest fallback.
All metadata/MCP checks precede sandbox removal. Remote squad discovery is not
implemented; `--squad` is required. `--local` consumes the component images of the
local build through thin directory kits instead of pulling the published set; see
below.

## Local builds without registry writes

Sbx 0.51.0 does not provide `kit build`. Importing an OCI image with
`sbx template load` does not make its kit reference resolve offline: sbx fetches
the manifest of every OCI kit reference from its registry, even with
`--pull never`. Only directory kits can use local images.

Agentic Box therefore uses the supported source-kit host-builder path. `abox build`
builds every component once with the v3 frontend into host Docker using `--load`.
`abox create --local` writes thin directory kits into `<build dir>/create/`: the
workload as its descriptor plus `FROM <ref>@<digest>`, each mixin as its descriptor
plus `FROM scratch` and `COPY --from=<ref>@<digest> / /`, because sbx keeps only the
layers a mixin recipe adds on top of its `FROM`. It uses them with sbx's host builder
and the recorded docker-driver context, preserving all v3 composition and runtime
capabilities. No recipe of the squad runs at create. The recorded Docker content
IDs are checked before replacement; missing or replaced images require a rebuild.
See [ADR 0006](adr/0006-build-produces-images-publish-pushes-them.md).

Creation still stages/exports/imports content and composes mixins; it is not a
promise of zero BuildKit work. Unlike the v2 POC, this route needs neither v2
translation nor a template override that could lose the workload's v3
declarations.

Remote consumers need neither sources nor any local build. See the
[remote-only workflow](cli-reference.md#remote-only-workflow).
