import { existsSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import type { Command, CommandContext, Output, Style } from "./api";
import type { SboxIO } from "./sbox";
import { styleFor } from "./style";

/** A command's module, loaded: the command, or broken with a one-line reason. */
type Loaded = { readonly command: Command } | { readonly broken: string };

/**
 * The commands installed in the commands folder, `~/.local/share/sbox/commands/<name>/command.js`,
 * each with its state folder `~/.local/state/sbox/<name>/`. A command whose module fails to load
 * or does not satisfy the command API is broken: it is listed with the reason and fails when run,
 * and every other command keeps working.
 */
export class InstalledCommands {
  private readonly home: string;
  private readonly folder: string;

  constructor(private readonly io: SboxIO) {
    this.home = io.env.HOME ?? "";
    this.folder = join(this.home, ".local/share/sbox/commands");
  }

  /** The installed commands with their summaries, styled for the output it is written to. */
  async overview(output: Output): Promise<string> {
    const names = this.names();
    if (names.length === 0) return "No commands are installed.\n";
    const style = this.styleOf(output);
    const width = Math.max(...names.map((name) => name.length));
    const lines = await Promise.all(
      names.map(async (name) => {
        const loaded = await this.load(name);
        const summary = "command" in loaded ? loaded.command.summary : `${style.red("broken")}: ${loaded.broken}`;
        return `  ${name.padEnd(width)}  ${summary}\n`;
      }),
    );
    return "Commands:\n" + lines.join("");
  }

  /** Runs the command `name` with `args`: its exit code, or `undefined` when no command of that name is installed. */
  async run(name: string, args: string[]): Promise<number | undefined> {
    if (!this.names().includes(name)) return undefined;
    const loaded = await this.load(name);
    if ("broken" in loaded) return this.fail(name, loaded.broken);
    try {
      return (await loaded.command.run(args, this.contextFor(name))) ?? 0;
    } catch (error) {
      return this.fail(name, messageOf(error));
    }
  }

  private names(): string[] {
    if (!statSync(this.folder, { throwIfNoEntry: false })?.isDirectory()) return [];
    return readdirSync(this.folder, { withFileTypes: true })
      .filter((entry) => entry.isDirectory() && existsSync(this.module(entry.name)))
      .map((entry) => entry.name)
      .sort();
  }

  private async load(name: string): Promise<Loaded> {
    let module: { default?: unknown };
    try {
      module = await import(this.module(name));
    } catch (error) {
      return { broken: `failed to load: ${messageOf(error).split("\n")[0]}` };
    }
    const defect = defectOf(module.default);
    if (defect !== undefined) return { broken: `command.js does not export a command: ${defect}` };
    return { command: module.default as Command };
  }

  private contextFor(name: string): CommandContext {
    return {
      name,
      paths: { home: this.home, state: join(this.home, ".local/state/sbox", name), data: this.folderOf(name) },
      env: this.io.env,
      stdout: this.io.stdout,
      stderr: this.io.stderr,
      style: this.styleOf(this.io.stdout),
      isTTY: Boolean(this.io.stdin.isTTY && this.io.stdout.isTTY),
    };
  }

  private fail(name: string, reason: string): number {
    this.io.stderr.write(`sbox ${name}: ${reason}\n`);
    return 1;
  }

  private styleOf(output: Output): Style {
    return styleFor(Boolean(output.isTTY) && !this.io.env.NO_COLOR);
  }

  private folderOf(name: string): string {
    return join(this.folder, name);
  }

  private module(name: string): string {
    return join(this.folderOf(name), "command.js");
  }
}

/** What keeps a module's default export from being a `Command`, if anything. */
function defectOf(exported: unknown): string | undefined {
  if (exported === undefined) return "it has no default export";
  const command = exported as Partial<Record<keyof Command, unknown>> | null;
  if (typeof command?.summary !== "string") return "its summary is not a string";
  if (typeof command.run !== "function") return "it has no run function";
  return undefined;
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
