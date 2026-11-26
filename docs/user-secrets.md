# Copying .NET user-secrets into a sandbox

abox can copy .NET user-secret stores from the host into a sandbox, so applications
started there find their development configuration. It is opt-in per sandbox location
and copies only the stores and keys you select.

This is not `abox secrets`, which manages the host-side credentials that kits declare
and the sbx proxy injects. Those never enter the sandbox.

## Safety

Read this before you enable the feature.

- **The selection is writable by the agent.** `abox.yaml` and `abox.user.yaml` lie in
  the workspace. An agent edit changes which host stores and keys the next copy reads.
  Review both files before every `create`, replacement and `abox user-secrets`. There
  is no separate host-side approval.
- **Copied values are plain files.** The sandbox user and root can read them. They are
  not encrypted.
- **The deny list is a safeguard, not a detector.** It blocks the substrings you list.
  It does not recognize production secrets and does not make an untrusted folder safe.
- **A copy replaces the whole destination store.** Keys that are no longer selected,
  and edits made inside the sandbox, are removed.

Host stores are never changed. Values are passed over stdin at run time. They never
appear in process arguments, images, build inputs, published kit sets, sandbox records
or output, also with `--verbose`.

## Selecting stores and keys

Add `userSecrets` to the [folder configuration](configuration.md) of the sandbox
location, not to a squad's `.abox` file:

```yaml
# abox.yaml or abox.user.yaml
userSecrets:
  - 5b3e0f6a-1c2d-4e5f-a6b7-c8d9e0f1a2b3     # a whole store
  - Cape.*                                   # every store whose ID starts with "Cape."
  - id: my-dev-store                         # only the matching keys of a store
    keys: ["ConnectionStrings:*", "Feature:Enabled"]
```

A `userSecrets` list in `abox.user.yaml` replaces the list of `abox.yaml` as a whole.
Without the setting, or with an empty list, nothing is read and nothing is written.

### Store IDs

| Entry | Selects |
| --- | --- |
| `my-store` or `id: my-store` | The whole store with that ID. |
| `Cape.*` or `id: Cape.*` | Every host store whose ID starts with `Cape.`. |
| `id: ...` with `keys: [...]` | Only the matching keys of those stores. |

- An ID is 1–128 ASCII letters, digits, dots, underscores or hyphens. It starts with a
  letter or digit, does not end with a dot and is not a Windows device name such
  as `CON`.
- A prefix is such a literal text followed by one final `*`. The dot in `Cape.*` is
  literal: it matches `Cape.Deals` and `cape.Position`, not `CapeOther`. A bare `*`,
  a `*` elsewhere, `?`, brackets and paths are errors.
- Matching ignores case. Two entries with the same ID text are an error.
- A prefix that matches nothing selects no stores and writes nothing.
- When an exact entry and a prefix select the same store, it is copied once with the
  union of their keys. A whole-store selection wins.
- Object entries accept only `id` and `keys`.

### Keys

`keys` patterns match effective .NET configuration keys, not JSON property names.
`{"ConnectionStrings": {"Db": "..."}}` has the key `ConnectionStrings:Db`; an array
item has a key such as `Services:2:Token`.

| `keys` | Result |
| --- | --- |
| omitted | All keys of the store. |
| `["Feature:Enabled"]` | That key only. |
| `["ConnectionStrings:*"]` | All keys below `ConnectionStrings`, at any depth. |
| `["*"]` | All keys. |
| `[]`, or patterns without a match | No keys: the destination becomes `{}`. |

- A pattern must match the whole key. Matching ignores case.
- `*` matches any number of characters, including `:`. It can appear anywhere and
  more than once. Every other character is literal.
- `ConnectionStrings` alone does not select the keys below it. Use
  `ConnectionStrings:*`.
- Patterns add up; a key that several patterns match is copied once.
- A store copied with `keys` is written with flat keys (`"Services:2:Token": ...`),
  so array indices never shift. A whole store keeps its JSON structure.

### Where the stores are

| | Path |
| --- | --- |
| Host, `APPDATA` set (also on Linux/macOS) | `<APPDATA>/Microsoft/UserSecrets/<id>/secrets.json` |
| Host, otherwise | `<HOME>/.microsoft/usersecrets/<id>/secrets.json` |
| Sandbox | The same rule, with the `APPDATA` and `HOME` of the sandbox's default user |

