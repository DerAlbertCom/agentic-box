# The standalone Copilot CLI reads its token from COPILOT_GITHUB_TOKEN and the tenant from
# GH_HOST, so it needs no /login of its own. The variable holds the placeholder of the
# abox-github-copilot service, which the proxy replaces for the Copilot endpoint only.
if [ -n "${GHE_COPILOT_TOKEN:-}" ]; then
  export COPILOT_GITHUB_TOKEN="$GHE_COPILOT_TOKEN"
fi
