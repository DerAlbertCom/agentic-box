# MTI-CA

The squads of the MTI-CA team. They replace the compositions of the earlier
`cape-agent-sandbox`.

| Squad | Base | Adds |
| --- | --- | --- |
| base | — | Workload, agents, Git defaults, RWE GHEC, Azure DevOps auth, sbox with themes |
| cape | base | CS defaults, database MCP, Elastic, JFrog, clean-code, host ports |
| ac, bc, cc | cape | Nothing; aliases of cape |
| cape-subagents | cape | OpenCode model tiers |
| mod | base | MOD defaults, Context7, dual Elastic, MongoDB, tfswitch, AWS gateway |
| predictopus | base | AssetOpt Renewables defaults, metsca NuGet, Predictopus Elastic |
| bau | base | CS defaults, database/Elastic, Bedrock, BAU app, skill and command |

`base` and `cape` are only inherited from. `abox build` builds and `abox publish`
publishes the other seven.

## Build

```sh
abox build --team mti-ca
```

See [development](../../docs/development.md) for local sandboxes and publishing, and
[authoring](../../docs/authoring.md) for the file formats.

Notes on the workload (`mti-ca.workload`):

- The template image is pinned by digest. It provides the `agent` user (UID 1000) with
  passwordless sudo, which the setup scripts need.
- All mise tools are pinned to exact versions. Some entries use mise's
  `--tool-option` and `--postinstall` flags.
- .NET is not installed by mise but by `install-dotnet.sh` through dotnetup, with a
  pinned manager version and pinned SDK versions. Review these pins when a new .NET
  release should be picked up.

## Host credentials

Real credentials never belong in kit arguments, kit files or the folder configuration
(`abox.yaml`, `abox.user.yaml`). Store them on the host with `abox secrets set`; the
proxy injects them for the hosts each kit names. Registry sign-in (`docker login`) is
separate.

| Service | What to enter |
| --- | --- |
| `azure-devops` | Username `pat`, then the PAT. Stored as `base64("pat:<PAT>")`. Covers `dev.azure.com`, `pkgs.dev.azure.com` and `vssps.dev.azure.com`. |
| JFrog | Username and token. Stored as `base64("<username>:<token>")`. |
| Elastic | The API key as issued; do not base64-encode it again. MOD and Predictopus keys need `feature_agentBuilder.read`. |
| Context7 | Optional. |
| `rwe-ghec` | Nothing; it comes from a host command, see below. |

`AZURE_DEVOPS_EXT_PAT` inside the sandbox is a placeholder, not the stored
credential.

### The RWE_GHE_TOKEN binding

Access to GitHub Enterprise Cloud (`rwe.ghe.com`), including Copilot, uses a custom
secret that takes its value from the host's `gh`. The base squad declares it:

```yaml
secrets:
  - service: rwe-ghec
    host: [copilot-api.rwe.ghe.com, api.rwe.ghe.com, rwe.ghe.com, uploads.rwe.ghe.com]
    env: RWE_GHE_TOKEN
    command: gh auth token -h rwe.ghe.com
    placeholder: gho_{rand}
```

The sandbox sees a placeholder in `RWE_GHE_TOKEN`. The proxy replaces it with the real
token in requests to these four hosts. The `rwe-ghec` kit fails its setup if the
placeholder is missing.

To set it up:

1. Sign in on the host: `gh auth login -h rwe.ghe.com`, with the scopes you need
   (`workflow` to change workflows).
2. `abox create --local` offers to store the binding when it is missing. For a
   published squad, or to do it by hand, run:

   ```sh
   sbx secret set-custom \
     --host copilot-api.rwe.ghe.com --host api.rwe.ghe.com \
     --host rwe.ghe.com --host uploads.rwe.ghe.com \
     --env RWE_GHE_TOKEN --command 'gh auth token -h rwe.ghe.com' \
     --placeholder 'gho_{rand}'
   ```

3. Check with `abox secrets list --location <folder>`.

sbx runs the command when it stores the secret, so a host `gh` that is not signed in
fails right away.

abox treats the binding as present when one custom secret covers all four hosts with
`RWE_GHE_TOKEN`. A custom secret that uses one of the hosts or the variable in another
way is reported as a conflicting binding and not replaced. Review it yourself before
removing it; another sandbox may use it.

The standalone Copilot CLI still needs its own `/login` inside the sandbox.
`GH_TOKEN` and `GITHUB_TOKEN` stay empty.

## MCP servers

The `squad-mcp` kit installs complete agent-native MCP configurations per squad,
because the servers need commands, headers or filters that the `mcp` key of `.abox`
does not support. Endpoints and organization names come from squad parameters;
credentials stay runtime references. Keep the service hosts in line with the
credential kits.

- Claude Code and Copilot cannot enforce Elastic's wildcard tool allow-list. Do not
  rely on it as an authorization boundary.
- MOD's AWS gateway is optional. After registering it on the host, load it with
  `sbx mcp load aws-gateway --sandbox <sandbox>`. Host SSO and proxy setup are not
  part of the squad.

## Differences from cape-agent-sandbox

- **State.** The state directories now persist under abox's state directory. Existing
  content of `~/.cabox/state` is not moved.
- **Git identity.** The `git-user` kit uses the optional `git-identity@1` capability.
  Host aliases are not synchronized automatically; the `sbx-sync-git-user` helper is
  still there to run by hand.
- **Host automation.** Skill and font synchronization, Copilot credit-budget
  reapplication and the Herdr TUI attach flow are not ported. Use
  `abox run herdr-app` for Herdr.
- **Network.** Egress is open, as before. Credentials are injected only for the hosts
  each kit names, and host or organization denies still win.
- **host-access-cape** replaces `cape-localhost-ports`; see its
  [README](../../kits/host-access-cape/README.md).
- **BAU.** The app's dependencies and models are downloaded when the sandbox is
  created.
