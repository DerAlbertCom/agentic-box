# GitHub Copilot token (`abox-github-copilot`)

The `github-enterprise-auth` kit sends this token only to `copilot-api.rwe.ghe.com`.
OpenCode, pi, Claude and the Copilot CLI use it through the placeholder
`GHE_COPILOT_TOKEN`.

Copilot rejects personal access tokens, so the value is the OAuth token of `gh` on this
machine. Sign in once:

```sh
gh auth login --hostname rwe.ghe.com
```

The guided secrets setup and `abox secrets set` then run
`gh auth token --hostname rwe.ghe.com` themselves and store its output; nothing is asked.
After a new `gh auth login`, store the new token with
`abox secrets update --service abox-github-copilot`. The proxy keeps this broader
token away from the tenant's API; `gh` and git inside the sandbox use the separate
[fine-grained token](../github-enterprise-token.md).
