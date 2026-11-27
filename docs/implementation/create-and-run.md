# Create, run, secrets and prune

How the sandbox commands work. For build and publish see
[build-and-publish.md](build-and-publish.md).

## Finding the sandbox of a location

The sandbox location is `--location`, then `defaultLocation`, then the current folder.
`Sandboxes/FolderSandbox` is the single rule that maps it to an existing sandbox. It is
used by `create`, `run`, `ports`, `user-secrets` and `secrets`. It lists the sandboxes
once and tries, in order:

1. **The current name**, `<folder>-abox-<hash>`. The hash is the first five hex
   characters, uppercase, of SHA-256 over the absolute path with forward slashes,
   lowercased on Windows.
2. **The legacy name** that cape-agent-sandbox generated, `<folder>-ai-sbx-<hash>`, only
   when the current name is not found. See below.
3. **On Windows, the recorded workspace.** If neither name exists, the sandbox whose
   primary host workspace (`workspaces[0]`) is this folder, compared case-insensitively,
   is accepted when its name is the legacy name for that recorded spelling. This
   recovers sandboxes created with a different path casing. Several matches are an
   ambiguity error.

A new sandbox always gets the current name. The caller keeps the actual name it found
and uses it for every later step: records, probes, the launcher, transfers and
removal. When `run` replaces a sandbox, it hands the already selected sandbox to the
creation, so a second candidate that appears after the user consented cannot redirect
the removal.

A failed listing is an error. It never counts as "no sandbox" and never leads to a
create. `run`, `ports`, `user-secrets` and `secrets` fail without a sandbox and print
the `abox create` command to run, with team, squad, tag and `--local` from the sandbox
record or build record when known (`Sandboxes/CreateRecoveryHints`).

The hidden `exec` and `pipe` commands take a sandbox name and use no location lookup.

### Legacy names

`Sandboxes/LegacySandboxName` reproduces the names of cape-agent-sandbox so existing
sandboxes are found. Lookup migrates nothing: no state layout, no Herdr registration.

- Folder part: in the last path segment, every character that is not an ASCII letter,
  digit, underscore or hyphen becomes a hyphen. Then lowercase, trim hyphens, and use
  `root` when empty.
- Hash: three bytes of SHA-256 over the UTF-8 real path, as six lowercase hex
  characters.
- Real path on Windows: `Path.GetFullPath`, then the final directory link target when
  there is one. Case and separators are kept.
- Real path elsewhere: the last output line of
  `bash -c 'cd "$1" 2>/dev/null && pwd -P' bash <path>`, or the given path on failure.

The real path comes through `IFolderRealPath`, so the naming code stays pure.

## Create

`create` never reads team sources. Its steps, in order:

1. **Read the folder configuration** once (`abox.yaml`, `abox.user.yaml`). This snapshot
   is used to the end; create never reloads it. The team is `--team`, the folder's
   `team`, then `defaultTeam`. The squad is `--squad` or the folder's `squad`; a
   `--squad` that conflicts with `abox.yaml` fails.
2. **Prepare user-secrets.** Snapshot the selected host stores and check them against
   the deny list, before anything is replaced. Create never forces a deny-list match:
   neither `--yes` nor the replacement consent is an override.
3. **Prepare application ports.** Discover and validate the port plan, also before
   anything is replaced.
4. **Load the squad** (see the two paths below) and validate its state and MCP
   declarations.
5. **Find an existing sandbox** and ask before replacing it, listing what is lost. A
   refusal changes nothing.
6. **Check credentials** (local sources only), register missing MCP servers and prepare
   the host state directories.
7. **Create the sandbox** with `sbx create`, after removing the old one.
8. **Save the sandbox record** in `<state dir>/sandboxes/<sandbox>.json`: team, squad,
   build time and the pulled reference. Team and squad are also saved to
   `abox.user.yaml` unless the folder configuration already gives them.
9. **Copy the prepared user-secrets.**
10. **Set up application ports and HTTPS.**

A failure in step 9 or 10 keeps the new sandbox and its record, returns 1 (130 on
cancellation) and prints the `abox user-secrets` or `abox ports` command to retry. It
never recreates the sandbox. Details are in
[user-secrets-and-ports.md](user-secrets-and-ports.md).

### From a published kit set

Reference: `rwest.jfrog.io/<pullRepository>/agentic-box-rwest-<team>/<squad>:<tag>`.
`pullRepository` is a user setting, `mtiae-docker-release` by default, and independent
of the workload's `publishRepository`.

