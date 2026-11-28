#!/usr/bin/env bash
# The current git from the git-core PPA instead of the distribution's older one.
set -euo pipefail

sudo add-apt-repository --yes --no-update ppa:git-core/ppa
sudo apt-get update
sudo apt-get install -y --no-install-recommends git
sudo rm -rf /var/lib/apt/lists/*
