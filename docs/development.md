# Development and local builds

To use a published squad, start with the [README](../README.md); no checkout or
local build is required. This guide is for contributors and maintainers. See
[authoring.md](authoring.md) for squads, kits and workloads, and
[implementation.md](implementation.md) for current behavior and the code map.

## The layout of a project

```text
.
├── teams/
│   └── sample/
│       ├── ac.abox             a squad: its kits, parameters, state and secrets
│       ├── cape.abox           the base of the team's squads
│       ├── secrets.abox        a squad with dummy secrets, to try abox secrets
│       └── sample.workload     the workload: template, tools and scripts
├── kits/                      local kits, such as pi-config and opencode-config
├── abox-cli/
│   ├── agentic-box.slnx
│   ├── src/
│   │   ├── Abox/              command line host: entry point and root command
│   │   ├── Abox.Core/         command slices and shared features
│   │   └── Abox.Infrastructure/  external adapters and generic technical helpers
│   └── test/
│       ├── Abox.Core.Tests/   feature tests with substitutes
│       ├── Abox.Infrastructure.Tests/  adapter tests with fake/synthetic tools
│       ├── Abox.IntegrationTests/  whole CLI and architecture rules
│       ├── Abox.Tests/        host tests
│       ├── Abox.TestUtilities/  shared test helpers
│       └── Abox.Infrastructure.TestUtilities/  helpers using Infrastructure
└── sbox-cli/                  Bun workspace: sbox and sbox-themes packages,
                               bundle helper, package.json and bun.lock
```

The `.abox` files and workloads live in team folders under `teams`, the kits in
`kits`. A kit named in a `.abox` file without a slash or colon is a directory in
`kits`; a name with a slash or colon is a reference sbx resolves.

## Building and running the command line

With the .NET 10 SDK installed, from the repository root:

```sh
cd abox-cli
dotnet build
dotnet test
dotnet run --project src/Abox -- --help
```

`dotnet test` runs every test project of the solution; each is also an xUnit v3
executable that can be run directly. See [synthetic CLI tests](verification/synthetic-cli-tests.md)
for separate discovery/execution commands, isolated output, prerequisites and
platform gaps. Help needs no sbx daemon.

### Checking the tool package locally

From the repository root, pack and install into a fresh isolated directory without
changing your global tools or publishing anything:

```sh
dotnet pack abox-cli/src/Abox/Abox.csproj --configuration Release --output abox-cli/src/Abox/bin/packages
dotnet tool install RwestAgenticBox.Cli --source ./abox-cli/src/Abox/bin/packages --tool-path ./abox-cli/src/Abox/bin/tool-test
./abox-cli/src/Abox/bin/tool-test/abox --help
dotnet tool uninstall RwestAgenticBox.Cli --tool-path ./abox-cli/src/Abox/bin/tool-test
```

`--source` restricts installation to the local package directory. The package and
test installation are under ignored `bin/` output. To check installation-directory
independence, also run the installed command by its absolute path with `--help`
from outside the repository. No sbx daemon is needed for help.

### The tool packages

`dotnet pack` writes one pointer package, `RwestAgenticBox.Cli`, and one package per
runtime identifier, `RwestAgenticBox.Cli.<rid>`, for every Windows, Linux (glibc and musl)
and macOS RID listed in `ToolPackageRuntimeIdentifiers` in `Abox.csproj`. Each is
framework-dependent and compiled ReadyToRun. A portable `RwestAgenticBox.Cli.any`
package without ReadyToRun serves every other platform. `dotnet tool install` picks the
package that matches the host; this needs the .NET 10 SDK or newer. The tool runs on
.NET 10, or rolls forward to the lowest newer major runtime when 10 is not installed
(`RollForward=Major`).

### Publishing the tool package

Publishing is manual for now. From the repository root, with a JFrog identity token:

```powershell
$env:JFROG_USER = '<user>'
$env:JFROG_TOKEN = '<identity token>'
./publish-abox.ps1            # add -DryRun to pack without pushing
```

The script shows the latest stable version on the push feed
`mtiae-agentic-box-local` on `rwest.jfrog.io` and asks whether to bump the patch or the
minor version. It warns about uncommitted changes in `abox-cli` and asks before
continuing, runs the test suites (`-SkipTests` skips them), packs into the ignored
`artifacts/abox/<version>/` and pushes the RID packages before the pointer package.

