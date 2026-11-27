# The GitHub Enterprise token

The `github-enterprise-auth` kit signs `gh`, git and the Copilot-backed agents
(OpenCode, pi, Claude, the Copilot CLI) in to one GitHub Enterprise Cloud tenant, such
as `rwe.ghe.com`. They all use one **fine-grained personal access token**: the
`abox-github-enterprise` secret. The token stays on the host. sbx's proxy sends it to the
tenant's hosts, and the sandbox only sees a `github_pat_…` placeholder in `GHE_TOKEN`.

A fine-grained token can push and open pull requests without being allowed to create
or delete repositories. A classic `gh` token (`gho_…`) cannot be limited that way, so
abox refuses it.

## Store it with abox

You create the token yourself; abox never creates one and never opens a browser. When
the secret is missing, `abox create --local` and `abox secrets set --service
abox-github-enterprise` ask for it. You need a terminal; the host needs no `gh`.

1. abox prints the permissions below and the prefilled link
   `https://<tenant>/settings/personal-access-tokens/new?…`.
2. Open the link in your browser, choose your organization as the resource owner and
   **All repositories**, check the expiry, and generate the token.
3. Paste the token into abox. It is not shown.
4. abox checks it with `GET https://api.<tenant>/user`. If the tenant rejects it, abox
   says why and you can paste another one.
5. abox stores it in the host's secret store. The value never goes into process
   arguments or logs.

Without a terminal, `abox create` warns and names the command to run. A sandbox
created without the token still starts: `gh`, git and the Copilot-backed agents fail
against the tenant until you store the token and recreate the sandbox.

## Permissions

| Permission | Level | For |
|---|---|---|
| Metadata | read | mandatory |
| Contents | read and write | clone, pull, push, branches, tags |
| Pull requests | read and write | open and update pull requests |
| Issues | read and write | create and update issues |
| Actions | read and write | `workflow_dispatch`, read runs and logs |
| Workflows | read and write | push changes under `.github/workflows` |
| Commit statuses | read | see CI results |
| Discussions | read and write | |
| Projects (organization) | read and write | |
| Copilot requests (account) | write | Copilot for OpenCode, pi, Claude and the Copilot CLI |
| **Administration** | **none** | no repository creation or deletion |

Fine-grained tokens cannot use the Checks API, so there is no Checks permission. CI
results show through commit statuses and Actions runs.

## Create it by hand

Open the template link below, with your tenant in place of `<tenant>`. It prefills the
name and every permission in the table:

```text
https://<tenant>/settings/personal-access-tokens/new?name=abox%20sandbox%20agents&description=Used%20by%20the%20agents%20in%20abox%20sandboxes&metadata=read&contents=write&pull_requests=write&issues=write&actions=write&workflows=write&statuses=read&discussions=write&organization_projects=write&copilot_requests=write
```

Choose the organization as the resource owner and all of its repositories. Then store
the token:

```sh
abox secrets set --location <folder> --service abox-github-enterprise
```

For a folder without a sandbox yet, `abox create --local` asks for the token before
it creates one.

## Expiry and renewal

abox reads the expiry date from the tenant's answer when it checks the token. From
seven days before that date, `abox create` and `abox run` warn. To renew, regenerate
the token on its settings page or create a new one, then store it again with `abox
secrets set --service abox-github-enterprise`. A running sandbox uses the new value with its
next request.

## Replacing the old `gh`-based token

Squads used to store a `gh auth token` as the secret `rwe-ghec`, under the variable
`RWE_GHE_TOKEN` (`gho_{rand}`). Its hosts are the same as the new secret's, so abox
reports it as a **conflicting binding** and does not overwrite it.

Remove it before you recreate the sandbox, while the folder's sandbox still comes from
the old build that declares `rwe-ghec`:

```sh
abox secrets list --location <folder>
abox secrets remove --location <folder> --service rwe-ghec
```

The stored value serves every sandbox, so this removes it for all of them. Then
recreate the sandbox with `abox create --local` and store the new token as described
above.

## Not covered

- **Wikis.** Pushing to `<repo>.wiki.git` with a fine-grained token is not confirmed, so
  the agents have no wiki write access.
- **github.com.** Public GitHub stays anonymous: `GH_TOKEN` and `GITHUB_TOKEN` are empty
  in the sandbox, and `gh` and git use the token only for the tenant.
- **Other tenants.** Kit network rules are static, so every `*.ghe.com` tenant is
  reachable from the sandbox. The token is only sent to the hosts in the secret's
  declaration.
