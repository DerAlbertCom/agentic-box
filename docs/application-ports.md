# Application ports

abox makes apps that run inside a sandbox reachable from the host browser. It reads the
ports your projects declare, adds an offset and forwards each resulting host port to the
sandbox. An app on sandbox port 8203 is reachable at `http://localhost:18203` with the
default offset of 10000.

Forwarding is set up automatically when a sandbox is created or replaced. For an existing
sandbox, after changing project ports, or to retry a failed setup, run:

```sh
abox ports --location "/path/to/project"
```

`abox ports` never creates or replaces a sandbox, and it never starts your apps. Start
them inside the sandbox with their normal profiles; localhost-only bindings work. The
printed table is the mapping plan, not proof that an app is running.

For ASP.NET Core projects the same setup also configures HTTPS; see
[development HTTPS certificates](https-development-certificates.md).

## What is scanned

Discovery reads files beneath the sandbox location. It runs no scripts, tools or builds
and never edits your sources.

| Source | Ports taken |
|---|---|
| `Properties/launchSettings.json` | every profile's `applicationUrl` values (semicolon-separated) |
| `angular.json` | each project's `architect.serve` or `targets.serve`: `options.port` and every `configurations.<name>.port` |
| `package.json` | `scripts.start`, when it is a direct `ng serve`, `ng s` or `ng dev` with one literal `--port 4200` or `--port=4200` |

Limits to know:

- Angular ports must be literal integers. The implicit default 4200 is not assumed; put
  the port in `angular.json`.
- Environment variables such as `PORT` are not evaluated, so the real listener can differ
  from the plan.
- A `start` script that wraps `ng` (`npx`, `cross-env`, another npm script) or runs
  another server (Vite, Next, a custom Node server) is ignored.
- A direct `ng` start script with a dynamic or repeated `--port` fails with a hint: put a
  literal port in `angular.json` and remove the flag.

Every source occurrence is printed. Duplicates give a warning and are published once.
All inputs and all resulting host ports are validated before anything is published, so an
invalid plan publishes nothing.

## Host ports and offsets

Host port = sandbox port + offset. The offset comes from, in this order:

1. `--offset <n>` for this run of `abox ports`.
2. `portOffset` in the sandbox location's `abox.user.yaml`, then `abox.yaml`.
3. 10000.

```yaml
# abox.yaml
portOffset: 20000
```

```sh
abox ports --location "/path/to/project"                  # 8203 -> http://localhost:28203
abox ports --location "/path/to/project" --offset 30000   # 8203 -> http://localhost:38203
```

There is no user-wide setting, and abox never writes `portOffset`. See
[configuration](configuration.md) for the folder configuration files.

An offset is a whole number from -65534 to 65534. Zero keeps the port number and a
negative offset subtracts. In YAML it must be an unquoted integer. Every resulting host
port must be between 1 and 65535, otherwise the whole run fails. Low host ports can still
need operating-system privileges.

## How forwarding works

sbx cannot publish a host port straight to an app that listens on sandbox loopback. abox
therefore starts a small TCP relay inside the sandbox for each app port and publishes the
host port to that relay.

- Host ports bind to `127.0.0.1` only.
- The relay needs `python3` 3.8 or later in the sandbox, for the sandbox user. abox checks
  this and installs nothing.
- The relay listens on its own high port. It does not reserve or change your app's port,
  sources or startup flags.
- Relay state lives in the sandbox under `~/.abox-ports-v1`.
- Relays end when the sandbox stops. `abox ports` starts a stopped sandbox and restores
  them on the same ports.

## Mappings persist

Mappings stay after abox exits. A one-run `--offset` selects the offset for that run; it
does not remove the mappings when the command ends.

- Running again with the same offset reuses the existing relays and mappings and only
  publishes what is missing.
- Mappings to other targets are left alone.
- abox never removes a mapping. Anything it cannot prove to be its own relay mapping is a
  conflict, and the run fails before any change:
  - **Offset change:** a mapping to one of abox's relays exists at another offset.
  - **Direct mapping:** a mapping goes straight to an app port, for example
    `127.0.0.1:18203:8203/tcp4`. Matching numbers do not prove abox made it, so this is a
    conflict at every offset, including 10000.
  - A host port that another service uses, a non-loopback binding or a different protocol.

### Changing the offset or resolving a conflict

Inspect the sandbox's mappings, remove the exact old binding yourself, then run abox
again. Use the real sandbox name and the exact binding that the inventory shows:

```sh
sbx ports <sandbox-name> --json
sbx ports <sandbox-name> --unpublish 127.0.0.1:28203:59646/tcp4 --json
abox ports --location "/path/to/project" --offset 30000
```

Here `28203` is the old host port and `59646` the relay port. Remove every old abox
binding before the new offset can proceed; the relays are reused. A direct mapping is
removed the same way, with its own spec (`127.0.0.1:18203:8203/tcp4`).

- Do not remove a binding that another person or tool created without their consent.
- Do not delete `~/.abox-ports-v1` while bindings still point to its relays.

## During create and replace

`abox create`, a replacement, and a replacement accepted in `abox run` all run this setup.
An ordinary `abox run` does not scan, publish or refresh anything.

1. Before anything changes, abox reads the folder configuration once and validates the
   port plan with the folder's `portOffset` (default 10000). An invalid plan fails here,
   and an existing sandbox is left untouched. Create has no `--offset` option.
2. The sandbox is created or replaced and its record saved.
3. Selected [user-secrets](user-secrets.md) are copied.
4. Ports are published, and HTTPS is configured when an ASP.NET Core project has ports.

Success is reported, and a launcher started, only when all of these steps succeed.

## Failure and retry

There is no rollback. A failure or cancellation can leave some mappings published and
relays running. abox exits with 1, or 130 when cancelled, and prints the command to retry.

- **Setup failed after create:** the new sandbox and its record are kept. Do not recreate
  it. If user-secret copying failed, run the printed `abox user-secrets` command first.
  Then run the printed ports command.
- **Retry with the same offset.** The printed command carries it, for example:

  ```sh
  abox ports --location "/path/to/project" --offset 10000
  ```

  Existing mappings are reused and only the missing ones are published. A different
  offset conflicts with the mappings that survived.
- **HTTPS failed after forwarding succeeded:** the mappings stay. Fix the prerequisite and
  run `abox ports` again.

Do not run two `abox ports` at the same time for one sandbox, and do not change its
bindings or relay state by hand while setup runs.
