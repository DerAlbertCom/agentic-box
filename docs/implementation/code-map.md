# Code map

Where things live. The rules behind this layout are in the
[coding standards](../../CODING_STANDARDS.md) and in
[ADR 0003](../adr/0003-external-tools-behind-use-case-interfaces.md),
[ADR 0004](../adr/0004-core-and-infrastructure-projects.md) and
[ADR 0005](../adr/0005-commands-bind-parameters-handlers-run-them.md).

## `abox-cli/src`

| Project | Holds | References |
|---|---|---|
| `Abox` | the host: `Program.cs` and `Cli/` (root command, composition). Assembly `abox`, package `RwestAgenticBox.Cli` | both others |
| `Abox.Core` | `Features/`: command slices and shared features | nothing in the solution |
| `Abox.Infrastructure` | adapters for external tools and generic technical helpers | `Abox.Core` |

### Command slices: `Abox.Core/Features/<Command>`

Each slice holds a command, its parameters record, its handler and everything only that
command uses.

| Folder | Command |
|---|---|
| `Build` | `abox build`: component images, kit build step, workload/state/config generators, the embedded CA bundle in `Assets/` |
| `Publish` | `abox publish`: freshness checks, pushes, the set and its promotion |
| `Create` | `abox create` |
| `Run` | `abox run` and the `shell` launcher |
| `Secrets` | `abox secrets init`, `update`, `list`, `set`, `remove`; reading a squad's secrets without a sandbox |
| `UserSecrets` | `abox user-secrets` |
| `Ports` | `abox ports` |
| `Prune` | `abox prune` |
| `Sandbox` | the hidden `abox exec` and `abox pipe` |

`CoreServiceCollection.cs` registers the shared services and each slice.

### Shared features: `Abox.Core/Features/Shared/<UseCase>`

A shared feature is named by what it does and owns the use-case interfaces for the
external tools it needs.

| Folder | Use case |
|---|---|
| `CommandLine` | how a command runs: binding, `CommandRunner`, exit codes, `--location`, arguments after `--` |
| `Configuration` | abox directories, the user configuration, the folder configuration (`FolderConfig`) |
| `Yaml` | `IYamlParser`, the strict reader of abox's own files, the merge of `abox.yaml` and `abox.user.yaml` |
| `Squads` | `.abox` and `.workload` files, the `base` chain, squad and source-root resolution, state path rules, kit descriptors and their `secrets.yaml` |
| `Builds` | the build record and build store; interfaces for building, inspecting and saving local images |
| `KitBuildScripts` | `IKitBuildScript`: runs a local kit's `build` file |
| `Registry` | interfaces for pushing, tagging and reading registry images; image reference rules |
| `Sandboxes` | finding a folder's sandbox, `SandboxCreator`, sandbox records, legacy names, published squad metadata; interfaces for lifecycle, exec, inspection, MCP registration and kit source trust |
| `Launchers` | `ILauncher`, `LauncherSession`, the launcher registry |
| `Credentials` | kit credential requests, a squad's secrets (`SquadSecrets`), identity, the input rules and remembered inputs, the guided secrets setup and the create-time check |
| `GitHubTokens` | the fine-grained token of the `abox-github-enterprise` service: the permissions and prefilled link it needs, the check of a pasted token, the expiry warning; `IGitHubTokenCheck` |
| `UserSecrets` | selection, deny list, project discovery, endpoint rewriting, the copy flow |
| `ApplicationPorts` | static port discovery, the port plan, publishing |
| `Https` | sandbox HTTPS activation, refresh and launch readiness |
| `Preflight` | the sbx checks before a command, install instructions |
| `SandboxCleanup` | `ISandboxCleanup` for `abox prune` |
| `Naming` | sandbox, image and squad build names; TCP port and Windows device-name rules |
| `Common` | `IConsole`, `AboxException`, temporary directories |

### Adapters: `Abox.Infrastructure/<Tool>`

| Folder | Tool |
|---|---|
| `Sbx` | every `sbx` call. One adapter per use-case interface, all through `SbxInvoker`, which owns the executable name, the minimum version, JSON handling and the failure headline. Also the embedded `*.py` and `*.sh` scripts that run in the sandbox |
| `Docker` | every `docker` call: Buildx builds, image store, registry digests, push, published image reading |
| `Dotnet` | `dotnet dev-certs` export and the host's .NET user-secrets store layout |
| `Shell` | kit `build` scripts, a folder's real path, programs on `PATH` |
| `GitHub` | the GitHub REST API: checking a token with `GET /user` and reading its expiry |
| `Herdr` | the `herdr-app` launcher |

The project root holds the generic helpers: `ProcessRunner` (processes from argument
lists), `ProcessOutput` (verbose diagnostics and the sensitive output policy),
`SecretRedactor`, `ProtectedTemporaryDirectory`, `YamlDotNetParser`, `SystemConsole` and
its key-by-key input (`TerminalInput`, `MultiSelection`).
`InfrastructureServiceCollection.cs` registers all adapters.

## `abox-cli/test`

One test project per source project, mirroring its folders.

| Project | Tests |
|---|---|
| `Abox.Core.Tests` | features with substitutes; references only Core |
| `Abox.Infrastructure.Tests` | adapters against fake `sbx`, shell and synthetic tools |
| `Abox.IntegrationTests` | the whole CLI with real Core and Infrastructure, real YAML files, and the architecture rules |
| `Abox.Tests` | the host: the root command and the executable |
| `Abox.TestUtilities`, `Abox.Infrastructure.TestUtilities` | shared helpers |

## `sbox-cli`

The Bun workspace of `sbox`, the command line inside a sandbox.

| Path | Holds |
|---|---|
| `sbox/` | the core: routes `sbox <command>` to an installed command, help, exit codes; the command API (`src/api.ts`) and the testing entry (`sbox/testing`) |
| `sbox-themes/` | the `themes` command: palettes in `themes/`, app templates in `apps/`, the login script in `profile/` |
| `bundle` | bundles a workspace module with the pinned Bun in a container |
| `data-folders` | prints the data folders a command's package declares |

The kits that install these are described in [kits/README.md](../../kits/README.md).