An application started in the sandbox with another `APPDATA` or `HOME` does not find
the copied stores.

A selected store needs a readable `secrets.json`: a UTF-8 JSON object, comments and
trailing commas allowed. It may be missing only when a project in the workspace
declares the store's ID and has appsettings to derive
[endpoint overrides](user-secrets-endpoint-rewriting.md) from.

### What fails the copy

These stop the copy of all stores before anything is written to the sandbox.
`--force` does not get past them.

- Invalid folder configuration, IDs or patterns.
- A selected store that is unreadable or not valid JSON.
- Keys that are duplicates in .NET terms, such as two spellings that differ only in
  case, or a nested and a flat form of the same key. This also fails when no pattern
  selects the duplicate.
- A matching store ID that is unsafe, or two matching IDs that differ only in case.
- A symbolic link or reparse point as the user-secrets root, a selected store folder
  or a `secrets.json`.
- A value on the deny list, unless `--force` is given.

## The deny list and `--force`

`userSecretsDenyList` in the [user configuration](configuration.md) is a list of
literal substrings. It is empty by default.

```yaml
# config.yaml
userSecretsDenyList:
  - prod.example.com
  - Password=Production
```

Before each copy, abox compares the selected values with the list, ignoring case. A
match stops the copy of all stores and names the store and the key, never the value.
Values that are not selected are not checked.

`abox user-secrets --force` copies despite matches and warns afterwards when a value
did match. It bypasses only the deny list. `create` and `run` have no such option:
`--yes` and the consent to replace a sandbox are not a force.

To create a sandbox with a store the deny list blocks:

1. Remove the store from `userSecrets`.
2. Run `abox create`.
3. Put the store back.
4. Run `abox user-secrets --location "/path/to/project" --force`.

## When copying runs

| Operation | Copies |
| --- | --- |
| `abox create`, also when it replaces a sandbox | Yes, after the sandbox is created |
| `abox run`, when you accept the replacement by a newer local build | Yes |
| `abox user-secrets [--location <path>] [--force]` | Yes, into the existing sandbox |
| Any other `abox run` | No |

`abox user-secrets` never creates or replaces a sandbox. It fails when the folder
configuration is missing, and it does not check which squad the sandbox was created
from. sbx may start a stopped sandbox for the copy.

During `create`, the selection and the host stores are read and checked against the
deny list before an existing sandbox is removed. The copy itself runs after the new
sandbox exists, before application ports and HTTPS are set up and before `create`
reports success. The folder configuration is read once at the start; later edits do
not affect the running command.

Every copy also [rewrites local endpoints](user-secrets-endpoint-rewriting.md) that
the sandbox is allowed to reach on the host.

## Output

The command lists each store with the number of keys copied and the names of rewritten
keys. A store with no selected keys is reported with `0 keys`, because its destination
was replaced by `{}`.

## Failure and recovery

Exit code 1 is an error, 130 a cancellation.

**The copy fails after `create` made the sandbox.** The sandbox and its record are
kept. `create` reports the failure and `run` does not start the launcher. Fix the cause
and run the printed command. Do not create the sandbox again just to retry.

```sh
abox user-secrets --location "/path/to/project"
```

**The copy stops halfway.** All stores are checked before the first write, but each
file is replaced on its own. A failure or cancellation during the transfer can leave
earlier stores replaced and later ones untouched. There is no rollback. Run
`abox user-secrets` again, with `--force` if you used it before. A file is replaced
only after its length and SHA-256 digest were verified, so a broken transfer never
leaves a truncated store.

**A leftover `.secrets.*` file.** A killed transfer can leave a private temporary file
in the store folder. .NET does not read it, and it goes away with the sandbox.

## Requirements in the sandbox

- `sh` and GNU coreutils (`stat`, `mktemp`, `sha256sum`, `mv -T`).
- Store folders are created with mode `0700`, files with `0600`, owned by the default
  user. The copy fails when the destination path contains a symbolic link, a file
  with several hard links or a file of another owner.
