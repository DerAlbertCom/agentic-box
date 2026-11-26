# Authoring squads

This guide is for maintainers who define a team's squads: which base image the
sandboxes are built on, which kits they carry and what they may reach. It is the
reference for the `.abox` and `.workload` files.

- [Kits](authoring-kits.md): local and registry kits, kit arguments, the kit build step.
- [Secrets](authoring-secrets.md): declaring the credentials the kits ask for.
- [Artifact format](artifact-format.md): what `abox build` and `abox publish` produce.
- [Development](development.md): building, publishing and testing from a checkout.

Terms such as squad, workload, kit and state are defined in the
[glossary](../GLOSSARY.md).

## How a project is put together

```text
.
├── teams/
│   └── sample/
│       ├── ac.abox            a squad
│       ├── cape.abox          the squad the others inherit from
│       ├── secrets.abox       a squad with dummy secrets, to try abox secrets
│       ├── sample.workload    the workload: the base image of the team
│       └── enable-fzf.sh      a script the workload runs
└── kits/
    └── pi-config/             a local kit: a directory with its descriptor
        └── pi-config.yaml
```

A team is a folder under `teams`. It holds the `.abox` files of its squads, its
workload and the scripts the workload runs. Local kits live in `kits`, so every team
can use them. All files are YAML, and an unknown key is an error.

## From files to a sandbox

```sh
abox build --team sample
abox create --team sample --squad ac --local --location <folder>
abox secrets set --location <folder>
abox run --location <folder>
```

- `build` builds every squad of the team that no other squad of the team names as its
  `base`; a base squad is built as part of the squads that inherit from it. Every
  squad is checked before Docker is used. Per squad it builds one component image
  for the workload, each local kit, the state mixin (if the squad declares state) and
  the configuration mixin. Later builds rebuild only the components whose inputs
  changed; `--rebuild` builds all of them. `--tag` selects another tag than `latest`.
- `create --local` creates the sandbox of a folder from the local build. Without
  `--local` it pulls the published kit set. It asks before it replaces an existing
  sandbox.
- `secrets set` stores the credentials the sandbox's kits ask for on the host.
- `run` opens the sandbox. If the squad has a newer local build than the sandbox was
  created from, it offers to replace the sandbox first.

Only `build` and `publish` read the team sources. `create`, `run` and `secrets` work
without a checkout. The commands and their options are in the
[CLI reference](cli-reference.md).

### Where build and publish find the sources

`build` and `publish` look for the `teams` folder in a start directory and then in its
parents. The start directory is the first of:

1. `--root <dir>`;
2. the current directory, if the team is found from there;
3. the user setting `sourceRoot`.

On a terminal, a run without a usable start directory asks for one and saves it as
`sourceRoot`. Without a terminal it fails.

The project that owns the `teams` folder also supplies the local kits and shared
workloads. The user settings `teamsDirectory` (default `teams`) and `kitsDirectory`
(default `kits`) name these folders relative to the project root.

## The squad configuration: `.abox`

```yaml
# teams/sample/ac.abox
base: cape

kits:
  - github-enterprise-auth                   # a local kit in the kits folder
  - docker.io/acme/elastic-auth:1.0          # a registry kit
  - name: elastic-auth                       # a kit with arguments
    args:
      url: ${{ parameters.elastic-url }}

parameters:
  elastic-url: https://foobar.com/mcp

mcp:
  - azuredevops                              # registered on the host already
  - name: elastic                            # registered by abox
    url: ${{ parameters.elastic-url }}

state:
  - ~/.config/opencode/state

secrets:
  - service: github
    prompt: The GitHub token for GitHub Enterprise Cloud
```

