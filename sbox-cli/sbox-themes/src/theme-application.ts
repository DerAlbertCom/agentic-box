import { existsSync, readFileSync } from "node:fs";
import { mkdir, rename, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import type { CommandContext } from "sbox";
import { renderAppTemplates, type RenderedApps } from "./apps";
import { foldersWith } from "./folders";
import { readPalette, type Palette } from "./palette";

const fallbackTheme = "tokyo-night";

export interface CurrentTheme {
  readonly name: string;
  /** The saved choice or default theme that is not installed and was skipped. */
  readonly warnings: readonly string[];
}

export interface Applied {
  readonly name: string;
  /** The apps' commands that failed, with their output. */
  readonly failures: readonly string[];
}

/**
 * Theme application: renders every app template with a theme and writes the result, all or
 * nothing. Reading the palette, the app templates and the settings files and rendering them
 * happen first; any error ends the application with nothing written. Only then are the files
 * written, the apps' commands run and, for a switch, the choice saved.
 */
export class ThemeApplication {
  private readonly themes: string;
  private readonly saved: string;

  constructor(private readonly ctx: Pick<CommandContext, "paths" | "env">) {
    this.themes = join(ctx.paths.data, "themes");
    this.saved = join(ctx.paths.state, "current");
  }

  /** The installed themes, sorted. */
  names(): string[] {
    return foldersWith(this.themes, "colors.toml");
  }

  /** The palette of the installed theme `name`, as applying it would read it. */
  palette(name: string): Palette {
    return readPalette(join(this.themes, name));
  }

  /** The saved choice, then the kit's default `SBOX_THEME`, then tokyo-night. */
  current(): CurrentTheme {
    const names = this.names();
    const saved = existsSync(this.saved) ? readFileSync(this.saved, "utf8").trim() : "";
    if (saved && names.includes(saved)) return { name: saved, warnings: [] };

    const preset = this.ctx.env.SBOX_THEME;
    const fallback = preset && names.includes(preset) ? preset : fallbackTheme;
    const warnings: string[] = [];
    if (saved) warnings.push(`the saved theme '${saved}' is not installed; using '${fallback}'.`);
    if (preset && preset !== fallback)
      warnings.push(`the default theme '${preset}' (SBOX_THEME) is not installed; using '${fallback}'.`);
    return { name: fallback, warnings };
  }

  /** Applies the installed theme `name` and saves it as the choice. */
  async switchTo(name: string): Promise<Applied> {
    const failures = await this.commit(await this.plan(name));
    await mkdir(this.ctx.paths.state, { recursive: true });
    await writeFile(this.saved, name + "\n");
    return { name, failures };
  }

  /** Applies the current theme without saving a choice, after passing each of its warnings to `warn`. */
  async applyCurrent(warn: (warning: string) => void): Promise<Applied> {
    const { name, warnings } = this.current();
    for (const warning of warnings) warn(warning);
    return { name, failures: await this.commit(await this.plan(name)) };
  }

  private async plan(name: string): Promise<RenderedApps> {
    return renderAppTemplates(join(this.ctx.paths.data, "apps"), this.palette(name), this.ctx.paths.home);
  }

  /** Writes the files, then runs the apps' commands; returns the commands that failed. */
  private async commit(rendered: RenderedApps): Promise<string[]> {
    for (const [path, text] of rendered.files) await writeAtomically(path, text);
    const failures: string[] = [];
    for (const { app, command } of rendered.commands) {
      const result = Bun.spawnSync(["sh", "-c", command], {
        cwd: this.ctx.paths.home,
        env: { ...this.ctx.env },
        stdout: "pipe",
        stderr: "pipe",
      });
      if (result.exitCode !== 0) {
        const output = (result.stderr.toString() + result.stdout.toString()).trim();
        failures.push(
          `${app}: '${command}' failed with exit code ${result.exitCode}${output ? `:\n${output}` : ""}`,
        );
      }
    }
    return failures;
  }
}

async function writeAtomically(path: string, text: string) {
  await mkdir(dirname(path), { recursive: true });
  const temporary = `${path}.sbox-${crypto.randomUUID()}`;
  await writeFile(temporary, text);
  await rename(temporary, path);
}
