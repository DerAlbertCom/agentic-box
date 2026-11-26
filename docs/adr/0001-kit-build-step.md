# Local kits with a build file are built into a staged copy

abox used to hand a local kit to sbx exactly as it lies in the repository. Kits whose payload is compiled, such as the TypeScript `sbox` commands, would then need their build output committed, where it can go stale. So a local kit folder with an executable `build` file is copied into the build's staging folder, `build <stagedKitDir>` runs with the kit's repository folder as the working directory (so it can reach sibling top-level packages), and the staged copy is what its component image is built from (see [ADR 0006](0006-build-produces-images-publish-pushes-them.md)). Kits without `build` still pass through unchanged, and the kit descriptor format does not change, so sbx sees nothing new.

## Considered Options

- **Commit the build output.** Rejected: generated files in review, and they can drift from their sources.
- **Build in the kit's Dockerfile.** Rejected: every create would compile, and the toolchain would have to be in the image or a build stage that sbx assembles.
- **A descriptor field for the build.** Rejected: the descriptor belongs to the kit spec that sbx reads; an executable `build` file needs no new format.

## Consequences

- The build step runs only when the kit's repository folder changed: that folder is the input of the kit's component image, and publish refuses the build when it changed since ([ADR 0006](0006-build-produces-images-publish-pushes-them.md), which supersedes the earlier `--metadata-only` and `--no-personal` consequences). Packages outside the folder that `build` reads are not covered; rebuild with `abox build --rebuild` after changing them.
- On Linux and macOS the `build` file runs as an executable, so one without the executable bit fails `abox build`. Passing such a kit through unbuilt would hand sbx a kit without its payload and fail only later, inside the sandbox.
- Windows has no executable bit, so there abox runs the `build` file with the `sh` of Git for Windows (one on the PATH, else the one next to `git.exe`), with MSYS path conversion turned off so Docker volume arguments reach Docker unchanged. Without such a shell `abox build` fails and names Git for Windows. Build scripts must therefore stay POSIX `sh`, and folders they hand to Docker must be host paths (`pwd -W` under Git's `sh`, as `sbox-cli/bundle` does). This replaces the earlier rule that refused every `build` file on Windows.

