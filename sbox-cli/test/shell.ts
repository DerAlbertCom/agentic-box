/** Resolves a native POSIX shell, including Git for Windows configured by the test preload. */
export function requireShell(name = "sh"): string {
  const shell = Bun.which(name, { PATH: process.env.PATH });
  if (!shell) throw new Error(`The shell integration tests require ${name} (Git for Windows on Windows).`);
  return shell;
}

/** A native host path with shell-friendly separators (also the spelling of Git's pwd -W). */
export function shellPath(path: string): string {
  return path.replace(/\\/g, "/");
}

export const workingDirectory = process.platform === "win32" ? "$(pwd -W)" : "$PWD";