| Key | Meaning |
|---|---|
| `base` | The squad this one inherits from. Chains are allowed, cycles are errors. |
| `workload` | The name of a `.workload` file. Only the root squad (the one without `base`) declares it. |
| `kits` | The kits of the squad. See [Kits](authoring-kits.md). |
| `parameters` | Named values. Kit arguments and MCP URLs read them with `${{ parameters.NAME }}`. |
| `mcp` | The MCP servers every agent gets. See [MCP servers](#mcp-servers). |
| `state` | Sandbox directories that survive a replacement. See [State](#state). |
| `secrets` | How the credentials the kits ask for are obtained. See [Secrets](authoring-secrets.md). |

References never carry a file extension.

**Finding a squad.** `--team sample --squad AC` resolves to `teams/sample/ac.abox`;
names are matched without regard to case. A `base` is looked up in the squad's own
team folder first, then in the other team folders of the project. A base that several
other teams hold is an error.

**Inheritance.** A squad adds to what its base declares; it cannot replace or remove
anything. Across the whole chain everything is declared once. These are errors:

- two kits of one repository, even with different tags;
- two MCP servers or two parameters of one name;
- two secrets of one service;
- duplicate or overlapping state directories;
- a parameter that is used but not declared.

The error names the duplicate and the files that declare it.

## The workload: `.workload`

The workload defines the base image of the team.

```yaml
# teams/sample/sample.workload
template: docker.io/sbx/shell-docker-image
publishRepository: mtiae-developer-container-images-local

apt:
  - git

mise:
  - nodejs@24.22.2
  - dotnet@9.0.303
  - dotnet@10.0.403        # of several versions, the last is active

scripts:
  - enable-fzf.sh
```

| Key | Meaning |
|---|---|
| `template` | The image to build on. Default `docker.io/sbx/shell-docker-image`, which has Docker Engine inside the sandbox. Pin a tag or digest, not `latest`. It must provide `curl` and `update-ca-certificates`. |
| `publishRepository` | Required. The JFrog repository on `rwest.jfrog.io` that the squad's images are named after and published to. One lowercase path segment: no host, slash or tag. Rebuild after changing it. |
| `apt` | Package names, installed as root in one `apt-get install` step before the tools. |
| `mise` | Tools, installed with mise in list order. Put a runtime such as `nodejs` before the `npm:` tools that need it. Of several versions of one tool, the last is the active one. |
| `scripts` | Scripts next to the file, run at the end of the image build. Two scripts with the same file name are rejected. |

A team's workload lives in its team folder. A workload shared by several teams can live
in the `kits` folder; the team folder is searched first.

What the build adds on its own:

- It pins `template` to the digest its tag resolves to.
- It installs the RWE inspection CA certificates into the image's system trust before
  anything is downloaded, and points mise and Node at them.
- If `GITHUB_TOKEN` is set on the host, mise downloads use it as a BuildKit secret. It
  does not end up in an image layer.

Workload steps run when the image is built, not when a sandbox is created.

## MCP servers

```yaml
mcp:
  - azuredevops
  - name: elastic
    url: ${{ parameters.elastic-url }}
```

- A name-only entry must already be registered on the host (`sbx mcp add`). `create`
  fails before it replaces a sandbox if the registration is missing.
- An entry with `url` is registered by abox when its name is not registered yet. An
  existing registration is not changed.
- Names match `[A-Za-z0-9_][A-Za-z0-9_.-]*`. A URL, after parameter expansion, must
  be an absolute `http` or `https` URL without a username or password.
- `name` and `url` are the only keys. abox cannot declare servers that need a command,
  arguments or headers; register those on the host, or install an agent-native
  configuration with a kit (as `kits/squad-mcp` does).

## State

`state` lists sandbox directories that persist on the host when the sandbox is
replaced.

```yaml
state:
  - ~/.config/opencode/state
  - /var/lib/cache
```

Paths are sandbox paths. `~` means `/home/agent`. Allowed forms are `~`,
`~/directory` and absolute Linux paths. Repeated and trailing slashes are collapsed.

Rejected:

- `/`, relative paths, `~other-user`, a leading `//`, backslashes, colons, and `.` or
  `..` segments;
- names that are not portable to every host: control characters, `< > " | ? *`,
  trailing dots or spaces, and Windows device names such as `CON`, `NUL` or `COM1`;
- entries that are equal or nested, in the sandbox or in their host mapping. Host
  paths are compared without regard to case and Unicode normalization, so
  `~/.cache` conflicts with `~/.CACHE/sessions`. `~` cannot be combined with
  anything below it.

On the host a directory is kept under `<abox state dir>/<sandbox>/`:

| Declaration | Path below the sandbox's state directory |
|---|---|
| `~/.config/opencode/state` | `.config/opencode/state` |
| `/var/lib/cache` | `var/lib/cache` |
| `~` or `/home/agent` | `home/agent` |

The sandbox name follows from the sandbox location, so a replaced sandbox finds its
state again. `create` validates all state entries before it changes anything.

## What abox checks

`abox build` resolves and checks every squad before it uses Docker. The error names
the file that is wrong:

- an unknown key in any YAML file;
- a cycle in a `base` chain;
- the inheritance errors listed [above](#the-squad-configuration-abox);
- an invalid state path, MCP name or MCP URL;
- a workload without a valid `publishRepository`;
- a build-phase argument passed to a local kit (see [Kits](authoring-kits.md)).

`abox publish` refuses a build whose sources changed since it was made. Run
`abox build` again first.
