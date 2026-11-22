import { beforeEach, describe, expect, test } from "bun:test";
import { join } from "node:path";
import { themesPackage, useThemeHome } from "./theme-home";

const home = useThemeHome();

beforeEach(async () => {
  await home.install("themes", { package: themesPackage });
});

const fzfLog = () => join(home.path, "fzf.log");

/** Puts a fake `fzf` on PATH that logs its arguments and input, then prints `selection` or cancels. */
async function fakeFzf(selection: string | null) {
  const answer = selection === null ? "exit 130" : `echo ${selection}`;
  await home.tool(
    "fzf",
    `#!/bin/sh
for arg in "$@"; do echo "arg: $arg"; done >> "${fzfLog()}"
while read -r line; do echo "item: $line"; done >> "${fzfLog()}"
${answer}
`,
  );
}

async function fzfCalls(): Promise<string[] | null> {
  if (!(await home.exists("fzf.log"))) return null;
  return (await home.file("fzf.log")).trimEnd().split("\n");
}

async function savedTheme(): Promise<string | null> {
  const current = ".local/state/sbox/themes/current";
  if (!(await home.exists(current))) return null;
  return (await home.file(current)).trim();
}

const background = (r: number, g: number, b: number) => `\x1b[48;2;${r};${g};${b}m`;
const foreground = (r: number, g: number, b: number) => `\x1b[38;2;${r};${g};${b}m`;

