# abox

`abox` gives each project folder its own AI agent sandbox, built on
[Docker Sandboxes](https://docs.docker.com/ai/sandboxes/). You create it from your
team's **squad** (a shared set of tools and configuration), then open a shell or the
Herdr desktop app to work in it. To use a published squad you need neither this
repository nor a local image build.

The sandbox works on your project files: **agent edits to the workspace change the host
folder**. Only give it files, services and credentials you trust it to use. A sandbox
does not protect against data loss or leaked secrets.

## Before you start

- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), to install the
  tool.
- [Docker Sandboxes (`sbx`)](https://docs.docker.com/ai/sandboxes/install/) 0.54.0 or
  later, on `PATH`. abox checks its health and sign-in and can open the Docker sign-in;
  for noninteractive use run `sbx login` first.
- Host Docker with Buildx and a running engine. Creating a sandbox needs it, also from a
  published squad; sbx alone is not enough.
- From your maintainer: the published **team**, **squad** and **tag**, the pull
  repository, and JFrog read access. The registry host is `rwest.jfrog.io`.

abox installs none of these. App forwarding and Herdr have extra requirements, listed
in their sections below.

## Install

The NuGet package is `RwestAgenticBox.Cli`; the command is `abox`. It is published to
the JFrog NuGet feed `mtiae-cs-nuget-prod`, not to nuget.org. Add the feed once with
your JFrog user and identity token (on Linux/macOS also pass
`--store-password-in-clear-text`):

```sh
dotnet nuget add source https://rwest.jfrog.io/artifactory/api/nuget/v3/mtiae-cs-nuget-prod/index.json --name mtiae-cs-nuget-prod --username <user> --password <identity token>
```

Then install, and later update or remove:

```sh
dotnet tool install --global RwestAgenticBox.Cli
dotnet tool update --global RwestAgenticBox.Cli
dotnet tool uninstall --global RwestAgenticBox.Cli
```

The global tools directory must be on `PATH`: `$HOME/.dotnet/tools` on Linux/macOS,
`%USERPROFILE%\.dotnet\tools` on Windows. If the package is not found, ask your
maintainer for read access to the feed, or
[build from source](docs/development.md#building-and-running-the-command-line).

## Quickstart

1. If your pull repository is not `mtiae-docker-release`, set `pullRepository` in your
   [user settings](docs/configuration.md#user-settings). It is a repository name, not a
   URL.
2. Sign in to the registry with your JFrog user and identity token. If you skip this,
   `abox create` starts the sign-in for you on a terminal.

   ```sh
   docker login rwest.jfrog.io
   ```

3. If the project already has an `abox.yaml` or `abox.user.yaml`, read it first,
   especially its [`userSecrets`](#copying-net-user-secrets) selection. Then create the
   sandbox:

   ```sh
   abox create --team <team> --squad <squad> --tag <tag> --location "/path/to/project"
   ```

   This pulls the published kit set, copies selected user-secrets and sets up the
   discovered app ports. It does not start your apps or open a shell.

4. Open it:

   ```sh
   abox run --location "/path/to/project"
   ```

abox cannot list the squads of a team; you have to know the names. `teams/sample` in
this repository is example configuration, not a published squad.

Creating from a published squad adds `rwest.jfrog.io/<pullRepository>/` to sbx's allowed
kit sources and keeps it there. See
[published sandboxes](docs/cli-reference.md#creating-local-and-published-sandboxes).

### Replacing a sandbox

When the folder already has a sandbox, `create` asks before it replaces it. `--yes`
skips the question, not the loss:

- **Lost:** files that exist only in the sandbox, installed changes, and worktrees under
  `~/worktrees`.
- **Kept:** the host workspace and the state directories the squad declares.

Updating the CLI or running `abox run` does not refresh a published squad. Create the
sandbox again to pull its tag again.

## Everyday use

Commands work on `--location`, else the user setting `defaultLocation`, else the current
folder.

| Task | Command |
|---|---|
| Open the default shell | `abox run` |
| Run a command instead of the shell | `abox run -- git status` |
| Open Herdr | `abox run herdr-app` |
| Keep your terminal's colors | `abox run --terminal-theme false` |
| List the credentials the squad's kits ask for | `abox secrets list` |
| Enter one of them | `abox secrets set --service <service>` |
| Copy the selected .NET user-secrets again | `abox user-secrets` |
| Scan app ports again and set up HTTPS | `abox ports` |

`run` starts a stopped sandbox but never creates one. It does not copy user-secrets or
redo the app setup.

The launchers are `shell` (default) and `herdr-app`. Herdr needs `herdr-app`, `hc` and
`abox` on the host `PATH`, and `hc` in the squad's sandbox. Install the desktop app from
[herdr.dev](https://herdr.dev).

All commands and options are in the [CLI reference](docs/cli-reference.md).

## Open your apps in a browser

Start the app inside the sandbox. abox forwards the ports it finds to the host, shifted
by an offset of 10000: an app on `http://localhost:8203` in the sandbox is
`http://localhost:18203` on the host. Forwarding binds to host loopback only.

abox finds ports in .NET `Properties/launchSettings.json`, in `angular.json`, and in
`ng` commands with a literal port in the `start` script of `package.json`. It does not
find other servers, such as Vite or Next. The squad must provide `python3` 3.8 or later.

After you change app ports, or when setup failed during creation, run:

```sh
abox ports --location "/path/to/project"
```

Set `portOffset` in the folder configuration for another offset. Mappings stay after the
command exits, and a new offset does not remove the old ones. See
[application ports](docs/application-ports.md).

For ASP.NET Core, abox copies your trusted host development certificate into the sandbox
so HTTPS works there. This needs an existing trusted certificate and the `dotnet
dev-certs` CLI on the host. See
[development HTTPS](docs/https-development-certificates.md).

## Configuration

Shared settings of a project go in `abox.yaml` in the project folder, personal ones in
`abox.user.yaml`. Add `abox.user.yaml` to `.gitignore` yourself.

```yaml
# abox.yaml
squad: your-squad
portOffset: 10000
```

Your own defaults, such as `defaultTeam`, `defaultTag` and `pullRepository`, go in the
user settings file `config.yaml`. Both are described in
[configuration](docs/configuration.md).

## Credentials

abox handles two different things:

- **Secrets** (`abox secrets`) are the host-side credentials a squad's kits declare.
  They stay on the host; sbx's proxy injects them. Values are global on the host, so
  removing one affects every sandbox. They are not Docker's registry sign-in.
- **User-secrets** (`abox user-secrets`) are .NET user-secret stores that abox copies
  into the sandbox.

### Copying .NET user secrets

Copying is opt-in: nothing is copied without a `userSecrets` selection in the folder
configuration. With one, abox copies when a sandbox is created or replaced and when you
run `abox user-secrets`.

**Read the selection before every copy or replacement.** The agent in the sandbox can
edit both folder configuration files, so it can change which host secrets the next copy
reads. Nothing on the host asks for approval. Copied secrets are readable inside the
sandbox and not encrypted. Each copy replaces the whole store in the sandbox.

Read [user-secrets](docs/user-secrets.md) before you enable this.

## Troubleshooting

- **No sandbox for the folder:** `run`, `ports` and `user-secrets` never create one. Use
  the `abox create` command they print.
- **sbx health or sign-in fails:** follow the printed hint; run `sbx login` if needed.
- **The pull fails:** check `docker login rwest.jfrog.io`, your JFrog read access, and
  the team, squad and tag.
- **Creation reports a failed copy, ports or HTTPS setup:** the new sandbox exists with
  partial setup. Fix the cause and run the printed retry commands: `abox user-secrets`
  first if copying failed, then `abox ports` with the same offset. Do not create the
  sandbox again just to retry setup.
- **Replacement failed after the old sandbox was removed:** the old sandbox is gone; its
  state directories are kept. Run the printed `abox create` command again.
- **More detail:** `abox <command> --help`, or add `--verbose` before any `--`. Read the
  tool output before you share it; it can contain sensitive data.

## More documentation

For users:

- [CLI reference](docs/cli-reference.md) and [configuration](docs/configuration.md)
- [Application ports](docs/application-ports.md) and
  [development HTTPS](docs/https-development-certificates.md)
- [User-secrets](docs/user-secrets.md)

For maintainers and contributors:

- [Authoring squads, kits and workloads](docs/authoring.md)
- [Development and local builds](docs/development.md)
- [How abox is built](docs/implementation.md) and the [glossary](GLOSSARY.md)
- [Coding standards](CODING_STANDARDS.md) and [proposals and open questions](docs/concept.md)
