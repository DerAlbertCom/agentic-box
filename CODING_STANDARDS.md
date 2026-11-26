# Coding standards

## Simplicity

Keep complexity low. Favor readability and ease of change over cleverness or brevity.

- Keep control flow and data flow straightforward. Use guard clauses to reduce nesting.
- Make intent explicit through clear names and direct operations.
- Add configuration only for real needs, not for hypothetical ones.
- Keep one cohesive class in one file. Use partial classes only where the language or a
  tool requires them, such as source-generated members. Wanting to split a class across
  files is a sign to extract a collaborator instead.

## Architecture

The reasons for these rules, and the options we rejected, are in
[ADR 0003](docs/adr/0003-external-tools-behind-use-case-interfaces.md),
[ADR 0004](docs/adr/0004-core-and-infrastructure-projects.md) and
[ADR 0005](docs/adr/0005-commands-bind-parameters-handlers-run-them.md). The
[code map](docs/implementation/code-map.md) shows the current layout.

### Projects and folders

`abox-cli/src` holds three projects. Namespaces follow folders.

| Project | Holds | References |
|---|---|---|
| `Abox.Core` | only `Features/` | nothing in the solution, and not YamlDotNet |
| `Abox.Infrastructure` | the adapters and the generic technical helpers | Core |
| `Abox` (the host, assembly `abox`) | `Program.cs` and `Cli/`: the root command and the composition | both |

- **Command slices:** a command, its subcommands, its registration and everything only
  that command uses live together in `Features/<Command>`.
- **Shared features:** what several commands use lives in `Features/Shared/<UseCase>`,
  named by use case (`Sandboxes`, `Squads`, `Builds`, `UserSecrets`, `Https`,
  `Preflight`), never by tool. There is no `Shared/Sbx` or `Shared/Docker`.
- **Infrastructure:** one folder per external tool (`Sbx`, `Docker`, `Dotnet`, `Herdr`,
  `Shell`) with that tool's adapters. The generic helpers (system console, process
  runner, YamlDotNet parser, protected staging, secret redaction) sit in its root.
- **External** means another program, or a file another tool owns, such as sbx's
  `credentials.yaml` or the .NET user-secrets store. abox's own files (`abox.yaml`,
  `abox.user.yaml`, `.abox`, `.workload`, its state folder) are not external; features
  read them with the generic helpers.

### Dependencies

- No command slice depends on another. Slices meet only through interfaces and models in
  shared features. Shared features never import a command slice.
- Adapters depend on the interfaces and models in `Features/Shared`.
- Core stays free of Infrastructure and the host. The project references enforce this;
  the ArchUnitNET rules in `Abox.IntegrationTests` check the rest.
- Production projects meet only through `public` types: the use-case interfaces and
  their models, and the few types the host needs to compose and run commands. Adapters,
  handlers, parameters records and everything else stay `internal`.
- Grant `InternalsVisibleTo` only to test code: the test projects, the two TestUtilities
  projects and `DynamicProxyGenAssembly2` (NSubstitute).

### External tools

- Every use of an external tool goes through a use-case interface: small (one use case,
  a few methods), in the shared feature it serves, with its own parameter and result
  models in domain terms.
- No command names, flags, argument lists, exit codes, JSON shapes, embedded scripts or
  process results cross that interface. The adapter in `Infrastructure/<Tool>` maps them.
- A new external use case adds a small interface; it does not widen an existing one.
  Prefer one adapter per interface.
- One sbx invoker in `Abox.Infrastructure/Sbx` owns the `sbx` executable name, the
  minimum version check, captured execution, JSON handling and the failure headline.
  Every sbx adapter calls `sbx` through it.
- Process arguments are always a list, never a shell string.
- Each launcher is one class behind `ILauncher` in the shared `Launchers` feature. A
  launcher that drives an external tool, such as Herdr, is an adapter in
  `Infrastructure/<Tool>`.
- YAML is parsed behind `IYamlParser` and read in one place, Core's strict `YamlReader`,
  so an unknown key is an error in every file.

### Commands and handlers

- A command describes only its syntax: options, arguments, subcommands, help. It derives
  from `AboxCommand<TParameters>`; its `BindAsync` maps the parse result to a
  `<Command>Parameters` record of effective values. Checks that depend only on the
  options throw `AboxException` there.
- The work runs in a `<Command>Handler`, an `ICommandHandler<TParameters>` that takes its
  services in its constructor and is registered in its slice.
- `CommandRunner` is the only module that runs a command. It binds the parameters,
  resolves the handler only when the action runs, and turns abox errors into exit code 1
  and cancellation into 130. No action handles these itself.
- Preflight stays in the handlers, in each command's own order.
- A command's constructor takes only the `CommandRunner`: never a service its action
  needs, never a `Lazy<T>`. `CommandConstructorTests` enforces this with an allow-list;
  a new entry there is a reviewed decision.
- Options get their user configuration defaults only from `InvocationDefaults`, during
  binding: the sandbox location (a `SandboxLocation`, which carries its folder
  configuration, loaded once), the team or teams, the validated tag and the arguments
  after `--`. Commands that work on a sandbox location use the shared `LocationOption`.
  No handler resolves `--location`, loads the folder configuration or reads a user
  configuration default itself.

