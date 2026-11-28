#!/usr/bin/env bash
# The interactive bash: locale, editor, prompt, completions and the wrappers first on PATH.
set -euo pipefail

mkdir -p "$HOME/.local/share/fzf-tab-completion"
curl -fsSL https://raw.githubusercontent.com/lincheney/fzf-tab-completion/7014e0a7cd68fe3530e2f58c45740d17e98f05b8/bash/fzf-bash-completion.sh \
  -o "$HOME/.local/share/fzf-tab-completion/fzf-bash-completion.sh"
printf '%s\n' 'set completion-ignore-case on' > "$HOME/.inputrc"
cat >> "$HOME/.bashrc" <<'BASH'

export LANG=C.UTF-8 LC_ALL=C.UTF-8 EDITOR=edit VISUAL=edit
export STARSHIP_LOG=error CARAPACE_MATCH=2
. /opt/sandbox/startup/dotnet-env.sh
command -v mise >/dev/null 2>&1 && eval "$(mise activate bash)"
command -v starship >/dev/null 2>&1 && eval "$(starship init bash)"
command -v carapace >/dev/null 2>&1 && source <(carapace _carapace)
if command -v fzf >/dev/null 2>&1 && [ -r ~/.local/share/fzf-tab-completion/fzf-bash-completion.sh ]; then
  bind 'set show-all-if-ambiguous off'
  bind 'set show-all-if-unmodified off'
  source ~/.local/share/fzf-tab-completion/fzf-bash-completion.sh
  bind -x '"\t": fzf_bash_completion'
fi
_abox_wrappers_path() {
  local entry next="$HOME/.local/bin"
  local -a entries
  IFS=: read -r -a entries <<< "$PATH"
  for entry in "${entries[@]}"; do
    [[ "$entry" == "$HOME/.local/bin" ]] || next+=":$entry"
  done
  export PATH="$next"
}
PROMPT_COMMAND+=(_abox_wrappers_path)
_abox_wrappers_path
BASH
