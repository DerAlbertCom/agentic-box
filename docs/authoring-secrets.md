# Authoring secrets

Secrets are the host-side credentials a squad's kits ask for. The value stays on the
host: sbx's proxy injects it into requests to the service, and the sandbox only sees a
placeholder. They are not the same as [.NET user-secrets](user-secrets.md).

Three things work together:

| Where | What it says |
|---|---|
| `credential@1` in a kit descriptor | Which service the kit needs, and for which domains and header the proxy injects it. |
| `secrets` in the `.abox` file | How the value of a service is obtained: the prompt, its parts, or a host command. |
| `abox secrets` on the host | Stores, lists and removes the values. |

Never put credentials in kit arguments, kit files, `secrets` prompts or the folder
configuration.

## Secrets

Declare each service once in the `secrets` list of a squad in the `base` chain.

```yaml
secrets:
  - github                                   # shorthand for "service: github"

  - service: azure-devops                    # a service a kit asks for with credential@1
    prompt: The Azure DevOps personal access token

  - service: registry                        # a value built from parts
    inputs:
      - name: username
        prompt: The user name
      - name: password
        prompt: The password
    separator: ":"
    encoding: base64

  - service: elastic                         # a custom secret: no credential@1 needed
    host: api.elastic.co
    env: ELASTIC_API_KEY
    prompt: The API key for Elastic

  - service: rwe-ghec                        # a custom secret from a host command
    host: [copilot-api.rwe.ghe.com, api.rwe.ghe.com, rwe.ghe.com, uploads.rwe.ghe.com]
    env: RWE_GHE_TOKEN
    command: gh auth token -h rwe.ghe.com
    placeholder: gho_{rand}
```

| Key | Meaning |
|---|---|
| `service` | The name the value is stored under. A kit's `credential@1` names the same service. |
| `prompt` | What the user is asked. Without it a generic prompt names the service. |
| `inputs` | The parts a value is built from, each with `name` and `prompt`. Without them one value is asked for. |
| `separator` | Joins the inputs. Default `:`. |
| `encoding` | `plain` (default) or `base64`, applied to the joined value. |
| `host`, `env` | Make it a **custom secret**: the variable `env` holds a placeholder in the sandbox, and the proxy replaces it in requests to `host`. `host` can be a list. |
| `command` | A host command that prints the value, instead of `prompt` or `inputs`. Needs `host`. |
| `placeholder` | The value the sandbox sees for a custom secret; `{rand}` is a random part. Needs `host`. |

A service that a kit asks for with `credential@1` is always stored by its name, so it
works without a declaration; the declaration only improves the prompt. A custom secret
must be declared, because abox cannot know its host and variable otherwise.

`abox build` writes the declarations into the squad's `squad.json`, which is part of
the published set. Prompts, hosts and variable names are therefore readable by
everyone who can read the set. Values and host commands are never written there.

## Storing the values

`abox secrets list|set|remove [--location <folder>]` works on the existing sandbox of
a folder. It reads the services the kits ask for and the squad's declarations from
inside the sandbox, so it needs no team sources. It starts a stopped sandbox, but
never creates or replaces one.

Values are stored globally on the host. A value set once serves every sandbox whose
kits ask for that service, and removing it affects all of them.

- `list` shows each service, the kits that ask for it, whether it is required, and
  whether a value is stored. `not declared` marks a service a kit asks for that the
  squad does not declare.
- `set` asks for every declared service, or only for those named with `--service
  <name>` (repeatable). An empty answer stores nothing. A service a kit asks for
  without a declaration gets the default prompt.
- `remove` takes the values of the declared or named services off the host.

Two cases need attention:

- **Conflicting binding.** A custom secret counts as stored when a stored custom
  secret covers all its hosts with its `env`. If another custom secret uses one of
  the hosts or the same variable differently, `list` shows `conflicting binding` and
  `set` does not overwrite it. Another sandbox may need it; review and remove it
  yourself with `sbx secret`.
- **Command-backed secrets.** abox never runs a host command that it read from a
  sandbox. For such a secret `set` prints the `sbx secret set-custom` command with
  `--command '<host command>'`. Run it with the `command` from the squad's `.abox`
  file. Put that command in the team's README for users without a checkout.

A sandbox created from an old build without declarations lists its services as not
declared. Recreate it from a current build to get the prompts and custom secrets.

## Approvals and the check in `abox create`

A stored value is not enough for a kit's credential. sbx injects it only when the
service is approved for the kit's domains (`apiKey.inject[].domain`). sbx keeps these
bindings in `credentials.yaml` in its config directory (`~/.config/sbx`, or
`%APPDATA%\sbx` on Windows):

```yaml
bindings:
    github:
        apiKey:
            domains:
                - api.github.com
```

`abox create --local` checks values and approvals before it replaces a sandbox, for
the credentials of the squad's local kits and for its declared custom secrets.

- On a terminal it asks to set each missing value, as `abox secrets set` does, and
  runs the `command` of a command-backed secret through sbx. For each missing approval
  it asks `Approve sending the credential '<service>' to <domains>?`. Yes adds the
  domains to the binding and keeps every other entry. Custom secrets need no approval.
- Without a terminal it prints what is missing, with the `abox secrets set` commands
  to run, and continues.

The check never fails the create. sbx starts a sandbox without an optional
credential and rejects a missing required one; abox shows its message.

## Trying it without real credentials

The `secrets` squad of the sample team (`teams/sample/secrets.abox`, kit
`kits/sample-secrets`) declares one secret of each kind. Its values are sent only to
hosts under `example.invalid`, which never resolve, so any text will do.

```sh
abox build --team sample
abox create --team sample --squad secrets --local --location <folder>
abox secrets list --location <folder>
abox secrets set --location <folder>
abox secrets remove --location <folder>
```
