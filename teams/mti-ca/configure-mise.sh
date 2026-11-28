#!/usr/bin/env bash
# mise itself: quiet, offline-friendly settings, a system-wide binary and fresh shims.
set -euo pipefail

mise settings set auto_update_check_duration 365d
mise settings set use_versions_host false
mise reshim
mise_binary=$(command -v mise)
if [ "$mise_binary" != /usr/local/bin/mise ]; then
  sudo ln -sf "$mise_binary" /usr/local/bin/mise
fi
mkdir -p "$HOME/.local/bin"
ln -sf "$HOME/.local/share/mise/shims/herdr" "$HOME/.local/bin/herdr"
rm -rf "$HOME/.cache/mise" "$HOME/.local/share/mise/downloads"
