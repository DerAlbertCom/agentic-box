# Coding standards

## Simplicity

Keep complexity low. Favor readability and ease of change over cleverness, brevity
or dogmatic application of patterns.

- Keep control flow and data flow straightforward. Reduce nesting and branching
  when it makes behavior easier to follow, for example with guard clauses.
- Make intent explicit through clear names and direct operations rather than
  clever indirection.
- Add configuration only for real needs, not hypothetical future requirements.
- **Partial classes:** prefer one cohesive class in one file. Avoid splitting
  handwritten code across partial classes to manage size or separate responsibilities;
  needing that split is a signal to reassess the single responsibility principle
  and extract cohesive collaborators instead. Reserve partial classes for concrete
  language or tooling requirements, such as source-generated members, and keep the
  handwritten class cohesive.

## Architecture

The rules below guide code organization and are recorded, with the options we rejected,
in [ADR 0003](docs/adr/0003-external-tools-behind-use-case-interfaces.md) and
[ADR 0004](docs/adr/0004-core-and-infrastructure-projects.md);
[the implementation code map](docs/implementation.md#the-code) documents the current
directory layout.

- **Projects:** `abox-cli/src` holds three projects. `Abox.Core` holds only `Features/`
  and references nothing in the solution, nor YamlDotNet. `Abox.Infrastructure` holds
  the adapters and the generic technical helpers and references Core. `Abox` (the host,
  assembly `abox`) holds only `Program.cs` and `Cli/` (the root command and the
  composition) and references both. Namespaces follow folders: `Abox.Features.<Command>`,
  `Abox.Features.Shared.<UseCase>`, `Abox.Infrastructure.<Tool>`, `Abox.Cli`.
- **Command slices:** use vertical slice architecture. Keep a command, all its
  subcommands, slice registration and command-specific implementation together in
  `Features/<Command>`. Code used by only one command lives in that command's slice.
- **Shared features:** put everything shared by commands in `Features/Shared/<UseCase>`,
  one folder per shared feature, named by use case (such as `Sandboxes`, `Squads`,
  `Builds`, `UserSecrets`, `Https`, `Preflight`), never by tool. There is no
  `Shared/Sbx` or `Shared/Docker`. Shared features never import a command slice.
- **Infrastructure:** `Abox.Infrastructure` holds only what is external: one folder per
  external tool (`Sbx`, `Docker`, `Dotnet`, `Herdr`, `Shell`) with that tool's adapters,
  plus, in its root, the generic technical helpers (system console, process runner,
  YamlDotNet parser, protected staging, secret redaction) and
  `InfrastructureServiceCollection`, its registration. The console abstraction,
  `AboxException` and the temporary-directory helper live in the shared `Common`
  feature in Core. External means another program, or a file another
  tool owns, such as sbx's `credentials.yaml` or the .NET user-secrets store. abox's own
  files (`abox.yaml`, `abox.user.yaml`, `.abox`, `.workload`, its state folder) are not
  external; features read them with the generic helpers.
- **Use-case interfaces:** every use of an external tool goes through a small, cohesive
  interface (one use case, a few methods) that lives in the shared feature it serves,
  next to its own parameter and result models in domain terms. No command names, flags,
  argument lists, exit codes, JSON shapes, embedded scripts or process results cross
  it; the adapter in `Infrastructure/<Tool>` maps them. A new external use case adds a
  small interface; it does not widen an existing one. One adapter may implement several
  interfaces when that is clearly simpler, but one adapter per interface is preferred.
- **One sbx invoker:** one invoker in `Abox.Infrastructure/Sbx` owns the `sbx` executable
  name, the minimum version check, captured execution, JSON handling and the failure
  headline; every sbx adapter calls `sbx` through it. Process arguments are always a
  list, never a shell string.
- **Dependency direction:** no command slice depends on another; slices meet only
  through interfaces and models in shared features, and shared features never depend
  on a command slice. Adapters depend on `Features/Shared` interfaces and models. The
  project references keep Core free of Infrastructure and the host; the ArchUnitNET
  rules in `Abox.IntegrationTests` check the rest.
- **Visibility:** production projects meet only through `public` types: the use-case
  interfaces and their models, `AboxException`, `IConsole`, the YAML parser and node
  tree, the command types the root command collects, the command-line types the host
  and the process runner use (`CommandLine`, `CommandHandling`, `TrailingArguments`,
  `Verbosity`), the two registration methods and `CreateSystemConsole`, which gives the
  entry point its console. Adapters, `SystemConsole`, `ProcessOutput` and everything
  else stay `internal`. Grant `InternalsVisibleTo` only to test code: the test
  projects, the two TestUtilities projects (their helpers build internal services)
  and `DynamicProxyGenAssembly2`, the assembly in which NSubstitute builds substitutes
  of internal interfaces.
- Runtime folder selections and their parsing/matching live in the shared
  `Configuration` feature, not the shared copy orchestration. Portable store-ID
  validation lives in the shared `Naming` feature (`UserSecretId`); both configuration
  parsing and the sbx adapter validate with it. Thus `Configuration` does not import
  `UserSecrets` or know its orchestration, while the copy flow can consume both.
- Each launcher is one class behind `ILauncher`, which lives in the shared `Launchers`
  feature; launchers that drive an external tool, such as Herdr, are adapters in
  `Infrastructure/<Tool>`.
- Each command is a class that the container builds and that takes its dependencies
  in its constructor; nothing looks the commands up by hand. The root command holds
  every command the container has, one per slice.
- Each layer registers its own services through one public extension method:
  `AddAboxCore` registers the shared services and calls one method per slice, which adds
  its command and any slice-specific services; `AddAboxInfrastructure` registers every
  adapter against its use-case interface, the process runner, the YAML parser and the
  launchers that drive an external tool. The host's `AddAbox` calls both, so adding a
  service never changes the host.
- The entry point builds a plain service provider, not a host: it does not load
  `appsettings.json` from the working directory. Commands defer configuration-dependent
  dependencies until their actions run, so help, `exec` and `pipe` need no user configuration.
- The services take their dependencies in their constructor, so the tests can hand them
  substitutes of the use-case interfaces (NSubstitute in the test projects).
- YAML is parsed behind `IYamlParser` (its only implementation, on YamlDotNet, is in
  Infrastructure) and read in one place, Core's strict `YamlReader`, so every file is
  read the same way and an unknown key is an error everywhere. Loaders of abox's own
  files take the parser as a dependency.

## SOLID principles

Use SOLID to support simplicity and ease of change. Prefer deep modules: hide
necessary complexity behind a small, coherent interface rather than pushing it
onto callers. Apply these principles within the vertical slices and shared
features described above, not as a reason to add layers or an interface for every
class.

- **Single responsibility:** give each module a cohesive responsibility and reason
  to change. Keep related knowledge together; this does not mean tiny classes.
- **Open/closed:** where real variation exists, support it through adapters at a
  deliberate seam rather than spreading conditional logic across callers. Do not
  build speculative extension frameworks for possible future requirements.
- **Liskov substitution:** every adapter must honor its interface's contract,
  including invariants, ordering constraints and error modes, so callers need no
  adapter-specific workarounds.
- **Interface segregation:** expose only what callers need to use a module
  correctly. Keep implementation details hidden without fragmenting a deep module
  into shallow, pass-through interfaces.
- **Dependency inversion:** keep application orchestration independent of technical
  details; put those details in adapters behind stable interfaces at real seams.
  Pass dependencies in, using abstractions for actual variation rather than
  requiring a separate interface for every concrete implementation.

## Testing

- Write behavior-driven unit tests: verify observable behavior and results through
  a module's interface, not its implementation details.
- Do not write tests merely to assert configuration values, constants or trivial
  implementations. Test meaningful configuration-dependent behavior instead of
  restating configuration values.
- Mark each test's phases explicitly with `// Arrange`, `// Act` and `// Assert`
  comments.
- Put a test in the test project of what it needs, under `abox-cli/test`:
  `Abox.Core.Tests` for features with substitutes (references only Core and
  `Abox.TestUtilities`), `Abox.Infrastructure.Tests` for adapters and the YAML parser,
  `Abox.IntegrationTests` for tests that need real Core and real Infrastructure
  together (the whole CLI through the CLI test application, including the composition
  root, real YAML files, the architecture rules), and `Abox.Tests` for the host's own
  tests that need no CLI test application. Shared helpers live in
  `Abox.TestUtilities`, which references only Core; shared helpers that need
  Infrastructure live in `Abox.Infrastructure.TestUtilities`, which only
  `Abox.Infrastructure.Tests` and `Abox.IntegrationTests` reference. Every helper has one
  owning project: test projects do not compile in each other's files with linked
  `<Compile>` items. Every test project uses the `Abox.Tests` namespace.
- `test/Directory.Build.props` holds the settings, test packages and global usings every
  test project shares; a project file lists only its references and what is its own.
- Mirror the source project's folders in its test project: keep each command and its
  subcommands together under `Features/<Command>`, group shared-feature tests by shared
  feature under `Features/Shared/<UseCase>`, and put adapter tests under `<Tool>/` in
  `Abox.Infrastructure.Tests`. Support fixtures tied to one area live next to that
  area's tests.
- Feature tests substitute the small use-case interfaces with NSubstitute; they do not
  fake tools. Adapter tests verify tool-specific behaviour (arguments, JSON, exit codes,
  scripts) against the fake sbx and shell setup and the synthetic tools. Command
  behaviour stays tested end to end through the CLI test application.

## TypeScript

The in-sandbox CLI `sbox` and its commands are TypeScript on Bun, in one Bun
workspace, `sbox-cli/`, with one lockfile (`sbox-cli/bun.lock`).

- Put each package directly in `sbox-cli/`: `sbox-cli/sbox` is the core (package
  `sbox`), and each command is `sbox-cli/sbox-<command>`, where `<command>` is exactly
  the command name, such as `sbox-cli/sbox-themes` for `sbox themes`. The workspace
  picks up every `sbox-*` folder.
- Commands depend on the core only through the command API in `sbox-cli/sbox/src/api.ts`,
  with type-only imports (`import type { Command } from "sbox"`), so nothing of
  the core is bundled into a command. The core's second export, `sbox/testing`, is for
  tests only (see below). Keep that API small; change it only in
  backward-compatible ways, because installed commands and the core evolve apart.
- A command's module default-exports its `Command` and checks it with
  `satisfies Command`. It writes through `ctx.stdout`/`ctx.stderr` and reads
  `ctx.env` and `ctx.paths`, never `process` directly, so tests can run it.
- Parse options with `util.parseArgs`. Add a CLI or TUI library only when a command
  needs one.
- Every package is bundled with `bun build --target=bun` into one file by its kit's
  build step, so the sandbox never runs `bun install` and nothing built is committed.
- A command's package declares the data folders installed next to its module once, in
  its `package.json` under `"sbox": { "data": [...] }`. The command home and the kit's
  build step (through `sbox-cli/data-folders`) both read it; neither lists the folders.
- Test through the entry point (`sbox(argv, io)`) with `bun test` and the command
  home, `useCommandHome()` from the core's testing entry (`sbox/testing`), and assert
  on output, exit codes and files. Do not write another temporary home, output capture
  or install. Import `sbox/testing` in tests only, never in a command's source, so
  nothing of the core is bundled into a command. Keep tests in a `test` folder next to
  `src`, mirroring it, with the same `// Arrange`, `// Act` and `// Assert` comments.
- `tsc` checks the types in strict mode: `bun run typecheck`. Run it and `bun test`
  from `sbox-cli/`; CI runs both.

## Domain language

Follow the [domain documentation rules](docs/agents/domain.md) for vocabulary and
decisions, including the root `GLOSSARY.md` when present.

## Formatting and hooks

The code is formatted with [CSharpier](https://csharpier.com), which comes with the
repository as a local tool:

```sh
dotnet csharpier format abox-cli  # format the code
dotnet csharpier check abox-cli   # check the formatting
```

The hook in `.githooks/pre-commit` checks the formatting before every commit. Enable
the hooks of the repository once:

```sh
git config core.hooksPath .githooks
```
