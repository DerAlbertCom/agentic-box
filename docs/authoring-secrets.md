# Authoring secrets

Secrets are the host-side credentials a squad's kits ask for. The value stays on the
host: sbx's proxy injects it into requests to the service, and the sandbox only sees a
placeholder. They are not the same as [.NET user-secrets](user-secrets.md).

Three things work together:

| Where | What it says |
|---|---|
| `credential@1` in a kit descriptor | Which service the kit needs, for which domains and header the proxy injects it, and in its `description` what the credential is for. |
| `secrets.yaml` next to the kit descriptor | How the value of a service is obtained: the prompt, its parts, fixed parts, a documentation page, or a host command. |
| `abox secrets` on the host | The guided secrets setup, and storing, listing and removing the values. |

Never put credentials in kit arguments, kit files, `secrets.yaml` or the folder
configuration.

## Declaring the secrets of a kit

A kit declares its secrets once, in a `secrets.yaml` next to its kit descriptor, so
every squad that uses the kit gets the same prompts. The file holds a list:

```yaml
# kits/azure-devops/secrets.yaml
- service: abox-azure-devops               # a service the kit asks for with credential@1
  docs: docs/secrets/azure-devops.md       # the page that tells how to obtain the value
  encoding: base64
  inputs:
    - name: username
      value: pat                           # a fixed part: never asked
    - name: pat
      prompt: Azure DevOps PAT
```

```yaml
# kits/jfrog/secrets.yaml
- service: abox-jfrog
  docs: docs/secrets/jfrog.md
  encoding: base64
  inputs:
    - name: username
      prompt: JFrog user name
      secret: false                        # echoed, and offered as the default next time
    - name: token
      prompt: JFrog token
```

```yaml
# custom secrets: no credential@1 needed
- service: abox-elastic
  description: The API key a setup script reads from ELASTIC_API_KEY
  host: api.elastic.co
  env: ELASTIC_API_KEY
  prompt: The API key for Elastic

- service: abox-build-cache                # a custom secret from a host command
  host: cache.example.com
  env: CACHE_TOKEN
  command: cache-cli token
  placeholder: cache_{rand}
```

| Key | Meaning |
|---|---|
| `service` | The name the value is stored under. A `credential@1` capability of the same kit names the same service, unless it is a custom secret. Start it with `abox-`. A plain list entry such as `- abox-github` is short for `service: abox-github`. |
| `prompt` | What the user is asked for a single value. Without it a generic prompt names the service. |
| `docs` | A repository-relative path to the page that tells how to obtain the value. abox joins it with the [documentation base URL](configuration.md) when it shows the link. |
| `description` | Only for a custom secret: what it is for. Any other secret takes its description from the `description` of its `credential@1` capability. |
| `inputs` | The parts a value is built from, each with `name` and either `prompt` or `value`. Without them one value is asked for. |
| `inputs[].secret` | `true` (default) masks the part with one `*` per character and never keeps it. `false` echoes it and remembers it as the default for the next time, for a user name. |
| `inputs[].value` | A fixed part. It is never asked, never kept, and has no `prompt` or `secret`. |
| `separator` | Joins the inputs. Default `:`. |
| `encoding` | `plain` (default) or `base64`, applied to the joined value. |
| `host`, `env` | Make it a **custom secret**: the variable `env` holds a placeholder in the sandbox, and the proxy replaces it in requests to `host`. `host` can be a list. |
| `valueCommand` | A host command whose output is the value, instead of `prompt` or `inputs`. `secrets init`, `update` and `set` run it with `sh -c` (on Windows the `sh` of Git for Windows), take its output without surrounding white space, never show it, and say that they did. A failing or silent command stores nothing. Unlike `command`, it needs no `host`, and sbx keeps the value, not the command. |
| `command` | A host command that sbx runs whenever it needs the value of a custom secret, instead of `prompt` or `inputs`. Needs `host`. |
| `placeholder` | The value the sandbox sees for a custom secret; `{rand}` is a random part. Needs `host`. |

