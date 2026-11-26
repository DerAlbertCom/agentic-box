# Artifact format

What `abox build` produces, what `abox publish` pushes and what `abox create`
consumes. All of it uses the v3 kit format of Docker's
[kit spec](https://github.com/docker/sandbox-kit-spec). The decision behind it is
[ADR 0006](adr/0006-build-produces-images-publish-pushes-them.md).

## Component images

`abox build` builds one image per component of a squad into the host image store, with
the `docker/sandbox-kit:3` Buildx frontend, for `linux/amd64`:

| Component | Content |
|---|---|
| `workload` | The base image: template, apt packages, mise tools, scripts, CA certificates. |
| `kits/<kit>` | One local kit, from its folder or its staged copy after a [build step](authoring-kits.md#kit-build-step). |
| `state` | One `host-mount@1` per state directory. Only when the squad declares state. |
| `config` | The squad's `squad.json`, see [below](#squadjson). |

Each image already carries its final registry name and is tagged with the hash of its
inputs:

```text
rwest.jfrog.io/<publishRepository>/agentic-box-rwest-<team>/<squad>/components/<component>:<input hash>
```

Team, squad and kit names are lowercased. Registry kits are not built; the build pins
them, and the workload's `template`, to the digest their tag resolves to.

The build record (`build.json`) and the generated kit sources are kept in
`<state dir>/builds/<team>/<squad>/<tag>/`.

## The published kit set

`abox publish` pushes the component images unchanged with `docker push`, so their
digests in JFrog equal the local ones. It then builds and pushes one `kind: set`
descriptor that lists, pinned by digest, the workload, the kits in order with their
create-phase arguments, the state mixin and the configuration mixin:

```text
rwest.jfrog.io/<publishRepository>/agentic-box-rwest-<team>/<squad>:<tag>
```

The frontend composes the set into one ordinary workload kit that shares the
components' layers. Consumers never see `kind: set`.

The set is first pushed under a candidate tag `publish-<id>` and verified. Only then
does the requested tag move to it. A failed push or verification leaves the requested
tag alone. Publishing several squads is not one transaction: squads that were already
promoted stay promoted when a later one fails. Candidate tags and old component
images stay in JFrog; cleaning them up is registry retention, not `abox prune`.

`sbx kit push` is not used. It packages the legacy `spec.yaml` format and rejects v3
descriptors.

## squad.json

The configuration mixin installs `/usr/share/abox/squad.json` in every sandbox, local
or published. `abox create` reads state and MCP settings from it, and `abox secrets`
reads the secret declarations from it. This is why neither needs the team sources.

| Field | Content |
|---|---|
| `SchemaVersion` | `1`. A file without it is read as version 1. |
| `Team`, `Squad`, `Tag` | The identity of the set. |
| `CreatedAt` | The build time. `abox run` compares it to find a newer local build. |
| `Kits` | Kit names and arguments, for information. |
| `Mcp` | The MCP server names and resolved URLs. |
| `State` | The state directories, as canonical sandbox paths with their host mapping. |
| `Secrets` | Optional. The resolved [secret declarations](authoring-secrets.md): `Service`, `Prompt`, `Hosts`, `Env`, `Inputs`, `Separator`, `Encoding`, `Placeholder`, `FromCommand`. |

It holds no secret values, no host commands (`FromCommand` only marks that there is
one), no host paths and no raw `.abox` files. Everyone who can read the set can read
it.

## How create consumes it

**Published.** `abox create` resolves the tag to the platform manifest's digest,
pulls the small `config` component and reads `squad.json` from its layers without
running a container. It then hands the set to sbx by digest, so a moving tag cannot
change what is created. Missing or invalid metadata is an error that needs a
republish; there is no fallback to a local build.

**Local.** sbx resolves kit image references only from a registry, even with
`--pull never`; only directory kits can use local images. So `abox create --local`
writes a thin directory kit per component into `<build dir>/create/`: the component's
descriptor plus a recipe that takes the content from the component image.

- Workload: `FROM <ref>:<tag>@<digest>`.
- Mixin: `FROM scratch` and `COPY --from=<ref>:<tag>@<digest> / /`, because sbx keeps
  only the layers a mixin recipe adds on top of its `FROM`.

The tag is needed because BuildKit finds a local image by tag and looks up a
digest-only reference in the registry. sbx builds these kits with its host builder in
the Docker context the build recorded. No recipe of the squad runs again, but the
layers are still transferred into sbx's store, and registry kits may need the
network. Before it replaces a sandbox, create checks that every component image is
still in the image store; if not, run `abox build` again.
