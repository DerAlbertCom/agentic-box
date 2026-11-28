#!/usr/bin/env bash
# The Azure DevOps extension, and no prompts when another extension is needed.
set -euo pipefail

az extension add --name azure-devops --yes
az config set extension.use_dynamic_install=no_prompt
