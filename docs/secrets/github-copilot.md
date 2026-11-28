# GitHub Copilot token (`abox-github-copilot`)

The `github-enterprise-auth` kit sends this token only to `copilot-api.rwe.ghe.com`.
OpenCode, pi, Claude and the Copilot CLI use it through the placeholder
`GHE_COPILOT_TOKEN`.

Copilot rejects personal access tokens, so the value is the OAuth token of `gh` on this
machine. Sign in once and print it:

```sh
gh auth login --hostname rwe.ghe.com
gh auth token --hostname rwe.ghe.com
```

Paste the output when the guided secrets setup asks for it. The proxy keeps this broader
token away from the tenant's API; `gh` and git inside the sandbox use the separate
[fine-grained token](../github-enterprise-token.md).
