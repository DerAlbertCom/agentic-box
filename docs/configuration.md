# Configuration

abox reads two kinds of settings: the **user configuration**, your personal defaults
for every folder, and the **folder configuration** of one sandbox location. Both are
optional. Command options are listed in the [CLI reference](cli-reference.md).

## User settings

Your personal defaults live in `config.yaml`:

| Case | Path |
|---|---|
| `XDG_CONFIG_HOME` is an absolute path | `$XDG_CONFIG_HOME/abox/config.yaml` |
| Linux, macOS | `<home>/.config/abox/config.yaml` |
| Windows | `<home>\.abox\config\config.yaml` |

`<home>` is `HOME` when it is set, otherwise `USERPROFILE`. This holds on Windows too,
so in Git Bash `HOME` wins.

```yaml
defaultTeam: sample
defaultTag: latest
pullRepository: mtiae-docker-release
# defaultLocation: /path/to/project
# sourceRoot: /path/to/agentic-box
userSecretsDenyList:
  - staging.example.internal
  - production.example.internal
terminalTheme: true
```

| Key | Meaning | Default |
|---|---|---|
| `defaultTeam` | Team used when `--team` and the folder configuration give none | none |
| `defaultTag` | Tag used when `--tag` is omitted | `latest` |
| `defaultLocation` | Sandbox location used when `--location` is omitted | the current folder |
| `pullRepository` | JFrog repository on `rwest.jfrog.io` that `create` pulls from. One lowercase repository name, not a URL or path. Independent of a workload's `publishRepository` | `mtiae-docker-release` |
| `sourceRoot` | Repository folder with the team sources, for `build` and `publish` outside the repository. A saved folder without team sources is an error | none |
| `teamsDirectory` | Name of the teams directory in the source repository | `teams` |
| `kitsDirectory` | Kits directory, relative to the source repository root | `kits` |
| `userSecretsDenyList` | Literal substrings, matched case-insensitively against selected user-secret values; a match blocks the copy. See [user-secrets.md](user-secrets.md) | `[]` |
| `terminalTheme` | Your default for [terminal colors](#terminal-colors) | `true` |
| `docsBaseUrl` | Where the documentation pages of the secrets are read. The guided secrets setup joins a kit's `docs` path with it when it shows the link, so the pages can move without a rebuild of published sets | `https://github.com/DerAlbertCom/agentic-box/blob/main/` |

The deny list is empty by default: abox does not detect production secrets by itself.
Put environment markers such as host names in it, never secret values.

## Folder configuration

A sandbox location can hold two YAML files:

- `abox.yaml`: shared settings. Commit it.
- `abox.user.yaml`: personal overrides. Add it to `.gitignore` yourself.

```yaml
# abox.yaml (committed)
squad: ac
portOffset: 20000
userSecrets: [shared-store]
```

```yaml
# abox.user.yaml (git-ignored)
userSecrets: [shared-store, my-dev-store]
```

Every key is allowed in both files:

| Key | Meaning |
|---|---|
| `team` | Team for `abox create`; see [team and squad](#team-and-squad) |
| `squad` | Squad for `abox create` |
| `memory` | Sandbox memory in whole GB; see [sandbox size](#sandbox-size) |
| `cpus` | Number of sandbox CPUs |
| `portOffset` | Default offset for app ports; see [application-ports.md](application-ports.md) |
| `terminalTheme` | `true` or `false`; see [terminal colors](#terminal-colors) |
| `userSecrets` | .NET user-secret stores and keys to copy; see [user-secrets.md](user-secrets.md) |

Rules:

- A value in `abox.user.yaml` replaces the one in `abox.yaml`. Lists are replaced as a
  whole, not appended: the example above selects both stores because it names both.
- Either file may exist alone.
- Invalid YAML, duplicate keys, unknown keys and invalid values are errors that name
  the file.
- Only the `.yaml` names are read. An `abox.json` or `abox.user.json` makes commands
  fail and ask you to rename it and convert the content to YAML.
- abox never writes `abox.yaml` or `.gitignore`. It writes `abox.user.yaml` only after
  a successful `abox create`, to save the team, squad and sandbox size.

The sandbox agent can edit both files, because they are in the workspace. Review them
before a create or user-secrets copy, in particular `userSecrets`.

## Team and squad

`abox create` takes

- the team from `--team`, then `team` in the folder configuration, then the user
  setting `defaultTeam`;
- the squad from `--squad`, then `squad` in the folder configuration. One of them is
  required.

After the sandbox is created, both are saved in `abox.user.yaml` unless the folder
configuration already gives them, so the next `abox create` in that folder needs
neither option.

A given `--squad` replaces the squad saved in `abox.user.yaml`, but it must match a
`squad` in `abox.yaml` (case-insensitive); a conflict fails before anything is changed.
When `abox run` offers to replace a sandbox, it refuses if the sandbox's squad differs
from the folder's squad.

## Sandbox size

```sh
abox create --memory 16 --cpus 8
```

`--memory` is a whole number of GB and `--cpus` a whole number of CPUs, both at least 1.
Each is taken from the option, then `abox.user.yaml`, then `abox.yaml`. With none of
them, sbx uses its own default (see `sbx create --help`).

```yaml
# abox.user.yaml
memory: 16
cpus: 8
```

Values given as options are saved in `abox.user.yaml` after the create, so later
creates in that folder use the same size. Only the top-level `memory` and `cpus` lines
are replaced or added; the rest of the file, comments included, is kept. If the file
cannot be updated, abox says so and the create still completes.

## Terminal colors

While you are in a sandbox, it recolors your terminal session to its current theme
(`sbox themes`) and resets the colors when the shell exits. The host terminal's
configuration is never changed.

The setting is taken from `--terminal-theme <true|false>`, then `terminalTheme` in
`abox.user.yaml`, then `abox.yaml`, then the user `config.yaml`, then `true`:

```sh
abox run --terminal-theme false   # keep the host colors for this session
```

`terminalTheme` must be an unquoted `true` or `false`. Terminals inside Herdr are not
recolored. See the [themes kit](../kits/README.md) and
[ADR 0002](adr/0002-terminal-colors-from-the-sandbox.md).
