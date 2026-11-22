# External tools behind use-case interfaces

The root of `abox-cli` mixed three kinds of module: use cases shared by several commands, wrappers of external tools, and code used by only one command. External tools were the worst of it. Every feature that talked to `sbx` widened one wide `ISbxClient`, which also grew HTTPS, port-relay, user-secret, kit-trust and credential knowledge, and features used `docker`, `dotnet dev-certs`, `herdr`, `bash` and a kit's `build` script directly or through tool-shaped classes, so flags, exit codes and process results leaked into them. So the project is organized by what a module is, and every external tool sits behind small interfaces named by use case:

- **Root.** The root of the CLI project holds only `Program.cs`, the project file, `AboxException`, `Assets/`, `Cli/`, `Features/` and `Infrastructure/`. Namespaces follow folders: `Abox.Features.<Command>`, `Abox.Features.Shared.<UseCase>`, `Abox.Infrastructure.<Tool>`.
- **Command slices.** Every command is a vertical slice in `Features/<Command>`. Code used by only one command lives in that command's slice.
- **Shared features.** Everything shared by commands lives in `Features/Shared/<UseCase>`, named by use case, never by tool: there is no `Shared/Sbx` or `Shared/Docker`. An interface that only one command uses still gets a shared folder named by its use case, such as sandbox cleanup for `prune`.
- **Infrastructure.** `Infrastructure/` holds only what is external: one folder per external tool (`Sbx`, `Docker`, `Dotnet`, `Herdr`, `Shell`) with that tool's adapters, plus the generic technical helpers (console, process runner, YAML reader, temporary directories, secret redaction). External means another program (`sbx` and the scripts it runs in the sandbox, `docker`, `dotnet dev-certs`, `herdr`, `bash`, a kit's `build` script) or a file another tool owns (sbx's `credentials.yaml`, the .NET user-secrets store). abox's own files (its state folder, `abox.yaml`, `abox.user.yaml`, `.abox` and `.workload`) and the project files abox scans are not external; features read them with the generic helpers.
- **Interfaces.** Every use of an external tool goes through a small, cohesive interface: one use case, a few methods. It lives in the shared feature whose use case it serves, next to its own parameter and result models, which are domain types. No command names, flags, argument lists, exit codes, JSON shapes, embedded scripts or process results cross it; adapters map them internally. Text a user must see, such as the manual command for setting a custom secret, is returned as a domain value. One adapter class may implement several interfaces when that is clearly simpler, but one adapter per interface is preferred.
- **One sbx invoker.** A single invoker in `Infrastructure/Sbx` owns the `sbx` executable name, the minimum version check, captured execution, JSON handling and the failure headline. The sbx adapters all call `sbx` through it. Running a command in a sandbox is one interface; its adapter adds the HTTPS bootstrap privately.
- **Dependency direction.** Features depend on `Features/Shared` interfaces and the generic helpers, never on an `Infrastructure/<Tool>` type. `Infrastructure/<Tool>` depends on `Features/Shared` interfaces and models, never on a command slice. `Cli` depends on features, never the other way round, and only `Cli` wires adapters to interfaces. An architecture test enforces these rules.

## Considered Options

- **Keep one wide sbx facade (`ISbxClient`).** Rejected: every new sbx use case widens it, it is the main hot spot of recent changes, each feature depends on members it does not use, and its pass-through members expose sbx details to features.
- **Feature-owned tool adapters,** each slice wrapping the tools it calls. Rejected: shared use cases would wrap the same tool several times, and the `sbx` executable, version check and failure headline would be duplicated instead of living in one place.
- **Interfaces owned by command slices.** Rejected: an interface used by several commands has no single owning slice, and adapters would have to import slices, reversing the dependency direction.
- **Interfaces in tool-named shared folders** (`Features/Shared/Sbx`, `Features/Shared/Docker`). Rejected: the folder name says which tool is called, not what is done, and invites one wide tool-shaped interface again.

## Consequences

- A new external use case adds a small interface in its shared feature and an adapter in `Infrastructure/<Tool>`; it does not widen an existing interface.
- There are more, smaller interfaces and adapter classes, and `Cli` wires each of them.
- Feature tests substitute the small interfaces with NSubstitute instead of faking tools. Tool-specific behaviour is tested at the adapter, in the test project under `Infrastructure/<Tool>`, against the fake sbx and shell setup and the synthetic tools.
- Swapping or changing a tool touches only its `Infrastructure/<Tool>` folder and the wiring in `Cli`.
- Embedded sbx scripts move with their adapters but keep their logical resource names; docs and verification notes that name their paths follow them.
- The move is expand–contract: the wide facade loses members as each use case moves behind its interface, and is deleted at the end, when the architecture test becomes strict.
