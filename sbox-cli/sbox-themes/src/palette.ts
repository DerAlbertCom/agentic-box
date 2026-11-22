import { existsSync, readFileSync } from "node:fs";
import { basename, join } from "node:path";

const namedColors = ["red", "green", "yellow", "blue", "magenta", "cyan"] as const;

// The required colors come first, so a theme that lacks one is told that one and not a color
// derived from it.
const colors = [
  "background",
  "foreground",
  ...namedColors,
  ...namedColors.map((name) => `bright_${name}` as const),
  "purple",
  "bright_purple",
  "color0",
  "color1",
  "color2",
  "color3",
  "color4",
  "color5",
  "color6",
  "color7",
  "color8",
  "color9",
  "color10",
  "color11",
  "color12",
  "color13",
  "color14",
  "color15",
  "cursor",
  "accent",
  "muted",
  "orange",
  "brown",
  "selection",
  "selection_background",
  "selection_foreground",
  "dark_background",
  "darker_background",
  "lighter_background",
  "dark_foreground",
  "light_foreground",
  "bright_foreground",
  "bg",
  "dark_bg",
  "darker_bg",
  "lighter_bg",
  "fg",
  "dark_fg",
  "light_fg",
  "bright_fg",
] as const;

const modes = ["dark", "light"] as const;

/** A key of a palette that holds a color. */
export type Color = (typeof colors)[number];
type Mode = (typeof modes)[number];

/**
 * The colors of a theme by key, after the fallbacks, each a #rrggbb, plus `mode` and its
 * alias `theme_type`. Nothing else of `colors.toml` is in it.
 */
export type Palette = Readonly<Record<Color, string> & Record<"mode" | "theme_type", Mode>>;

const hex = /^#[0-9a-f]{6}$/i;

/** Reads `<folder>/colors.toml` of the theme named like the folder; see `parsePalette`. */
export function readPalette(folder: string): Palette {
  return parsePalette(
    basename(folder),
    readFileSync(join(folder, "colors.toml"), "utf8"),
    existsSync(join(folder, "light.mode")),
  );
}

/**
 * Resolves the text of the theme's `colors.toml` the way upstream does into a palette with
 * every color and the mode, or fails naming the theme and the key. Other keys are dropped.
 */
export function parsePalette(theme: string, toml: string, lightModeFile = false): Palette {
  const fail = (problem: string): never => {
    throw new Error(`the theme '${theme}': ${problem}`);
  };
  let parsed: Record<string, unknown> = {};
  try {
    parsed = Bun.TOML.parse(toml) as Record<string, unknown>;
  } catch (error) {
    fail(`colors.toml is not valid TOML: ${(error as Error).message}`);
  }

  const set: Record<string, string> = {};
  const take = (key: string, valid: (value: unknown) => boolean, expected: string) => {
    const value = parsed[key];
    if (value === undefined || value === "") return;
    if (!valid(value)) fail(`'${key}' must be ${expected}, not ${JSON.stringify(value)}`);
    set[key] = value as string;
  };
  for (const key of colors) {
    take(key, (value) => typeof value === "string" && hex.test(value), "a color like #rrggbb");
  }
  for (const key of ["mode", "theme_type"]) take(key, (value) => modes.includes(value as Mode), "dark or light");

  const palette = resolve(set, lightModeFile);
  const missing = colors.find((key) => !palette[key]);
  if (missing) fail(`the color '${missing}' is missing and no other color gives it`);
  return palette as Palette;
}

/** Blends `color` toward `other` by `amount`: a percentage such as 15 or "15%", or a fraction up to 1. */
export function mix(color: string, other: string, amount: number | string): string {
  for (const value of [color, other]) {
    if (!hex.test(value)) throw new Error(`mix needs colors like #rrggbb, not '${value}'`);
  }
  let t = typeof amount === "number" ? amount : Number.parseFloat(amount);
  if (Number.isNaN(t)) throw new Error(`mix needs an amount like 15%, not '${amount}'`);
  if ((typeof amount === "string" && amount.trim().endsWith("%")) || t > 1) t /= 100;
  t = Math.min(Math.max(t, 0), 1);
  const a = channels(color);
  const b = channels(other);
  return (
    "#" +
    a
      .map((value, i) =>
        Math.floor(value * (1 - t) + b[i]! * t + 0.5)
          .toString(16)
          .padStart(2, "0"),
      )
      .join("")
  );
}

