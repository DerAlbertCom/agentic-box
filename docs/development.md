# Development and local builds

This guide is for contributors and maintainers. To use a published squad, the
[README](../README.md) is enough. See [authoring.md](authoring.md) for squads, kits and
workloads, and [implementation.md](implementation.md) for how abox is built.

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

Help needs no sbx daemon.

### Running the tests

`dotnet test` in `abox-cli` runs the four test projects: `Abox.Core.Tests`,
`Abox.Infrastructure.Tests`, `Abox.IntegrationTests` and `Abox.Tests`. Each is an
xUnit v3 executable, so after `dotnet build` you can also run one directly, as CI does:

```sh
dotnet abox-cli/test/Abox.Core.Tests/bin/Debug/net10.0/Abox.Core.Tests.dll -noColor
```

Add `-class Abox.Tests.<ClassName>` for one class (every test project uses the
`Abox.Tests` namespace), `-list tests/json` to list the tests without running them, or
`-result-xml <file>` for a report.

- The tests use synthetic certificates, temporary folders and fake `sbx`, Docker and
  shell tools. They do not touch host certificates, real secrets, registries or
  sandboxes.
- Unix shell tests need `sh` and `bash` on `PATH`; Windows cases use PowerShell and the
  `sh` of Git for Windows. Tests for another platform are skipped.
- Keep the `Fixtures/` folder next to a test executable when you move build output; the
  tests read their inputs only from there.
- Certificate tests write below the executable's folder, which must not sit under a
  symbolic link or junction.
- Leave `ABOX_DISPOSABLE_RECEIVER_CHECKS` unset. It enables a test that needs a
  dedicated disposable Linux environment.

### Checking the tool package locally

From the repository root, pack and install into a fresh isolated directory without
changing your global tools or publishing anything:

```sh
dotnet pack abox-cli/src/Abox/Abox.csproj --configuration Release --output abox-cli/src/Abox/bin/packages
dotnet tool install RwestAgenticBox.Cli --source ./abox-cli/src/Abox/bin/packages --tool-path ./abox-cli/src/Abox/bin/tool-test
./abox-cli/src/Abox/bin/tool-test/abox --help
dotnet tool uninstall RwestAgenticBox.Cli --tool-path ./abox-cli/src/Abox/bin/tool-test
```

`--source` restricts installation to the local package directory. Both folders are
ignored `bin/` output.

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

Sandbox commands need `sbx` 0.54.0 or later on `PATH`, healthy and signed in. Builds
and creation need host Docker with Buildx and a running engine. A build always uses the
docker-driver builder of the current Docker context, whatever other builder is selected.

From the repository root:

```sh
abox build --team sample
abox create --team sample --squad ac --local --location "/path/to/project"
abox run --location "/path/to/project"
```

When running from source, replace `abox` with
`dotnet run --project abox-cli/src/Abox --`. Only `build` and `publish` read the team
and kit sources; `create --local` uses the local build. Outside the repository, `build`
and `publish` find the sources through the user setting `sourceRoot`; see
[user settings](configuration.md#user-settings).

`abox build` builds one image per component (workload, local kits, state and config
mixins) and rebuilds only the components whose inputs changed. Use
`abox build --rebuild` to build every component again, for example after changing
files outside a kit's folder that its build step reads (such as `sbox-cli/`).

`create --local` uses the component images of the build, so keep them and the Docker
context they were built in. It can still fetch registry kits, so it does not work
offline. See
[local and published creation](cli-reference.md#creating-local-and-published-sandboxes).

## Publishing squads

Set `publishRepository` in each `.workload` file (included workloads use
`mtiae-developer-container-images-local`). Sign in on the host with a JFrog
identity with read/write access to the selected repository, then build and publish:

```sh
docker login rwest.jfrog.io
abox build --team sample
abox publish --team sample --tag latest
```

**Publishing moves the chosen tag**, `latest` by default. Use a test tag to avoid
replacing a shared publication; build and publish must use the same tag. Each squad has
a stable reference, for example
`rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac:latest`.

Publish pushes the component images `abox build` made, unchanged, and builds only the
set. It refuses a stale build: changed sources, a component image that is gone or was
replaced, or a base image or registry kit tag that now resolves to another digest. Run
`abox build --team <team> --tag <tag>` again before publishing. Build inputs must be
ordinary files and directories, not symbolic links.

`publish` needs Docker/Buildx but not sbx running or signed in. Registry credentials
stay in Docker's host credential store; `abox secrets` does not configure them.
See [how build and publish work](implementation/build-and-publish.md).

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
