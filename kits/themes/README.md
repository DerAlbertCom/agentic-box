# themes

The `themes` kit installs the `sbox themes` command (from `sbox-cli/sbox-themes/`)
with every theme and app template. It switches the color theme of all supported apps
in a sandbox at once, without recreating the sandbox. It needs the
[`sbox` kit](../README.md#sbox).

## Using it in a squad

```yaml
kits:
  - sbox
  - name: themes
    args:
      THEME: nord          # the team's default; the user's choice wins

state:
  - ~/.local/state/sbox    # keeps the user's choice when the sandbox is replaced
```

The current theme is the user's saved choice, then the kit argument `THEME`, then
`tokyo-night`. The choice is saved in `~/.local/state/sbox/themes/current`. A saved or
configured theme that is not installed gives a warning and the next fallback.

## Commands

| Command | What it does |
|---|---|
| `sbox themes <name>` | Switches to the theme: renders every app, saves the choice, recolors the terminal. |
| `sbox themes` | In a terminal, opens an `fzf` picker with a color preview; Esc changes nothing. Otherwise like `--list`. |
| `sbox themes --list` | Lists the themes; `*` marks the current one. |
| `sbox themes --current` | Prints the current theme. |
| `sbox themes --apply` | Renders the current theme again without saving. The kit's install and startup hooks run it. |

A switch is all or nothing up to the point of writing. The palette and every app
template are read and rendered in memory first; an invalid palette, a failing
template, a malformed `app.yaml` or a settings file that is not a JSON object stops
with exit code 1 and changes nothing. Then the files are written, each atomically,
and the apps' commands run. A failing command is reported and fails `sbox themes`, but
the switch is kept.

## Themed apps

| App | File in the sandbox | Selected by |
|---|---|---|
| OpenCode | `~/.config/opencode/themes/sbox.json` | `theme` in `~/.config/opencode/cli.json` (merged) |
| Claude Code | `~/.claude/themes/sbox.json` | `theme: custom:sbox` in `~/.claude/settings.json` (merged) |
| pi | `~/.pi/agent/themes/sbox.json` | `theme: sbox` in `~/.pi/agent/settings.json` (merged) |
| bat | `~/.config/bat/themes/sbox.tmTheme` | `BAT_THEME=sbox`, set by the kit; a switch rebuilds bat's cache |
| delta | `~/.config/git/sbox.gitconfig` (only `[delta]`) | an `[include]` in `~/.config/git/config`, added once |
| fzf | `~/.config/fzf/sbox` | `FZF_DEFAULT_OPTS_FILE`, set by the kit |
| lazygit | `~/.config/lazygit/sbox.yml` | `LG_CONFIG_FILE`, set by the kit: `config.yml`, then `sbox.yml` |
| yazi | `~/.config/yazi/theme.toml` | the file itself |
| terminal | `~/.config/sbox/terminal-colors.sh` | see [Terminal colors](#terminal-colors) |

Every app gets one fixed theme named `sbox`, so a switch only rewrites that theme's
content. Terminal tools pick it up on their next run; the agents may need a restart.

A switch overwrites only the theme files above and merges its keys into the listed
settings files. Your other configuration stays yours: the first switch only adds the
`[include]` to the git config and creates an empty lazygit `config.yml` if there is
none. The theme wins for the keys it sets; `~/.gitconfig` is read last and wins over
both.

## Terminal colors

The `terminal` app renders the terminal color script
`~/.config/sbox/terminal-colors.sh`. It prints the escape sequences (OSC 4, 10, 11,
12) that set the 16 ANSI colors, foreground, background and cursor of the current
terminal session. It never changes the host terminal's configuration.

The terminal is recolored only when `SBOX_TERMINAL_THEME=1` is set and the output is a
terminal. `abox run` sets the variable unless the user turned terminal colors off.

- **At login.** The kit installs `/etc/profile.d/sbox-terminal-theme.sh`. A login
  shell (`bash -l`) runs the script and resets the colors when the shell exits.
- **On a switch.** `sbox themes <name>` and a picker selection recolor the terminal
  they run in. Other terminals of the sandbox follow at their next login.

Good to know:

- Run the script by hand after a program reset the colors:
  `sh ~/.config/sbox/terminal-colors.sh`.
- The reset is an `EXIT` trap set at login. A `~/.bashrc` that sets its own `EXIT`
  trap replaces it, and the colors stay after exit.
- A login on a fresh sandbox before the startup hook has rendered the script gets no
  colors.
- To try it without `abox run`:
  `sbx exec -it -e SBOX_TERMINAL_THEME=1 <sandbox> bash -l`.

The reasons are in [ADR 0002](../../docs/adr/0002-terminal-colors-from-the-sandbox.md).

## Adding a theme

A theme is a folder `sbox-cli/sbox-themes/themes/<name>/` with a `colors.toml`, plus
its `LICENSE` if it has its own. The palettes come unchanged from
[Omarchy](https://github.com/basecamp/omarchy) (`themes/*/colors.toml`), plus
`osaka-jade` from its own repository. Refresh them with:

```sh
cd sbox-cli/sbox-themes && bun run sync-themes
```

The sync adds new themes and updates changed ones. It never deletes, and it reports
themes that upstream removed. It writes nothing if a downloaded palette is invalid.
Review the diff before committing.

A theme of your own is a new folder with a `colors.toml` in the same format:

- Every color is `#rrggbb`. `mode` (or `theme_type`) is `dark` or `light`; if absent
  it is derived from the background.
- The minimum is `background`, `foreground` and the six colors `red`, `green`,
  `yellow`, `blue`, `magenta` and `cyan` (or the ANSI colors `color0`–`color15` they
  fall back to).
- Everything else has upstream's fallbacks: `orange` falls back to `yellow`, a
  `bright_*` color is its base mixed 20% with white, `dark_background` and
  `darker_background` are the background mixed 25% and 50% with black.
- Keys outside the palette, such as window border colors, are ignored.

A theme whose palette is still incomplete or invalid after the fallbacks is refused
with an error that names the theme and the key. `--list` and `--current` still show
it, because they do not read palettes.

## Adding an app

An app is a folder `sbox-cli/sbox-themes/apps/<app>/` with an `app.yaml` and Liquid
templates. No TypeScript changes are needed.

```yaml
files:                          # templates rendered to files, relative to the app folder
  - template: sbox.json.liquid
    target: ~/.config/opencode/themes/sbox.json
settings:                       # merged into JSON files on every apply; strings are Liquid
  - file: ~/.config/opencode/cli.json
    merge:
      theme: { name: sbox, mode: "{{ mode }}" }
run:                            # shell commands run from the home folder afterwards
  - bat cache --build
```

Every key is optional, and an unknown key is an error. Targets start with `~/`. A
template sees every color of the palette as `#rrggbb`, and `mode`.

| Filter | Example | Result |
|---|---|---|
| `mix` | `{{ background \| mix: foreground, 30 }}` | The color 30% of the way to `foreground`; `"30%"` also works. |
| `strip` | `{{ accent \| strip }}` | `7aa2f7` |
| `rgb` | `{{ accent \| rgb }}` | `122,162,247` |

A template that uses a key outside the palette fails the switch before anything is
written.