/** The decimal channels of a #rrggbb color. */
export function channels(color: string): number[] {
  if (!hex.test(color)) throw new Error(`expected a color like #rrggbb, not '${color}'`);
  return [1, 3, 5].map((start) => Number.parseInt(color.slice(start, start + 2), 16));
}

const legacyNames = [
  ["background", "bg"],
  ["dark_background", "dark_bg"],
  ["darker_background", "darker_bg"],
  ["lighter_background", "lighter_bg"],
  ["foreground", "fg"],
  ["dark_foreground", "dark_fg"],
  ["light_foreground", "light_fg"],
  ["bright_foreground", "bright_fg"],
] as const;

// The cascade of upstream's theme color resolution: every step only fills keys that are still empty.
function resolve(palette: Record<string, string>, lightModeFile: boolean): Record<string, string> {
  const fill = (key: string, ...sources: (string | undefined)[]) => {
    if (palette[key]) return;
    const value = sources.find((source) => source);
    if (value) palette[key] = value;
  };
  for (const [canonical, short] of legacyNames) fill(canonical, palette[short]);

  fill("background", palette.color0);
  fill("foreground", palette.color7);
  if (palette.background) palette.color0 = palette.background;
  if (palette.foreground) palette.color7 = palette.foreground;

  const ansi = ["", "red", "green", "yellow", "blue", "magenta", "cyan"];
  ansi.forEach((name, i) => {
    if (!name) return;
    fill(name, palette[`color${i}`]);
    fill(`bright_${name}`, palette[`color${i + 8}`]);
  });
  fill("magenta", palette.purple);
  fill("bright_magenta", palette.bright_purple);

  fill("light_foreground", palette.color7, palette.foreground);
  fill("bright_foreground", palette.color15, palette.foreground);
  if (palette.bright_foreground) palette.cursor = palette.bright_foreground;
  fill("lighter_background", palette.color0, palette.background);
  fill("dark_foreground", palette.color8, palette.foreground);
  fill("muted", palette.color8, palette.dark_foreground);
  fill("selection", palette.selection_background, palette.color8, palette.color0, palette.background);
  fill("selection_background", palette.selection);
  fill("selection_foreground", palette.bright_foreground);
  fill("orange", palette.yellow);
  fill("brown", mixed(palette.orange, "#000000", 50));

  fill("dark_background", mixed(palette.background, "#000000", 25));
  fill("darker_background", mixed(palette.background, "#000000", 50));
  for (const name of ansi.slice(1)) fill(`bright_${name}`, mixed(palette[name], "#ffffff", 20));
  fill("purple", palette.magenta);
  fill("bright_purple", palette.bright_magenta);

  const aliases = ["background", "red", "green", "yellow", "blue", "magenta", "cyan", "foreground", "muted"];
  aliases.forEach((name, i) => fill(`color${i}`, palette[name]));
  ["red", "green", "yellow", "blue", "magenta", "cyan"].forEach((name, i) =>
    fill(`color${i + 9}`, palette[`bright_${name}`]),
  );
  fill("color15", palette.bright_foreground);
  fill("accent", palette.color4);

  for (const [canonical, short] of legacyNames) if (palette[canonical]) palette[short] = palette[canonical];

  fill("mode", palette.theme_type, lightModeFile ? "light" : undefined, modeOf(palette.background));
  palette.theme_type = palette.mode!;
  return palette;
}

function mixed(color: string | undefined, other: string, amount: number): string | undefined {
  return color && hex.test(color) ? mix(color, other, amount) : undefined;
}

function modeOf(background: string | undefined): string {
  if (!background || !hex.test(background)) return "dark";
  const sum = channels(background).reduce((total, value) => total + value, 0);
  return sum > 382 ? "light" : "dark";
}
