# The standalone Copilot CLI reads its token from COPILOT_GITHUB_TOKEN and the tenant from
# GH_HOST, so it needs no /login of its own. The variable holds the proxy placeholder only.
if [ -n "${GHE_TOKEN:-}" ]; then
  export COPILOT_GITHUB_TOKEN="$GHE_TOKEN"
fi
