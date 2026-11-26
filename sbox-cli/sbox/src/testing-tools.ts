import { rmSync } from "node:fs";
import { link, mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

let launcher: Promise<string> | undefined;

async function buildLauncher(): Promise<string> {
  const shell = Bun.which("sh", { PATH: process.env.PATH });
  if (!shell) throw new Error("The shell integration tests require sh (Git for Windows on Windows).");
  const folder = await mkdtemp(join(tmpdir(), "sbox-test-tool-"));
  process.once("exit", () => rmSync(folder, { recursive: true, force: true }));
  const executable = join(folder, "tool.exe");
  const built = await Bun.build({
    entrypoints: [join(import.meta.dir, "testing-tool.ts")],
    define: { TEST_SHELL: JSON.stringify(shell) },
    compile: {
      outfile: executable,
      executablePath: process.execPath,
      autoloadBunfig: false,
      autoloadDotenv: false,
    },
  });
  if (!built.success) throw new AggregateError(built.logs, "Could not build the native test tool");
  return executable;
}

/** Prepare once, outside individual test deadlines, using only the already-installed Bun. */
export async function prepareTestTools() {
  if (process.platform === "win32") await (launcher ??= buildLauncher());
}

/** Installs a shell fixture, with a native entry point when Windows cannot exec its shebang. */
export async function writeTestTool(folder: string, name: string, script: string) {
  await mkdir(folder, { recursive: true });
  await writeFile(join(folder, name), script, { mode: 0o755 });
  if (process.platform !== "win32") return;
  const executable = await (launcher ??= buildLauncher());
  await rm(join(folder, name + ".exe"), { force: true });
  await link(executable, join(folder, name + ".exe"));
}
