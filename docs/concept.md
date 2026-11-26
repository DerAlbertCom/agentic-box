# Concept: proposals and open questions

abox gives each folder its own AI agent sandbox on Docker Sandboxes (`sbx`). Users run
only `abox`; it calls `sbx` for them. Inside a sandbox, four agents (GitHub Copilot,
OpenCode, pi, Claude Code) are meant to work against GitHub Copilot on GitHub Enterprise
Cloud with data residency.

What is built is described in [implementation.md](implementation.md) and the
[CLI reference](cli-reference.md). This file keeps only what is **not built**: the
proposals and the questions nobody has answered yet.

## Proposals

### `orca` launcher

Orca (orca.dev) is a desktop agent IDE: each task gets a git worktree and agent
terminals. It reaches remote machines over SSH, and its CLI (`orca`) talks to the
running app. The launcher would:

1. Start `sshd` in the sandbox as root, through the internal exec. Idempotent.
2. Keep abox's SSH key pair in `ssh/` under the abox config directory and add its public
   key to the sandbox user's `authorized_keys`.
3. Publish sandbox port 22 on loopback (`sbx ports <sandbox> --publish 127.0.0.1::22`)
   and read the host port back.
4. Write an OpenSSH host entry `abox-<sandbox>` (127.0.0.1, that port, user `agent`, the
   key) to `ssh/config` in the abox config directory.
5. Open the workspace on that target through the Orca CLI (`--host ssh:<target-id>`
   exists; the exact command is not verified).

It needs `openssh-server` in the base image, and answers to open questions 6 to 8.

### Other proposals

- **`run` offers a newer published tag.** Today `abox run` offers a replacement only
  when a newer local build exists. A sandbox created from JFrog keeps its image until it
  is created again.
- **`prune` reaches further.** Also keep what a stopped sandbox uses, and prune the local
  builds of the build store.
- **File collisions between kits.** Resolving a squad rejects duplicate kit repositories,
  but not two different kits that write the same file.
- **Claude Code on Copilot.** Claude Code changes its backend only through
  `ANTHROPIC_BASE_URL` and `ANTHROPIC_AUTH_TOKEN`; it has no GitHub Copilot option. It
  therefore needs a gateway that also respects data residency.
- **GitHub Enterprise tenant in the user configuration**, default `rwe` (`rwe.ghe.com`).
- **Local overlays on published squads.** A user cannot add own kits to a published kit
  set.

## Open questions

1. Do any team members use Intel Macs? sbx supports Apple silicon only.
2. Which gateway gives Claude Code access to Copilot?
3. Does the credentials proxy cover `api.github.com`? If not, `mise` inside a sandbox
   hits anonymous rate limits.
4. Should `publish` move `latest`, or require an approval? Today it moves the chosen tag
   after it verified the pushed set, so a test publication must use its own `--tag`.
5. Worktrees under `~/worktrees` are lost when a sandbox is replaced. Declare them as
   state, or use a clone whose commits stay on the host through a git remote?
6. Orca takes SSH hosts through its own form, with a picker over `~/.ssh` config files.
   Should abox write the `abox-<sandbox>` entry into `~/.ssh/config` (with consent), or
   does the user add the Orca target once by hand?
7. Orca creates git worktrees inside the repository, which is mounted from the host.
   Accept that, or use a clone for Orca sandboxes?
8. Should every base image include `openssh-server`, or only squads whose users run
   `orca`?
