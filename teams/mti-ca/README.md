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
| `abox-azure-devops` | Username `pat`, then the PAT. Stored as `base64("pat:<PAT>")`. Covers `dev.azure.com`, `pkgs.dev.azure.com` and `vssps.dev.azure.com`. |
| JFrog | Username and token. Stored as `base64("<username>:<token>")`. |
| Elastic | The API key as issued; do not base64-encode it again. MOD and Predictopus keys need `feature_agentBuilder.read`. |
| Context7 | Optional. |
| `abox-github-enterprise` | A fine-grained personal access token for `rwe.ghe.com`, see below. |

`AZURE_DEVOPS_EXT_PAT` inside the sandbox is a placeholder, not the stored
credential.

### The GitHub Enterprise token

Access to GitHub Enterprise Cloud (`rwe.ghe.com`), including Copilot, uses one
fine-grained personal access token: the service `abox-github-enterprise`. The
`github-enterprise-auth` kit asks for it with `credential@1`, for `rwe.ghe.com`,
`api.rwe.ghe.com`, `uploads.rwe.ghe.com` and `copilot-api.rwe.ghe.com`. Like every abox
service it is stored by its name, so it never collides with another secret for the
same hosts, such as the old `gh`-based `RWE_GHE_TOKEN`.

The sandbox sees a placeholder in `GHE_TOKEN`, and the proxy sends the real token to
these four hosts. Without a stored token the kit warns and leaves `gh` and git signed
out; the sandbox is still created, and what needs the tenant fails inside it until the
token is stored and the sandbox recreated.

You create the token yourself in the browser. When it is missing, `abox create --local`
and `abox secrets set --service abox-github-enterprise` print the permissions and the
prefilled link, ask you to paste the token, check it and store it. The host needs no
`gh`. The permissions, the template link for creating the token by hand, the
expiry warning and what to do with the old `gh`-based `RWE_GHE_TOKEN` secret are in
[docs/github-enterprise-token.md](../../docs/github-enterprise-token.md).

The token cannot create or delete repositories (`gh repo create` fails with 403), and
there is no wiki write access. `GH_TOKEN` and `GITHUB_TOKEN` stay empty, so github.com
is anonymous. The standalone Copilot CLI uses the same token through
`COPILOT_GITHUB_TOKEN` and `GH_HOST` in login shells.

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