The description lives in the kit descriptor's own `description` field of the capability,
as plain text, so the descriptor stays valid kit spec and carries no abox-specific keys:

```yaml
- type: com.docker.sandbox/credential@1
  description: JFrog Artifactory user name and token for the NuGet feeds
  config:
    service: abox-jfrog
```

Every service name starts with `abox-`, in `secrets.yaml` and in the kits'
`credential@1`. The prefix keeps abox's values apart from the services sbx or another
tool configures, so a sandbox only gets the secrets abox stored.

`abox build` checks the declarations and fails on:

- an unknown key;
- a declaration that is not a custom secret and names a service no `credential@1`
  capability of the same kit asks for;
- a service that two kits of one squad declare differently. Several kits may declare a
  service identically;
- `secrets` in a squad configuration (`.abox`). The declarations moved into the kits;
  the message names the kit's `secrets.yaml`.

The service `abox-github-enterprise` gets its value differently when a kit asks for
it with `credential@1`. The user creates a fine-grained personal access token; abox
prints the permissions and the prefilled link for the tenant (the kit's domain without
`api.`, `uploads.` or `copilot-api.`), asks for the organization that owns the token so
the prefilled link names it as the resource owner, checks the pasted token against `api.<tenant>`,
stores it under the service name, and warns on `create` and `run` before it expires. See [the GitHub Enterprise token](github-enterprise-token.md).

A service that a kit asks for with `credential@1` is always stored by its name, so it
works without a declaration; the declaration only improves the prompt. A custom secret
must be declared, because abox cannot know its host and variable otherwise. Kits from a
registry cannot be inspected; their services get the default prompt.

`abox build` collects the declarations of the squad's local kits into the squad's
`squad.json`, which is part of the published set, with each description, documentation
path, per-input secret flag and fixed value. Prompts, descriptions, hosts and variable
names are therefore readable by everyone who can read the set. Values and host commands
are never written there. A `squad.json` of an older build has none of the new fields:
its inputs are secret, and it shows no description and no link.

## The guided secrets setup

One guided setup, reachable three ways, asks for the values and approvals:

```sh
abox secrets init   [--location <path>] [--team <team>] [--squad <squad>] [--local] [--tag <tag>] [--all]
abox secrets update [--location <path>] [--service <name>]...
abox create         ...   # only when something is missing
```

For each service it prints the name, the kits that ask for it, whether it is required,
the description, the domains it is sent to and the documentation link. Then it asks:

- an input marked `secret: false` in clear text, with its last value as the default;
- a secret input masked with one `*` per typed or pasted character, then confirms the
  length it received;
- never a fixed input.

An empty answer skips the secret, so nothing incomplete is stored. After a value is
stored, or when it is stored already, a missing approval for the kits' domains is asked
for. The setup ends with the table `abox secrets list` prints.

- `init` prepares everything a squad needs. It works before a sandbox exists, for a
  local build (`--local`, from the build record) and for a published squad (the set's
  bundled metadata, read from the registry), without team sources. Team and squad come
  from the options, then the folder configuration, then the user defaults. It asks only
  for what is not stored or not approved, so a second squad on the same host asks only
  for what is new; `--all` asks for every secret again, after a loss or a rotation.
- `update` shows a list of the services with their state (stored, not set, optional,
  conflict); move with the arrow keys, pick with space, confirm with Enter. Nothing is
  preselected. `--service <name>` skips the list. It reads the services of the folder's
  sandbox, so the list reflects what the sandbox runs, and the squad of the folder
  configuration when the folder has no sandbox.
- `create` runs it once, before the sandbox is created; see below.

Without a terminal, `init` and `update` ask nothing: they fail with exit code `1` and
print the `abox secrets set --location <path> --service <name>` command of each service.
Redirected input is read line by line, without masking.

