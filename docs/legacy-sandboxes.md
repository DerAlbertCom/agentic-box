# Reusing cape-agent-sandbox environments

abox reuses a sandbox that the older cape-agent-sandbox tool created for the same
folder, so you do not have to recreate it.

## How the sandbox of a folder is found

`run`, `ports`, `user-secrets` and `create` look for the sandbox of the
[sandbox location](cli-reference.md#sandbox-location) in this order:

1. the abox name, `<folder>-abox-<hash>`;
2. only if that does not exist, the name cape-agent-sandbox generated,
   `<folder>-ai-sbx-<hash>`.

If both exist, the abox sandbox is used and the legacy one is left alone. An error
while listing sandboxes stops the command; it is never treated as "no sandbox".

The legacy hash depends on the exact spelling of the folder path at the time the
sandbox was created. On Windows, when neither name is found, abox also accepts a
sandbox whose recorded host workspace is this folder (compared case-insensitively) and
whose name is the legacy name for that recorded spelling. Sandboxes with custom names
are never adopted, and several matches are an error.

## What happens to a legacy sandbox

- It is used in place, under its own name. Nothing is renamed, migrated or cleaned up,
  and its state directories and Herdr registration are not moved.
- `abox create` asks before replacing it, as for any sandbox, and lists the same
  losses. The replacement keeps the legacy name.
- `abox user-secrets` copies into it without checking which squad it was created from.
  Review the [user-secrets](user-secrets.md) selection before copying.

A folder without any sandbox gets one with the abox name.
