// The testing entry of sbox: the command home. Import it in tests only, never in a command's
// source, so nothing of the core is bundled into a command.
import { afterEach, beforeEach } from "bun:test";
import { existsSync } from "node:fs";
import { cp, mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { basename, dirname, join } from "node:path";
import { sbox } from "./sbox";

/** Where a command comes from when it is installed into the command home. */
export type CommandSource =
  /** A package folder: its `src/command.ts`, run from source, with the data folders it declares. */
  | { readonly package: string }
  /** A package folder's `src/command.ts` with the given data files instead, by their path in the command's folder. */
  | { readonly package: string; readonly files: Readonly<Record<string, string>> }
  /** The text of a command's module. */
  | { readonly module: string };

export interface RunOptions {
  /** Variables added to the environment, which holds `HOME` and `PATH`; they may replace those two. */
  readonly env?: Readonly<Record<string, string>>;
  /** Whether stdin and stdout are terminals. */
  readonly tty?: boolean;
}

export interface RunResult {
  readonly code: number;
  readonly stdout: string;
  readonly stderr: string;
}

export interface CommandHome {
  /** The temporary home folder, new for each test. */
  readonly path: string;
  /** Installs the command `name` into the commands folder. */
  install(name: string, source: CommandSource): Promise<void>;
  /** Runs `sbox` with `argv` and captures its exit code and output. */
  run(argv: string[], options?: RunOptions): Promise<RunResult>;
  /** Puts an executable `script` named `name` into a folder that is first on `PATH` for every run. */
  tool(name: string, script: string): Promise<void>;
  /** Writes a file by its path in the home. */
  write(path: string, text: string): Promise<void>;
  /** Writes a file by its path in the state folder of the command `name`. */
  writeState(name: string, path: string, text: string): Promise<void>;
  /** Reads a text file by its path in the home. */
  file(path: string): Promise<string>;
  /** Reads a JSON file by its path in the home. */
  json(path: string): Promise<any>;
  /** Whether a file exists by its path in the home. */
  exists(path: string): Promise<boolean>;
}

class Capture {
  text = "";
  constructor(readonly isTTY = false) {}
  write(chunk: string) {
    this.text += chunk;
  }
}

/** A temporary home for each test of the calling file, removed after the test. */
export function useCommandHome(): CommandHome {
  let home = "";

  beforeEach(async () => {
    home = await mkdtemp(join(tmpdir(), "sbox-home-"));
  });

  afterEach(async () => {
    await rm(home, { recursive: true, force: true });
  });

  const commandFolder = (name: string) => join(home, ".local/share/sbox/commands", name);
  const stateFolder = (name: string) => join(home, ".local/state/sbox", name);
  const tools = () => join(home, ".command-home-tools");

  async function writeIn(folder: string, path: string, text: string, mode?: number) {
    await mkdir(dirname(join(folder, path)), { recursive: true });
    await writeFile(join(folder, path), text, { mode });
  }

  async function file(path: string) {
    return readFile(join(home, path), "utf8");
  }

  return {
    get path() {
      return home;
    },

    async install(name, source) {
      const folder = commandFolder(name);
      if ("module" in source) {
        await writeIn(folder, "command.js", source.module);
        return;
      }
      const entry = join(source.package, "src/command.ts");
      await writeIn(folder, "command.js", `export { default } from ${JSON.stringify(entry)};\n`);
      if ("files" in source) {
        for (const [path, text] of Object.entries(source.files)) await writeIn(folder, path, text);
        return;
      }
      for (const data of await dataFolders(source.package)) {
        await cp(join(source.package, data), join(folder, data), { recursive: true });
      }
    },

    async run(argv, options = {}) {
      const stdout = new Capture(options.tty);
      const stderr = new Capture();
      const code = await sbox(argv, {
        env: { HOME: home, PATH: `${tools()}:${process.env.PATH ?? ""}`, ...options.env },
        stdout,
        stderr,
        stdin: { isTTY: options.tty },
      });
      return { code, stdout: stdout.text, stderr: stderr.text };
    },

    tool: (name, script) => writeIn(tools(), name, script, 0o755),
    write: (path, text) => writeIn(home, path, text),
    writeState: (name, path, text) => writeIn(stateFolder(name), path, text),
    file,
    json: async (path) => JSON.parse(await file(path)),
    exists: (path) => Bun.file(join(home, path)).exists(),
  };
}

/** The data folders a command's package declares in its package.json under `sbox.data`. */
async function dataFolders(packageFolder: string): Promise<string[]> {
  const manifest = JSON.parse(await readFile(join(packageFolder, "package.json"), "utf8"));
  const folders: string[] = manifest.sbox?.data ?? [];
  const missing = folders.find((folder) => !existsSync(join(packageFolder, folder)));
  if (missing !== undefined) {
    throw new Error(`${basename(packageFolder)} declares the data folder '${missing}', but it is missing.`);
  }
  return folders;
}