### Composition

- Register every service as scoped. Only what must outlive one invocation is a
  singleton: today only the console.
- Each layer registers its own services through one public extension method:
  `AddAboxCore` (which calls one method per slice) and `AddAboxInfrastructure`. The
  host's `AddAbox` calls both, so adding a service never changes the host.
- The entry point builds a plain service provider with `ValidateScopes` and
  `ValidateOnBuild`, opens one scope per process run and resolves the root command
  from it.
- The user configuration is loaded only once an action runs, so help, `exec` and `pipe`
  need none.
- Services take their dependencies in their constructor, so tests can substitute them.

## SOLID principles

Use SOLID to support simplicity, not to add layers. Prefer deep modules: hide necessary
complexity behind a small, coherent interface.

- **Single responsibility:** one cohesive responsibility and reason to change per
  module. This does not mean tiny classes.
- **Open/closed:** where real variation exists, support it with adapters at a deliberate
  seam. Do not build extension points for possible future requirements.
- **Liskov substitution:** every adapter honors its interface's contract, including
  ordering and error modes, so callers need no adapter-specific workarounds.
- **Interface segregation:** expose only what callers need, without fragmenting a deep
  module into pass-through interfaces.
- **Dependency inversion:** keep orchestration independent of technical details. Use an
  abstraction for actual variation, not one interface per class.

## Testing

- Test observable behavior through a module's interface, not its implementation.
- Do not write tests that only restate configuration values, constants or trivial code.
- Mark the phases of every test with `// Arrange`, `// Act` and `// Assert` comments.
- Feature tests substitute the use-case interfaces with NSubstitute; they do not fake
  tools. Adapter tests check tool-specific behavior (arguments, JSON, exit codes,
  scripts) against the fake sbx and shell setup. Command behavior is tested end to end
  through the CLI test application.

Put a test in the project of what it needs, under `abox-cli/test`:

| Project | For | References |
|---|---|---|
| `Abox.Core.Tests` | features, with substitutes | Core, `Abox.TestUtilities` |
| `Abox.Infrastructure.Tests` | adapters and the YAML parser | Infrastructure, both TestUtilities |
| `Abox.IntegrationTests` | real Core with real Infrastructure: the whole CLI, the composition, real YAML files, the architecture rules | everything |
| `Abox.Tests` | the host's own tests that need no CLI test application | the host |
| `Abox.TestUtilities` | shared helpers | Core only |
| `Abox.Infrastructure.TestUtilities` | shared helpers that need Infrastructure | used only by the Infrastructure and integration tests |

- Mirror the source folders: `Features/<Command>`, `Features/Shared/<UseCase>`, and
  `<Tool>/` in `Abox.Infrastructure.Tests`. Fixtures for one area live next to its tests.
- Every helper has one owning project; do not link files between test projects.
- Every test project uses the `Abox.Tests` namespace.
- `test/Directory.Build.props` holds the settings, packages and global usings all test
  projects share. A project file lists only its references and what is its own.

## TypeScript

`sbox`, the command line inside a sandbox, and its commands are TypeScript on Bun, in
one Bun workspace, `sbox-cli/`, with one lockfile.

- Each package sits directly in `sbox-cli/`: `sbox-cli/sbox` is the core, and each
  command is `sbox-cli/sbox-<command>`, named exactly after the command.
- Commands depend on the core only through the command API in
  `sbox-cli/sbox/src/api.ts`, with type-only imports (`import type { Command } from
  "sbox"`), so nothing of the core is bundled into a command. Keep that API small and
  change it only in backward-compatible ways: installed commands and the core evolve
  apart.
- A command's module default-exports its `Command` and checks it with
  `satisfies Command`. It writes through `ctx.stdout`/`ctx.stderr` and reads `ctx.env`
  and `ctx.paths`, never `process`, so tests can run it.
- Parse options with `util.parseArgs`. Add a CLI or TUI library only when a command
  needs one.
- Each kit's build step bundles its package into one file with
  `bun build --target=bun`. The sandbox never runs `bun install`, and nothing built is
  committed.
- A command declares the data folders installed next to its module once, in its
  `package.json` under `"sbox": { "data": [...] }`. Nothing else lists them.
- Test through the entry point (`sbox(argv, io)`) with `bun test` and the command home,
  `useCommandHome()` from `sbox/testing`. Assert on output, exit codes and files. Do not
  write another temporary home, output capture or install. Import `sbox/testing` only in
  tests.
- Keep tests in a `test` folder next to `src`, mirroring it, with the same `// Arrange`,
  `// Act` and `// Assert` comments.
- Run `bun run typecheck` (strict `tsc`) and `bun test` from `sbox-cli/`; CI runs both.

## Domain language

Use the vocabulary of the root [GLOSSARY.md](GLOSSARY.md) and follow the
[domain documentation rules](docs/agents/domain.md).

## Formatting and hooks

The code is formatted with [CSharpier](https://csharpier.com), a local tool of the
repository:

```sh
dotnet csharpier format abox-cli  # format the code
dotnet csharpier check abox-cli   # check the formatting
```

The hook in `.githooks/pre-commit` checks the formatting before every commit. Enable the
hooks once:

```sh
git config core.hooksPath .githooks
```
