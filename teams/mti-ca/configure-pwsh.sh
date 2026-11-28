#!/usr/bin/env bash
# PowerShell modules for completion, YAML and tests.
set -euo pipefail

pwsh -NoLogo -NoProfile -Command '
  $ErrorActionPreference = "Stop"
  # PSResourceGet 1.3 (PowerShell 7.7) also registers MicrosoftArtifactRegistry ahead of PSGallery,
  # and its "Not Found" fails the install instead of falling through, so name the gallery.
  Install-PSResource -Name CompletionPredictor -Version "[0.1.1]" -Repository PSGallery -Scope CurrentUser -TrustRepository
  Install-PSResource -Name powershell-yaml -Version "[0.4.12]" -Repository PSGallery -Scope CurrentUser -TrustRepository
  Install-PSResource -Name Pester -Version "[6.2.0]" -Repository PSGallery -Scope CurrentUser -TrustRepository
'
