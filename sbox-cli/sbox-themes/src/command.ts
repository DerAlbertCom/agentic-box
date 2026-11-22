import { existsSync } from "node:fs";
import { join } from "node:path";
import { parseArgs } from "node:util";
import type { Command, CommandContext } from "sbox";
import { pick, preview } from "./picker";
import { ThemeApplication } from "./theme-application";

/** The terminal color script, rendered by the `terminal` app template. */
const terminalColorScript = ".config/sbox/terminal-colors.sh";

export default {
  summary: "Switch the color theme of the sandbox's apps",
  async run(args, ctx) {
    const { values, positionals } = parseArgs({
      args,
      options: {
        list: { type: "boolean" },
        current: { type: "boolean" },
        apply: { type: "boolean" },
        preview: { type: "string" },
      },
      allowPositionals: true,
    });
    if (positionals.length > 1) throw new Error("expected one theme name");
    const themes = new ThemeApplication(ctx);

    if (values.current) {
      ctx.stdout.write(themes.current().name + "\n");
      return 0;
    }
    if (values.preview !== undefined) {
      if (!themes.names().includes(values.preview)) return unknown(values.preview, themes.names(), ctx);
      const width = Number.parseInt(ctx.env.FZF_PREVIEW_COLUMNS ?? "", 10) || 60;
      ctx.stdout.write(preview(values.preview, themes.palette(values.preview), width));
      return 0;
    }
    if (values.apply) {
      const applied = await themes.applyCurrent((warning) =>
        ctx.stderr.write(ctx.style.yellow(`sbox themes: ${warning}`) + "\n"),
      );
      reportFailures(applied.failures, ctx);
      ctx.stdout.write(`Applied the theme '${applied.name}'.\n`);
      return applied.failures.length ? 1 : 0;
    }
    let name = positionals[0];
    if (name === undefined && !values.list && ctx.isTTY) {
      name = await pick(themes.names(), themes.current().name, ctx);
      if (name === undefined) return 0;
    }
    if (values.list || name === undefined) {
      const current = themes.current().name;
      for (const theme of themes.names()) {
        ctx.stdout.write(theme === current ? ctx.style.bold(`* ${theme}`) + "\n" : `  ${theme}\n`);
      }
      return 0;
    }
    if (!themes.names().includes(name)) return unknown(name, themes.names(), ctx);
    const switched = await themes.switchTo(name);
    reportFailures(switched.failures, ctx);
    if (ctx.env.SBOX_TERMINAL_THEME === "1" && ctx.stdout.isTTY) recolorTerminal(ctx);
    ctx.stdout.write(`Switched to the theme '${name}'. Some apps show it only after a restart.\n`);
    return switched.failures.length ? 1 : 0;
  },
} satisfies Command;

/** Reports an unknown theme with the available ones and returns the exit code. */
function unknown(name: string, themes: readonly string[], ctx: CommandContext): number {
  ctx.stderr.write(`sbox themes: there is no theme '${name}'. The themes are:\n`);
  for (const theme of themes) ctx.stderr.write(`  ${theme}\n`);
  return 1;
}

function reportFailures(failures: readonly string[], ctx: CommandContext) {
  for (const failure of failures) ctx.stderr.write(`sbox themes: ${failure}\n`);
}

/** Prints the colors of the terminal color script, if it is rendered and runs. */
function recolorTerminal(ctx: CommandContext) {
  const script = join(ctx.paths.home, terminalColorScript);
  if (!existsSync(script)) return;
  const result = Bun.spawnSync(["sh", script], { stdout: "pipe", stderr: "ignore" });
  if (result.exitCode === 0) ctx.stdout.write(result.stdout.toString());
}

