import { parseArgs } from "node:util";
import type { Output } from "./api";
import { InstalledCommands } from "./installed-commands";

export interface SboxIO {
  readonly env: Readonly<Record<string, string | undefined>>;
  readonly stdout: Output;
  readonly stderr: Output;
  readonly stdin: { readonly isTTY?: boolean };
}

const usage = "Usage: sbox <command> [args...]\n";

/** The sbox core: routes `sbox <command> [args...]` to the command's module in the commands folder. */
export async function sbox(argv: string[], io: SboxIO): Promise<number> {
  const commands = new InstalledCommands(io);
  // Options before the command name belong to sbox, everything after it to the command.
  const start = argv.findIndex((arg) => !arg.startsWith("-"));
  const [name, ...args] = start === -1 ? [] : argv.slice(start);
  let help: boolean | undefined;
  try {
    const options = parseArgs({
      args: start === -1 ? argv : argv.slice(0, start),
      options: { help: { type: "boolean", short: "h" } },
    });
    help = options.values.help;
  } catch (error) {
    io.stderr.write(`sbox: ${(error as Error).message}\n\n${usage}`);
    return 2;
  }

  if (help || name === undefined) {
    io.stdout.write(usage + "\n" + (await commands.overview(io.stdout)));
    return 0;
  }

  const code = await commands.run(name, args);
  if (code !== undefined) return code;
  io.stderr.write(`sbox: the command '${name}' is not installed.\n\n${await commands.overview(io.stderr)}`);
  return 1;
}