Users install from the virtual repository `mtiae-cs-nuget-prod`, which lists
`mtiae-agentic-box-local` first, ahead of the nuget.org remote. It caches metadata for
up to 10 minutes, so a new version can take that long to appear there. Readers need
read access to `mtiae-agentic-box-local` as well. Artifactory's v3 service index offers
no flat container, so the script lists versions through the registration resource.

## Local sandbox builds

Sandbox commands need `sbx` 0.51.0 or later on `PATH`, healthy and signed in.
Normal image builds and all creation need host Docker with Buildx and a running
engine. Local builds use the current Docker context's **docker-driver** builder,
regardless of a separately selected `docker-container` or remote publishing builder.
abox does not install dependencies itself.

From the repository root:

```sh
abox build --team sample --no-verify
abox create --team sample --squad ac --local --location "/path/to/project" --root .
abox run --location "/path/to/project"
```

If running from source instead of an installed tool, replace `abox` with
`dotnet run --project abox-cli/src/Abox --`. `--no-verify` skips additional kit
inspection, **not** the image build; recipe-based local kits require it on sbx
0.51.0. `--root` locates team sources separately from the sandbox location and is
accepted by create **only with `--local`**. Installing abox does not install these
team/kit sources.

Local creation consumes a prebuilt workload image and thin runtime kit, not a
fresh workload installation. It still runs the thin sbx wrapper build and may
need frontend/registry dependencies or custom mixin recipe builds; it is not an
offline guarantee. Keep the recorded Docker context/image available. See
[local and published creation](cli-reference.md#creating-local-and-published-sandboxes).

### Metadata-only preparation

`abox build --team <team> --metadata-only --no-personal` prepares sources without
abox's Docker/Buildx or sbx calls. Kit build steps still run and can require tools
of their own. This is not needed to consume a published squad.

`--metadata-only` **replaces the manifest for the selected team/squad/tag** and clears
its previous local image selection, even if inputs are unchanged. Existing
Docker images, thin runtime kits and sandboxes are not deleted. Use a separate
tag if you need to keep a local build selected; otherwise a normal `abox build`
without `--metadata-only` is required before the next `create --local`.
`abox run` continues using existing sandboxes rather than offering metadata-only
preparation as a newer local image build.

## Publishing squads

Set `publishRepository` in each `.workload` file (included workloads use
`mtiae-developer-container-images-local`). Sign in on the host with a JFrog
identity with read/write access to the selected repository, then build and publish:

```sh
docker login rwest.jfrog.io
abox build --team sample --no-personal --no-verify
abox publish --team sample --tag latest
```

**Publishing updates the chosen tag**, `latest` by default, after remote descriptor
verification. Use a dedicated test tag to avoid replacing a shared publication;
build and publish must select the same tag. Each squad has a stable reference,
for example `rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac:latest`.
This example is not evidence that an artifact is available.

Builds containing personal `.abox` configuration, default local builds without
fingerprints, older builds without provenance, and builds whose local sources
have changed must be rebuilt with `--no-personal` before publication. Changing the
resolved workload's repository also requires a rebuild. Link-free source enforcement
and fingerprinting apply only to `--no-personal` builds; ordinary local builds retain
their linked-input support.

`publish` needs Docker/Buildx but not sbx running or signed in. Registry credentials
stay in Docker's host credential store; `abox secrets` does not configure them.
See [publishing details and builder selection](implementation.md#abox-publish---team-name---root-dir---tag-tag---builder-name).

## Building and testing sbox

`sbox` and its commands are TypeScript packages in the Bun workspace `sbox-cli/`.
With [Bun](https://bun.sh) 1.4.4 installed, from `sbox-cli/`:

```sh
bun install --frozen-lockfile
bun test
bun run typecheck
```

`abox build` bundles them through the kits' build steps in a Bun container, so
bundling does not require host Bun; see [the sbox kit](../kits/README.md#sbox).

## The formatting and the hooks

See the [coding standards](../CODING_STANDARDS.md) for architecture conventions and
[formatting and hook setup](../CODING_STANDARDS.md#formatting-and-hooks).