- **Kit source trust.** sbx only accepts kits from `kit.allowedSources`. Create reads
  the setting and appends `rwest.jfrog.io/<pullRepository>/` when no existing prefix
  covers it. It keeps all entries and never adds a wildcard or the whole host. sbx has
  no atomic append, so abox serializes its own updates and checks the value after
  writing. This is a persistent change to the user's sbx settings.
- **Registry sign-in.** Before the first registry read, create checks Docker's stored
  sign-in to `rwest.jfrog.io` (see [the registry sign-in](#the-registry-sign-in)).
- **Squad metadata.** Create resolves the tag with Buildx, selects the host's Linux
  platform manifest, checks its v3 workload descriptor and pulls the digest-pinned
  configuration mixin. It reads `squad.json` from a `docker image save` archive,
  without extracting files or running a container. Invalid or missing metadata means
  the squad must be published again; there is no local fallback.
- **sbx create** gets the immutable platform digest and `--pull always`. sbx's default
  for kit sets, `missing`, would never refresh a tag. The set already contains its
  mixins and arguments.

### From a local build (`--local`)

Create loads the build record and reads `squad.json` from the local config image the
same way. Before anything changes it checks that every component image is still in the
recorded image store with its recorded image ID. Otherwise it fails and says to run
`abox build`. Create never builds from sources.

sbx resolves OCI kit references only from the registry, even with `--pull never`. So
create writes **thin directory kits** into `<build dir>/create/<component>`:

- the workload: its descriptor plus `FROM <ref>:<tag>@<digest>`;
- each mixin: its descriptor plus `FROM scratch` and
  `COPY --from=<ref>:<tag>@<digest> / /`, because sbx keeps only the layers a mixin
  recipe adds on top of its `FROM`.

The tag in the reference matters. BuildKit finds a local image by its tag, but looks
up a digest-only reference in the registry, where an unpublished component does not
exist. Registry kits are passed pinned by digest and can still need the network, so
`--local` is not an offline guarantee.

sbx receives `SBX_KIT_BUILDER=host`, `BUILDX_BUILDER=<recorded context>` and
`DOCKER_CONTEXT=<recorded context>`, so the thin build sees the image store of the
build. It gets `--kit` per mixin, `--kit-arg` per argument and `--pull never`. Local kit
arguments with the same key must have equal values.

### Common to both

- `--mount <host>:<path>` per state directory, under `<state dir>/<sandbox>/`.
  `Squads/StateEntry` owns the path and overlap rules; the whole stored list is
  validated before any change. Rules are in [authoring.md](../authoring.md).
- `--static-mcp <names>` with the validated MCP servers.
- `--no-input`. abox reads nothing from the terminal while sbx runs, so sbx must never
  wait for an answer.
- sbx's progress is shown live, with known secrets redacted. After success, abox
  repeats sbx's `Note:` lines, such as a credential that was not injected.
- On failure, the sbx invoker builds a headline from the first failed kit step and its
  captured output, else from the last `error:` line. abox then says whether the old
  sandbox was already removed (its state directories are kept) and prints the create
  command to retry.

### The registry sign-in

`Registry/RegistrySignIn` runs before `create` reads a published set and before
`publish` pushes. The Docker adapter (`IRegistryLogin`) runs `docker login
rwest.jfrog.io` with an empty standard input: Docker then tries only its stored
credentials and fails when there are none or the registry rejects them. On a terminal,
abox then runs `docker login rwest.jfrog.io` attached to it, so the user signs in with
their JFrog user and identity token. Without a terminal, or when that sign-in fails,
the command stops and names the `docker login` command to run. Docker keeps the
credentials; abox never sees them.

### The credential check

When the squad's local sources are available, create checks every `credential@1`
request with an `apiKey` mechanism of a local kit:

- the stored value, with the identity rules of `abox secrets list`;
- the approval in sbx's `credentials.yaml` (`ICredentialApprovals`), which must cover
  every `apiKey.inject[].domain`.

On a terminal it asks for a missing value and for each missing approval; an approval
merges the domains into the file and keeps every other entry. Without a terminal it
warns and prints the `abox secrets set` commands. The check never fails a create.

## Run

1. Resolve the location, check the launcher's host programs and load the folder
   configuration.
2. Require the folder's sandbox. `run` never creates one.
3. For a locally created sandbox, offer a replacement when the build record of its
   recorded team, squad and tag is newer than the build time in the sandbox record. An
   accepted replacement goes through the same `SandboxCreator` as `create`, including
   user-secrets and port setup. With a `squad` in the folder configuration, it requires
   a sandbox record with that squad. Another team's build is never offered. A pulled
   sandbox gets no such offer; recreate it to refresh its tag.
4. Check the launcher's programs in the sandbox. A missing one means the image predates
   the launcher: the user is told to create the sandbox again.
5. Ask the runtime for `WORKSPACE_DIR` and start the launcher.

An ordinary run copies no user-secrets, sets up no ports and does not check the squad.
`sbx exec` starts a stopped sandbox, so there is no separate start step.

**Terminal colors.** The launcher session sets `SBOX_TERMINAL_THEME=1` on every exec
unless colors are off: `--terminal-theme`, then `terminalTheme` in `abox.user.yaml`,
`abox.yaml`, the user `config.yaml`, then on. abox emits no escape sequences; see
[ADR 0002](../adr/0002-terminal-colors-from-the-sandbox.md).

### Launchers

A launcher implements `ILauncher`: an id, a summary, the programs it needs on the host
and in the sandbox, and a run method that gets a `LauncherSession` (location, sandbox,
workspace, argv, exec in the sandbox). It returns an exit code.

- **`shell`** runs `bash -l` in the workspace with a terminal, or the command after `--`,
  and returns its exit code.
- **`herdr-app`** needs `herdr-app` and `hc` on the host and `hc` in the sandbox. It
  registers the sandbox as an `hc` machine reached through the hidden `abox pipe`
  command instead of SSH, starts the desktop app if needed and opens a space in the
  workspace. The machine name is the sandbox name, compared exactly. The launcher opens
  a space only after the app confirmed it is ready. A failed or malformed discovery is
  an error, not "machine absent"; configuration diagnostics in a valid `errors` list
  only warn.

`abox pipe` and `abox exec` run a command in a named sandbox. `pipe` passes bytes both
ways untouched. Neither is listed in `--help`.

## Secrets

`abox secrets list|set|remove` manages the host-side values the kits of an existing
sandbox ask for. It needs no team sources.

**What it reads.** One `sbx exec` returns every `/usr/share/sandbox/kit/*/kit.yaml` and
`/usr/share/abox/squad.json` (`ISandboxSquadFileReader`). Each file travels base64 on
its own line, so no content can break the framing. The descriptors give the
`credential@1` requests; `Secrets` in `squad.json` gives the declarations. Both are
parsed with the code that build and the create-time check use. A sandbox without
`squad.json` or `Secrets` has no declarations.

**Declarations never carry a host command.** For a command-backed secret, `squad.json`
holds only `FromCommand`. `set` does not run anything for it: it prints the
`sbx secret set-custom` command line to run by hand and says where to find the command.

**Identity** (`Credentials/SecretIdentity`). A service secret is matched by name. A
custom secret with hosts is matched when a stored custom secret is sent to all its
hosts; one with only `env` by that variable. `list` marks a stored custom secret that
shares a host or variable without being the binding as `conflicting binding`, and
`set` refuses to store over one.

- `list` shows requested and declared services, the kits asking, whether each is
  required, and whether a value is stored.
- `set` asks for the declared or named services. A service a kit asks for can be
  stored without a declaration, with a default prompt. A custom service (`host`/`env`)
  that is not declared is refused, because nothing else tells abox it exists.
- `remove` takes values off the host. Values are global on the host, so this affects
  other sandboxes.

The value goes to sbx on standard input as UTF-8 without a BOM or newline. It never
appears in process arguments, the environment or shell history.

## Prune

sbx stores every kit it builds as a template image: one per assembled set plus one per
kit, and the kit sources in their own repository. A build replaces them and sbx never
removes the old ones.

`abox prune` lists the template images, groups the assembled images by build id (the
tags `<id>`, `<id>-kit-N` and `<id>-template`), keeps the newest set with the kit
sources it uses and removes the rest with `sbx template rm`, one image at a time.

- `--full` removes every eligible image. What a sandbox uses, sbx refuses; those
  refusals are reported and do not fail the command.
- `--sbx` stops the running sandboxes first.

Prune is conservative:

- Only the repositories `sandbox-kit-assembled` and `sbx-kit-src` count, bare or as the
  last path segment of a qualified name. A suffix match such as `other-sbx-kit-src`
  does not.
- An image without a reported age or image id stays. If an assembled image has
  uncertain metadata, its whole set stays and so do all kit sources.
- Sets tied for newest all stay.
- It never calls `sbx prune` or `sbx reset`, and never touches secrets, other
  repositories, local builds or registry images.

The prune service works on domain kinds through `ISandboxCleanup`; the sbx adapter
classifies the images.