describe("sbox themes --preview <name>", () => {
  test("prints truecolor swatches of the palette", async () => {
    // Act
    const result = await home.run(["themes", "--preview", "tokyo-night"]);

    // Assert
    expect(result.stderr).toBe("");
    expect(result.code).toBe(0);
    expect(result.stdout).toContain("tokyo-night");
    expect(result.stdout).toContain(background(0xf7, 0x76, 0x8e)); // red
    expect(result.stdout).toContain(background(0x9e, 0xce, 0x6a)); // green
    expect(result.stdout).toContain(background(0x7a, 0xa2, 0xf7)); // blue
    expect(result.stdout).toContain(background(0xff, 0x7a, 0x93)); // bright_red
    expect(result.stdout).toContain(background(0x0d, 0xb9, 0xd7)); // bright_cyan
  });

  test("prints a code and diff sample in the theme's colors on its background", async () => {
    // Act
    const result = await home.run(["themes", "--preview", "tokyo-night"]);

    // Assert
    const lines = result.stdout.split("\n");
    const sample = lines.filter((line) => line.startsWith(background(0x1a, 0x1b, 0x26)));
    expect(sample.length).toBeGreaterThan(4);
    const code = sample.join("\n");
    expect(code).toContain(foreground(0xad, 0x8e, 0xe6) + "function"); // magenta keyword
    expect(code).toContain(foreground(0x9e, 0xce, 0x6a) + '"'); // green string
    expect(code).toContain(foreground(0x41, 0x48, 0x68) + "//"); // muted comment
    expect(lines.some((line) => line.includes(foreground(0xf7, 0x76, 0x8e) + "-"))).toBe(true); // red removal
    expect(lines.some((line) => line.includes(foreground(0x9e, 0xce, 0x6a) + "+"))).toBe(true); // green addition
  });

  test("uses the colors of a light theme", async () => {
    // Act
    const tokyo = await home.run(["themes", "--preview", "tokyo-night"]);
    const white = await home.run(["themes", "--preview", "white"]);

    // Assert
    expect(white.code).toBe(0);
    expect(white.stdout).toContain("white");
    expect(white.stdout).not.toContain(background(0x1a, 0x1b, 0x26));
    expect(white.stdout).not.toBe(tokyo.stdout);
  });

  test("fails for an unknown theme", async () => {
    // Act
    const result = await home.run(["themes", "--preview", "no-such-theme"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toContain("there is no theme 'no-such-theme'");
    expect(result.stdout).toBe("");
  });
});

describe("sbox themes in a terminal", () => {
  test("offers the themes in fzf and switches to the chosen one", async () => {
    // Arrange
    await fakeFzf("nord");

    // Act
    const result = await home.run(["themes"], { tty: true });

    // Assert
    expect(result.stderr).toBe("");
    expect(result.code).toBe(0);
    expect(result.stdout).toContain("Switched to the theme 'nord'.");
    expect(await savedTheme()).toBe("nord");
    const calls = (await fzfCalls())!;
    const items = calls.filter((line) => line.startsWith("item: "));
    expect(items).toHaveLength(24);
    expect(items).toContain("item: tokyo-night");
    expect(calls.some((line) => line.startsWith("arg: ") && line.includes("sbox themes --preview {}"))).toBe(true);
  });

  test("preselects the current theme", async () => {
    // Arrange
    await home.writeState("themes", "current", "nord\n");
    await fakeFzf("nord");

    // Act
    await home.run(["themes"], { tty: true });

    // Assert
    const calls = (await fzfCalls())!;
    const items = calls.filter((line) => line.startsWith("item: ")).map((line) => line.slice("item: ".length));
    const position = items.indexOf("nord") + 1;
    expect(calls).toContain(`arg: load:pos(${position})`);
  });

  test("changes nothing when the choice is cancelled", async () => {
    // Arrange
    await home.writeState("themes", "current", "nord\n");
    await fakeFzf(null);

    // Act
    const result = await home.run(["themes"], { tty: true });

    // Assert
    expect(result.code).toBe(0);
    expect(result.stdout).toBe("");
    expect(await savedTheme()).toBe("nord");
    expect(await fzfCalls()).not.toBeNull();
    expect(await home.exists(".config/opencode/themes/sbox.json")).toBe(false);
  });

  test("fails without changing anything when the chosen theme is invalid", async () => {
    // Arrange
    const nord = await home.file(".local/share/sbox/commands/themes/themes/nord/colors.toml");
    await home.write(".local/share/sbox/commands/themes/themes/broken/colors.toml", `${nord}\ncolor13 = "pink"\n`);
    await home.writeState("themes", "current", "nord\n");
    await fakeFzf("broken");

    // Act
    const result = await home.run(["themes"], { tty: true });

    // Assert
    expect(result.code).toBe(1);
    expect(result.stdout).toBe("");
    expect(result.stderr).toStartWith("sbox themes: the theme 'broken': 'color13' must be a color like #rrggbb");
    expect(await savedTheme()).toBe("nord");
    expect(await home.exists(".config/opencode/themes/sbox.json")).toBe(false);
  });

  test("recolors the terminal with the chosen theme with SBOX_TERMINAL_THEME=1", async () => {
    // Arrange
    await fakeFzf("nord");

    // Act
    const result = await home.run(["themes"], { tty: true, env: { SBOX_TERMINAL_THEME: "1" } });

    // Assert
    expect(result.code).toBe(0);
    expect(result.stdout).toStartWith("\x1b]4;0;#2e3440\x07");
    expect(result.stdout).toContain("\x1b]11;#2e3440\x07\x1b]12;#d8dee9\x07Switched to the theme 'nord'.");
  });

  test("leaves the terminal colors alone without SBOX_TERMINAL_THEME=1 or when cancelled", async () => {
    // Arrange
    await fakeFzf("nord");

    // Act
    const unset = await home.run(["themes"], { tty: true });
    await fakeFzf(null);
    const cancelled = await home.run(["themes"], { tty: true, env: { SBOX_TERMINAL_THEME: "1" } });

    // Assert
    expect(unset.stdout).toStartWith("Switched to the theme 'nord'.");
    expect(cancelled.stdout).toBe("");
  });

  test("lists the themes without opening fzf when it is not a terminal", async () => {
    // Arrange
    await fakeFzf("nord");

    // Act
    const bare = await home.run(["themes"]);
    const list = await home.run(["themes", "--list"]);

    // Assert
    expect(bare).toEqual(list);
    expect(bare.stdout).toContain("* tokyo-night\n");
    expect(await fzfCalls()).toBeNull();
    expect(await savedTheme()).toBeNull();
  });
});
