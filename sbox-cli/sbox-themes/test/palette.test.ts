import { describe, expect, test } from "bun:test";
import { readdirSync } from "node:fs";
import { join } from "node:path";
import { themesPackage, useThemeHome } from "./theme-home";

const home = useThemeHome();

const probe = `mode={{ mode }}
background={{ background }} dark={{ dark_background }} darker={{ darker_background }}
foreground={{ foreground }} accent={{ accent }} orange={{ orange }} brown={{ brown }}
bright_red={{ bright_red }} color9={{ color9 }} color0={{ color0 }}
mix={{ background | mix: foreground, 25 }} percent={{ red | mix: blue, "50%" }}
strip={{ bright_red | strip }} rgb={{ bright_red | rgb }}
{% if mode == "light" %}light{% else %}dark{% endif %}
`;

function probeApp(extra = "") {
  return {
    "apps/probe/app.yaml": `files:\n  - template: probe.liquid\n    target: ~/probe.txt\n${extra}`,
    "apps/probe/probe.liquid": probe,
  };
}

describe("palettes", () => {
  test("resolve legacy names, color aliases and derived colors like upstream", async () => {
    // Arrange
    await home.install("themes", {
      package: themesPackage,
      files: {
        ...probeApp(),
        "themes/legacy/colors.toml": [
          'bg = "#ffffff"',
          'fg = "#000000"',
          'color1 = "#ff0000"',
          'color2 = "#00ff00"',
          'color3 = "#00ff00"',
          'color4 = "#0000ff"',
          'color5 = "#ff00ff"',
          'color6 = "#00ffff"',
        ].join("\n"),
      },
    });

    // Act
    const result = await home.run(["themes", "legacy"]);

    // Assert
    expect(result.stderr).toBe("");
    expect(await home.file("probe.txt")).toBe(`mode=light
background=#ffffff dark=#bfbfbf darker=#808080
foreground=#000000 accent=#0000ff orange=#00ff00 brown=#008000
bright_red=#ff3333 color9=#ff3333 color0=#ffffff
mix=#bfbfbf percent=#800080
strip=ff3333 rgb=255,51,51
light
`);
  });

  test("keep the colors a palette sets and its mode", async () => {
    // Arrange
    await home.install("themes", {
      package: themesPackage,
      files: {
        ...probeApp(),
        "themes/set/colors.toml": [
          'mode = "dark"',
          'accent = "#123456"',
          'background = "#F0F0F0"',
          'dark_background = "#010101"',
          'darker_background = "#020202"',
          'foreground = "#0F0F0F"',
          'red = "#ff0000"',
          'green = "#00ff00"',
          'yellow = "#ffff00"',
          'blue = "#0000ff"',
          'magenta = "#ff00ff"',
          'cyan = "#00ffff"',
          'orange = "#ff8000"',
          'brown = "#030303"',
          'bright_red = "#040404"',
        ].join("\n"),
      },
    });

    // Act
    await home.run(["themes", "set"]);

    // Assert
    expect(await home.file("probe.txt")).toBe(`mode=dark
background=#F0F0F0 dark=#010101 darker=#020202
foreground=#0F0F0F accent=#123456 orange=#ff8000 brown=#030303
bright_red=#040404 color9=#040404 color0=#F0F0F0
mix=#b8b8b8 percent=#800080
strip=040404 rgb=4,4,4
dark
`);
  });
});

/** A palette with every color a theme must set; the cascade derives the others. */
const complete = {
  background: "#101010",
  foreground: "#e0e0e0",
  red: "#ff0000",
  green: "#00ff00",
  yellow: "#ffff00",
  blue: "#0000ff",
  magenta: "#ff00ff",
  cyan: "#00ffff",
};

function colorsToml(colors: Record<string, string>) {
  return Object.entries(colors)
    .map(([key, value]) => `${key} = ${JSON.stringify(value)}`)
    .join("\n");
}

/** Installs the themes command with its themes and apps and adds the theme `name` with `colors`. */
async function installWithTheme(name: string, colors: Record<string, string>) {
  await home.install("themes", { package: themesPackage });
  await home.write(`.local/share/sbox/commands/themes/themes/${name}/colors.toml`, colorsToml(colors));
}