The value of an input marked `secret: false` is kept in plain text in abox's state
directory (`secret-inputs.json`), by service and input name, after a successful store.
It is not encrypted and not in an OS keychain. `abox secrets remove` forgets it. Fixed
and secret inputs are never kept.

## Storing the values one by one

`abox secrets list|set|remove [--location <folder>]` works on the existing sandbox of
a folder. It reads the services the kits ask for and the squad's declarations from
inside the sandbox, so it needs no team sources. It starts a stopped sandbox, but
never creates or replaces one.

Values are stored globally on the host. A value set once serves every sandbox whose
kits ask for that service, and removing it affects all of them.

- `list` shows each service, the kits that ask for it, whether it is required, and
  whether a value is stored. `not declared` marks a service a kit asks for that no kit
  declares.
- `set` asks for every declared service, or only for those named with `--service
  <name>` (repeatable), with the input rules of the guided setup. An empty answer
  stores nothing. A service a kit asks for without a declaration gets the default
  prompt.
- `remove` takes the values of the declared or named services off the host and
  forgets their remembered inputs.

Two cases need attention:

- **Conflicting binding.** A custom secret counts as stored when a stored custom
  secret covers all its hosts with its `env`. If another custom secret uses one of
  the hosts or the same variable differently, `list` shows `conflicts with <variable>`
  and neither `set` nor the guided setup overwrites it. Another sandbox may need it;
  review and remove it yourself with `sbx secret`.
- **Command-backed secrets.** abox never runs a host command that it read from a
  sandbox or a published set, and a build never bundles one. For such a secret `set`
  and the guided setup print the `sbx secret set-custom` command with
  `--command '<host command>'`. Run it with the `command` from the kit's
  `secrets.yaml`. Put that command in the team's README for users without a checkout.

A sandbox created from an old build without declarations lists its services as not
declared. Recreate it from a current build to get the prompts and custom secrets.

## Approvals and `abox create`

A stored value is not enough for a kit's credential. sbx injects it only when the
service is approved for the kit's domains (`apiKey.inject[].domain`). sbx keeps these
bindings in `credentials.yaml` in its config directory (`~/.config/sbx`, or
`%APPDATA%\sbx` on Windows):

```yaml
bindings:
    abox-github:
        apiKey:
            domains:
                - api.github.com
```

The guided setup asks `Approve sending the credential '<service>' to <domains>?`. Yes
adds the domains to the binding and keeps every other entry. Custom secrets need no
approval.

sbx rejects a sandbox whose required credential is missing, and several kits use their
credential while they install. So `abox create` checks the values and approvals after
all its other checks and the replacement question, and before the sandbox is created or
an existing one replaced, for local and published sources alike. It reads both from the
build's `squad.json`, so it needs no team sources.

- When nothing is missing it asks nothing.
- On a terminal it runs the guided setup once, for required and optional services
  together, with a header that says why (the squad needs N secrets that are not stored
  or approved), that the values stay on the host, and that an empty answer skips an
  optional secret. Skipping a required secret or declining its approval stops the
  create before an existing sandbox is replaced.
- Without a terminal it asks nothing. A missing required secret stops the create with
  exit code `1`, before anything is created or replaced; the message lists what is
  missing and names `abox secrets init --location <path>` to run on a terminal. Only
  optional secrets missing is one warning with the same hint, and the create goes on.

## Trying it without real credentials

The `secrets` squad of the sample team (`teams/sample/secrets.abox`, kit
`kits/sample-secrets` with its `secrets.yaml`) declares one secret of each kind. Its
values are sent only to hosts under `example.invalid`, which never resolve, so any text
will do.

```sh
abox build --team sample
abox secrets init --team sample --squad secrets --local --location <folder>
abox create --team sample --squad secrets --local --location <folder>
abox secrets update --location <folder>
abox secrets remove --location <folder>
```
