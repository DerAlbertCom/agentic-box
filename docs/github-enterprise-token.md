# The GitHub Enterprise tokens

The `github-enterprise-auth` kit signs `gh`, git and the Copilot-backed agents
(OpenCode, pi, Claude, the Copilot CLI) in to the GitHub Enterprise Cloud tenant
`rwe.ghe.com`. It asks with `credential@1` for two services, each stored on the host by
its name:

| Service | Token | The proxy sends it to | Placeholder in the sandbox |
|---|---|---|---|
| `abox-github-enterprise` | fine-grained personal access token | `rwe.ghe.com`, `api.`, `uploads.rwe.ghe.com` | `GHE_TOKEN` |
| `abox-github-copilot` | `gh` OAuth token | `copilot-api.rwe.ghe.com` only | `GHE_COPILOT_TOKEN` |

Copilot's endpoint rejects personal access tokens ("Personal Access Tokens are not
supported for this endpoint"), and the tenant offers no Copilot permission for them. So
Copilot gets the `gh` OAuth token. That token has `gh`'s `repo` scope, but the proxy
sends it only to the Copilot endpoint: an agent that calls the tenant's API gets the
personal access token.

A fine-grained token can push and open pull requests without being allowed to create
or delete repositories. A classic `gh` token (`gho_…`) cannot be limited that way, so
abox refuses it.

## Store the personal access token with abox

You create the token yourself; abox never creates one and never opens a browser. When
the token is missing, `abox create --local` and `abox secrets set --service
abox-github-enterprise` ask for it. You need a terminal; the host needs no `gh`.

1. abox asks for the organization that owns the repositories, then prints the
   permissions below and the prefilled link
   `https://<tenant>/settings/personal-access-tokens/new?target_name=<organization>&…`.
   The link names the organization as the resource owner, so its organization
   permissions are prefilled too. Without an organization, the link leaves those out.
2. Open the link in your browser, choose **All repositories**, check the expiry, and
   generate the token.
3. Paste the token into abox. It is not shown.
4. abox checks it with `GET https://api.<tenant>/user`. If the tenant rejects it, abox
   says why and you can paste another one.
5. abox stores it in the host's secret store. The value never goes into process
   arguments or logs.

Without a terminal, `abox create` warns and names the command to run. A sandbox
created without the token still starts: `gh` and git fail against the tenant until you
store the token and recreate the sandbox.

## Store the Copilot token

On the host, sign `gh` in to the tenant once and print its token:

```sh
gh auth login --hostname rwe.ghe.com
gh auth token --hostname rwe.ghe.com
```

When the service is missing, `abox create --local` asks for it; paste the `gho_…`
token. Or store it at any time with:

```sh
abox secrets set --location <folder> --service abox-github-copilot
```

The token belongs to the host's `gh` login: `gh auth logout` or `gh auth refresh`
replaces it, and then you store it again. Without it the sandbox still starts, and the
Copilot-backed agents fail until you store it and recreate the sandbox.

## Permissions of the personal access token

In the order of GitHub's page: the repository permissions alphabetically, then the
organization's.

| Permission | Level | For |
|---|---|---|
| Actions | read and write | `workflow_dispatch`, read runs and logs |
| **Administration** | **none** | no repository creation or deletion |
| Commit statuses | read | see CI results |
| Contents | read and write | clone, pull, push, branches, tags |
| Discussions | read and write | |
| Issues | read and write | create and update issues |
| Metadata | read | mandatory |
| Pull requests | read and write | open and update pull requests |
| Workflows | read and write | push changes under `.github/workflows` |
| Projects (organization) | read and write | only when the organization owns the token |

Fine-grained tokens cannot use the Checks API, so there is no Checks permission. CI
results show through commit statuses and Actions runs. There is no Copilot permission
either; Copilot uses the [Copilot token](#store-the-copilot-token).

## Create it by hand

Open the template link below after replacing `<organization>`. It prefills the name,
the resource owner and every permission in the table:

```text
https://rwe.ghe.com/settings/personal-access-tokens/new?name=abox%20sandbox%20agents&description=Used%20by%20the%20agents%20in%20abox%20sandboxes&target_name=<organization>&actions=write&statuses=read&contents=write&discussions=write&issues=write&metadata=read&pull_requests=write&workflows=write&organization_projects=write
```

Choose all of the organization's repositories. Then store the token:

```sh
abox secrets set --location <folder> --service abox-github-enterprise
```

For a folder without a sandbox yet, `abox create --local` asks for the token before
it creates one.

## Expiry and renewal

This covers the personal access token; the `gh` OAuth token does not expire. abox reads the expiry date from the tenant's answer when it checks the token. From
seven days before that date, `abox create` and `abox run` warn. To renew, regenerate
the token on its settings page or create a new one, then store it again with `abox
secrets set --service abox-github-enterprise`. A running sandbox uses the new value with its
next request.

## The old `gh`-based token

Squads used to store a `gh auth token` as a custom secret under the variable
`RWE_GHE_TOKEN`, sent to all four hosts. The new tokens are services stored by their
names, so they live side by side with it: sandboxes from an older build keep using the old secret, and new ones use
`abox-github-enterprise` and `abox-github-copilot`. Remove the old secret once no sandbox needs it any more; abox
does not remove secrets it did not store, so use sbx and choose the entry with
`RWE_GHE_TOKEN`:

```sh
sbx secret rm
```

## Not covered

- **Wikis.** Pushing to `<repo>.wiki.git` with a fine-grained token is not confirmed, so
  the agents have no wiki write access.
- **github.com.** Public GitHub stays anonymous: `GH_TOKEN` and `GITHUB_TOKEN` are empty
  in the sandbox, and `gh` and git use the token only for the tenant.
- **Limiting the Copilot token.** `gh` always requests `repo`, `read:org` and `gist`,
  and they cannot be removed. Only the proxy's host list keeps that token to Copilot.
- **Other tenants.** The kit's hosts are fixed to `rwe.ghe.com`, because a kit's
  `credential@1` domains cannot come from a parameter. Another tenant needs its own copy
  of the kit.
