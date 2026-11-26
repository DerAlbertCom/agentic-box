# Build produces the component images; publish pushes them and builds only the set

`abox build` used to build only the workload image, leaving local kits and the state and config mixins as directories that sbx built at `create --local`, while `abox publish` built every component again from source snapshots. What we tested was a different build from what we shipped, a broken mixin recipe surfaced only during publish, and every build rebuilt everything. Now `abox build` builds every component of a squad (workload, local kits, state mixin, config mixin) as a `linux/amd64` image in the host image store, under its final JFrog name and tagged with its input hash, pins the base image and registry kits by digest, and writes a build record. It rebuilds only components whose inputs changed. `abox publish` refuses stale builds, pushes the component images unchanged (same digests), and then builds and pushes only the set. `create --local` reads the build record and the images and hands sbx thin directory kits that wrap them.

## Considered Options

- **Build the set in `abox build` too, so publish only pushes.** Rejected: the `docker/sandbox-kit:3` frontend resolves set components only from the registry. Named build contexts and local OCI layouts are ignored, so a set cannot reference components that exist only locally.
- **Hand sbx the local component images as OCI kit references with `--pull never`.** Rejected: sbx fetches the manifest of every OCI kit reference from the registry, even with `--pull never`. Only directory kits can use local images, hence the thin directory kits.
- **Thin mixin kits as `FROM <mixin image>`.** Rejected: sbx keeps only the layers a mixin recipe adds on top of its `FROM`, so the mixin's files are lost. Thin mixin kits are `FROM scratch` plus `COPY --from=<image> / /`; the thin workload kit stays `FROM <image>`.
- **Keep building components at publish.** Rejected: that is what made published images differ from tested ones.

## Consequences

- Publish keeps exactly one `buildx build`: the set, which is composition only and runs no recipe.
- A build is stale when its sources changed (including the repository folder of a kit with a build step), a component image left the image store or was replaced, or the base image or a registry kit tag now resolves to another digest. Publish checks all selected squads before its first push and asks for `abox build`.
- The config mixin dates the build, so it is rebuilt whenever any other component is. Files outside a kit's folder that its build step reads are not part of its input hash; use `abox build --rebuild` after changing them.
- `--metadata-only`, `--no-personal`, `--no-verify` (the `sbx kit inspect` check) and personal configurations are gone: building the mixins as images replaces the declaration-only check, and every build is publishable. Local kits accept only create-phase arguments, because their images are built without arguments.
- Only `abox build` and `abox publish` read team sources; outside the repository they find them through the `sourceRoot` user setting.