const shellCode = `#ffffff'; touch "$HOME/pwned"; printf '`;

/** Whether `sbox themes` left the apps and the saved choice `nord` as they were. */
async function expectNothingChanged() {
  expect(await home.exists(".config/sbox/terminal-colors.sh")).toBe(false);
  expect(await home.exists(".config/opencode/themes/sbox.json")).toBe(false);
  expect(await home.exists(".config/opencode/cli.json")).toBe(false);
  expect(await home.file(".local/state/sbox/themes/current")).toBe("nord\n");
}

describe("a palette with a value that is not a color", () => {
  test("is refused naming the theme and the key, and changes nothing", async () => {
    // Arrange
    await installWithTheme("evil", { ...complete, color13: shellCode });
    await home.writeState("themes", "current", "nord\n");

    // Act
    const result = await home.run(["themes", "evil"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stdout).toBe("");
    expect(result.stderr).toStartWith("sbox themes: the theme 'evil': 'color13' must be a color like #rrggbb");
    await expectNothingChanged();
  });

  test("fails --apply for the current theme without rendering or falling back", async () => {
    // Arrange
    await installWithTheme("evil", { ...complete, color13: shellCode });
    await home.writeState("themes", "current", "evil\n");

    // Act
    const result = await home.run(["themes", "--apply"], { env: { SBOX_THEME: "nord" } });

    // Assert
    expect(result.code).toBe(1);
    expect(result.stdout).toBe("");
    expect(result.stderr).toStartWith("sbox themes: the theme 'evil': 'color13' must be a color like #rrggbb");
    expect(await home.exists(".config/sbox/terminal-colors.sh")).toBe(false);
    expect(await home.file(".local/state/sbox/themes/current")).toBe("evil\n");
  });

  test("fails the preview naming the theme and the key", async () => {
    // Arrange
    await installWithTheme("evil", { ...complete, red: "red" });

    // Act
    const result = await home.run(["themes", "--preview", "evil"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stdout).toBe("");
    expect(result.stderr).toBe(`sbox themes: the theme 'evil': 'red' must be a color like #rrggbb, not "red"\n`);
  });

  test("is still listed and can still be the current theme", async () => {
    // Arrange
    await installWithTheme("evil", { ...complete, color13: shellCode });
    await home.writeState("themes", "current", "evil\n");

    // Act
    const list = await home.run(["themes", "--list"]);
    const current = await home.run(["themes", "--current"]);

    // Assert
    expect(list.code).toBe(0);
    expect(list.stdout).toContain("* evil\n");
    expect(current.code).toBe(0);
    expect(current.stdout).toBe("evil\n");
  });
});

describe("a palette", () => {
  test("that lacks a color it cannot derive is refused naming the color", async () => {
    // Arrange
    const { green: _, ...withoutGreen } = complete;
    await installWithTheme("faded", withoutGreen);
    await home.writeState("themes", "current", "nord\n");

    // Act
    const result = await home.run(["themes", "faded"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toStartWith("sbox themes: the theme 'faded': the color 'green' is missing");
    await expectNothingChanged();
  });

  test("with a mode other than dark or light is refused", async () => {
    // Arrange
    await installWithTheme("dim", { ...complete, mode: "dim" });
    await home.writeState("themes", "current", "nord\n");

    // Act
    const result = await home.run(["themes", "dim"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toBe(`sbox themes: the theme 'dim': 'mode' must be dark or light, not "dim"\n`);
    await expectNothingChanged();
  });

  test("ignores the keys that are not part of it", async () => {
    // Arrange
    await installWithTheme("bordered", { ...complete, hyprland_active_border: "rgba(26a269ee) 45deg" });

    // Act
    const result = await home.run(["themes", "bordered"]);

    // Assert
    expect(result.stderr).toBe("");
    expect(result.code).toBe(0);
  });

  test("of every shipped theme applies", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });
    const names = readdirSync(join(themesPackage, "themes"), { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => entry.name);

    // Act
    const failed: string[] = [];
    for (const name of names) {
      const result = await home.run(["themes", name]);
      if (result.code !== 0 || result.stderr !== "") failed.push(`${name}: ${result.stderr}`);
    }

    // Assert
    expect(names).toContain("tokyo-night");
    expect(failed).toEqual([]);
  });
});
