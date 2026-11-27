# Kits

This folder holds the local kits that the squads under `teams/` name. Each kit is a
directory with a descriptor of the same name, for example
`pi-config/pi-config.yaml`. The descriptor follows Docker's
[kit spec](https://github.com/docker/sandbox-kit-spec).

How to write a kit is in [docs/authoring-kits.md](../docs/authoring-kits.md). The kits
here are working examples to copy from:

| Kit | Shows |
|---|---|
| `pi-config`, `opencode-config` | Writing an agent configuration. |
| `github-enterprise-auth` | A `credential@1` service the kit's setup reads, and agent configs rendered from templates. See [the GitHub Enterprise token](../docs/github-enterprise-token.md). |
| `sample-secrets` | Dummy credentials, to try `abox secrets`. |
| [`host-access-cape`](host-access-cape/README.md) | Reaching services on the host. |
| `sbox`, [`themes`](themes/README.md) | A kit build step that bundles TypeScript. |

## sbox

The `sbox` kit installs `sbox`, the command line inside the sandbox for user-facing
actions such as switching the theme. Do not confuse it with `sbx`, the Docker
Sandboxes CLI on the host.

`sbox` runs on Bun, which the workload must install through mise (`bun@1.4.4` in
`teams/mti-ca/mti-ca.workload`). Without Bun, `sbox` says so.

| File in the sandbox | What it is |
|---|---|
| `~/.local/bin/sbox` | The launcher, on `PATH`. |
| `~/.local/share/sbox/sbox.js` | The bundled core from `sbox-cli/sbox/`. |
| `~/.local/share/sbox/commands/<command>/command.js` | A command, installed by its command kit. |
| `~/.local/state/sbox/<command>/` | The state folder of a command. |

`sbox --help` lists the installed commands. A command whose module fails to load is
listed as broken with the reason; the other commands keep working.

The bundle is not committed. The kit's
[build step](../docs/authoring-kits.md#kit-build-step) (`kits/sbox/build`) produces it
during `abox build`, running Bun in Docker through `sbox-cli/bundle`. Bun's package
cache is kept in the Docker volume `agentic-box-bun-cache`.

### Command kits

A command kit adds one `sbox` command; [`themes`](themes/README.md) is one. To write
another:

- The package's module default-exports a `Command` (`summary` and `run(args, ctx)`)
  from the command API in `sbox-cli/sbox/src/api.ts`. See the
  [TypeScript coding standards](../CODING_STANDARDS.md).
- The package declares the data folders to install next to the module in its
  `package.json`: `"sbox": { "data": ["themes", "apps"] }`.
- The kit's build step bundles the module into `command.js` with `sbox-cli/bundle` and
  copies the declared data folders (`sbox-cli/data-folders` lists them). A declared
  folder that is missing fails the build.
- The kit's recipe copies the result to `~/.local/share/sbox/commands/<command>/`.

`kits/themes/build` is the example to copy.
