# Authoring kits

A kit gives the agents of a sandbox a capability: a configuration file, a reachable
host, a credential or a tool. A squad lists its kits under `kits` in its
[`.abox` file](authoring.md#the-squad-configuration-abox). The kit descriptor follows
Docker's [kit spec](https://github.com/docker/sandbox-kit-spec); abox adds nothing to
that format.

## Local and registry kits

```yaml
kits:
  - pi-config                                # local: the directory kits/pi-config
  - docker.io/acme/elastic-auth:1.0          # registry: resolved by sbx
  - name: themes                             # with arguments
    args:
      THEME: nord
```

- A name without a slash or colon is a **local kit**: a directory in the `kits`
  folder. `abox build` builds it into a component image.
- A name with a slash or colon is a **registry kit**. abox does not build it.
  `abox build` pins it to the digest its tag resolves to, and `create --local` and
  `publish` use that digest.
- Two kits of the same repository in one `base` chain are an error, even with
  different tags.

## A local kit

A local kit is a directory with a descriptor named after it:

```yaml
# kits/pi-config/pi-config.yaml
# syntax=docker/sandbox-kit:3
schemaVersion: "3"
kind: mixin
displayName: pi configuration
version: "1.0.0"

capabilities:
  - type: com.docker.sandbox/agent-context@2
    config:
      directory: /home/agent/.pi/agent
      filename: AGENTS.md
      content: |
        You work in a sandbox.
```

abox looks for `<directory-name>.yaml`, then `spec.yaml`, then the first `*.yaml` in
the directory. Use `<directory-name>.yaml`.

The kits in [`kits/`](../kits/README.md) are working examples: `pi-config` writes an
agent configuration, `github-enterprise-auth` sets up `gh`, git and the agents with a
custom secret and renders their configs for the tenant host from a kit argument, and
`sbox` and `themes` use a build step.

## Kit arguments

`args` on a kit entry are passed to the kit as the kit spec defines. Values can use
`${{ parameters.NAME }}`.

- A local kit takes only create-phase arguments, because its image is built without
  arguments. `abox build` rejects an argument that the descriptor declares as
  `buildArg` or `buildSecret`.
- If two local kits get an argument with the same key, the values must be equal.
- Never put credentials in kit arguments. They are stored in the published set, which
  everyone with read access can read. Use [secrets](authoring-secrets.md).

## Kit build step

A local kit with an executable `build` file is built before its component image is
made. Use it for a payload that is compiled, such as TypeScript, so no build output is
committed.

1. abox copies the kit folder into the build directory,
   `<state dir>/builds/<team>/<squad>/<tag>/kits/<kit>/`.
2. abox runs `build <stagedKitDir>` with the kit's folder in the repository as the
   working directory. The script writes its output into the staged folder.
3. abox builds the component image from the staged copy. The folder in the repository
   is not changed.

```sh
#!/bin/sh
# kits/example/build
set -eu
staged="$1"
mkdir -p "$staged/files/home/.local/share/example"
cp ../../example/tool.sh "$staged/files/home/.local/share/example/"
```

Rules:

- The script must be POSIX `sh` and committed as executable (`chmod +x`). On Linux and
  macOS a `build` file that is not executable fails the build.
- On Windows abox runs it with the `sh` of Git for Windows. Paths handed to Docker
  must be Windows paths there: take them with `pwd -W 2>/dev/null || pwd`.
- A non-zero exit fails `abox build` and shows the script's output.
- The staged output must not contain symbolic links.
- The step runs only when the kit's image is rebuilt: the kit's folder changed, the
  image left the image store, or `--rebuild` was given. Changes to files outside the
  kit's folder that the script reads (such as `sbox-cli/`) are not detected. Run
  `abox build --rebuild` after changing them.

`kits/sbox/build` and `kits/themes/build` are real examples. They bundle a TypeScript
package with `sbox-cli/bundle`, which runs Bun in a pinned container, so the host
needs only Docker. `sbox-cli/bundle` passes a certificate file named in
`NODE_EXTRA_CA_CERTS` (such as the chain of a TLS-inspecting proxy) into that
container.

The reasons are in [ADR 0001](adr/0001-kit-build-step.md) and
[ADR 0006](adr/0006-build-produces-images-publish-pushes-them.md).
