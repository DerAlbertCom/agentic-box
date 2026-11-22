import { describe, expect, test } from "bun:test";
import { themesPackage, useThemeHome } from "./theme-home";

const home = useThemeHome();

describe("sbox themes <name> for OpenCode", () => {
  test("renders the OpenCode theme sbox from the palette and selects it", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });

    // Act
    const result = await home.run(["themes", "tokyo-night"]);

    // Assert
    expect(result.stderr).toBe("");
    expect(result.code).toBe(0);
    const theme = await home.json(".config/opencode/themes/sbox.json");
    expect(theme.$schema).toBe("https://opencode.ai/theme.json");
    expect(theme.theme.primary).toEqual({ dark: "#7aa2f7", light: "#7aa2f7" });
    expect(theme.theme.background).toEqual({ dark: "#1a1b26", light: "#1a1b26" });
    expect(theme.theme.backgroundPanel).toEqual({ dark: "#232431", light: "#232431" });
    expect(theme.theme.diffRemovedBg).toEqual({ dark: "#3b2936", light: "#3b2936" });
    expect(await home.json(".config/opencode/cli.json")).toEqual({
      $schema: "https://opencode.ai/v2/cli.json",
      theme: { name: "sbox", mode: "dark" },
    });
  });

  test("selects the light mode for a light theme", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });

    // Act
    await home.run(["themes", "white"]);

    // Assert
    expect((await home.json(".config/opencode/cli.json")).theme).toEqual({ name: "sbox", mode: "light" });
  });

  test("keeps the other settings of OpenCode's cli.json", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });
    await home.write(
      ".config/opencode/cli.json",
      JSON.stringify({ keybinds: { leader: "ctrl+x" }, theme: { name: "tokyo-night", mode: "light" } }),
    );

    // Act
    await home.run(["themes", "nord"]);
    await home.run(["themes", "nord"]);

    // Assert
    expect(await home.json(".config/opencode/cli.json")).toEqual({
      keybinds: { leader: "ctrl+x" },
      theme: { name: "sbox", mode: "dark" },
      $schema: "https://opencode.ai/v2/cli.json",
    });
  });
});

describe("sbox themes <name> for Claude Code", () => {
  test("renders the Claude Code theme sbox from the palette and selects it", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });

    // Act
    const result = await home.run(["themes", "tokyo-night"]);

    // Assert
    expect(result.code).toBe(0);
    const theme = await home.json(".claude/themes/sbox.json");
    expect(theme.name).toBe("sbox");
    expect(theme.base).toBe("dark");
    expect(theme.overrides.claude).toBe("#7aa2f7");
    expect(theme.overrides.claudeShimmer).toBe("#8aa7eb");
    expect(theme.overrides.inactive).toBe("#707590");
    expect(theme.overrides.diffRemovedWord).toBe("#613847");
    expect(theme.overrides.selectionBg).toBe("#292e42");
    expect(await home.json(".claude/settings.json")).toEqual({ theme: "custom:sbox" });
  });

  test("uses the light base for a light theme", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });

    // Act
    await home.run(["themes", "white"]);

    // Assert
    const theme = await home.json(".claude/themes/sbox.json");
    expect(theme.base).toBe("light");
    expect(theme.overrides.text).toBe("#000000");
    expect(theme.overrides.diffAdded).toBe("#e1e1e1");
  });

  test("keeps the other settings of Claude Code's settings.json", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });
    const settings = { model: "opus", permissions: { allow: ["Bash(ls:*)"] }, theme: "dark" };
    await home.write(".claude/settings.json", JSON.stringify(settings, null, 2) + "\n");

    // Act
    await home.run(["themes", "nord"]);
    const first = await home.file(".claude/settings.json");
    await home.run(["themes", "nord"]);

    // Assert
    expect(JSON.parse(first)).toEqual({ ...settings, theme: "custom:sbox" });
    expect(await home.file(".claude/settings.json")).toBe(first);
  });
});


describe("sbox themes <name> for pi", () => {
  test("renders the pi theme sbox from the palette and selects it", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });

    // Act
    const result = await home.run(["themes", "tokyo-night"]);

    // Assert
    expect(result.code).toBe(0);
    const theme = await home.json(".pi/agent/themes/sbox.json");
    expect(theme.name).toBe("sbox");
    expect(theme.vars.background).toBe("#1a1b26");
    expect(theme.vars.selectedBackground).toBe("#2f3954");
    expect(theme.vars.panel).toBe("#232431");
    expect(theme.vars.dimText).toBe("#5f637a");
    expect(theme.colors.userMessageBg).toBe("panel");
    expect(await home.json(".pi/agent/settings.json")).toEqual({ theme: "sbox" });
  });

  test("uses light colors for a light theme", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });

    // Act
    await home.run(["themes", "white"]);

    // Assert
    const theme = await home.json(".pi/agent/themes/sbox.json");
    expect(theme.vars.background).toBe("#ffffff");
    expect(theme.vars.panel).toBe("#f0f0f0");
  });

  test("keeps the other settings of pi's settings.json", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });
    const settings = { defaultProvider: "anthropic", packages: ["npm:pi-skills"], theme: "dark" };
    await home.write(".pi/agent/settings.json", JSON.stringify(settings, null, 2) + "\n");

    // Act
    await home.run(["themes", "nord"]);
    const first = await home.file(".pi/agent/settings.json");
    await home.run(["themes", "nord"]);

    // Assert
    expect(JSON.parse(first)).toEqual({ ...settings, theme: "sbox" });
    expect(await home.file(".pi/agent/settings.json")).toBe(first);
  });
});


describe("sbox themes <name> for the terminal tools", () => {
  test("renders the bat theme sbox and rebuilds bat's cache with it, leaving bat's config to the user", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });
    await home.write(".config/bat/config", "--style=plain\n");

    // Act
    const result = await home.run(["themes", "nord"]);

    // Assert
    expect(result.code).toBe(0);
    const theme = await home.file(".config/bat/themes/sbox.tmTheme");
    expect(theme).toContain("<key>name</key>\n\t<string>sbox</string>");
    expect(theme).toContain("<key>background</key>\n\t\t\t\t<string>#2e3440</string>");
    expect(theme).toContain("<key>lineHighlight</key>\n\t\t\t\t<string>#383e4a</string>");
    expect(theme).toContain("<key>gutterForeground</key>\n\t\t\t\t<string>#616773</string>");
    expect(await home.file(".config/bat/config")).toBe("--style=plain\n");
    expect(await home.file("bat.log")).toBe(`${home.path}: bat cache --build\n`);
    expect(await home.file("bat.cache")).toBe(theme);
  });

  test("renders delta's theme into its own git config with the bat theme sbox and the palette's diff colors", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });

    // Act
    await home.run(["themes", "nord"]);

    // Assert
    const theme = await home.file(".config/git/sbox.gitconfig");
    expect(theme).toStartWith("[delta]\n\tdark = true\n\tsyntax-theme = sbox\n");
    expect(theme).toContain('\tminus-style = syntax "#443b46"\n');
    expect(theme).toContain('\tminus-emph-style = syntax "#5c424d"\n');
    expect(theme).toContain('\tplus-style = syntax "#40494b"\n');
    expect(theme).toContain('\tplus-emph-style = syntax "#536058"\n');
    expect(theme).toContain('\tline-numbers-minus-style = "#bf616a"\n');
    expect(theme).toContain('\tline-numbers-zero-style = "#616773"\n');
    expect(await home.file(".config/git/config")).toBe("[include]\n\tpath = sbox.gitconfig\n");
  });

  test("includes delta's theme once in the git config and keeps the user's settings", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });
    await home.write(".config/git/config", "[user]\n\tname = Someone\n[delta]\n\tside-by-side = true\n");

    // Act
    await home.run(["themes", "nord"]);
    await home.run(["themes", "white"]);

    // Assert
    expect(await home.file(".config/git/config")).toBe(
      "[user]\n\tname = Someone\n[delta]\n\tside-by-side = true\n[include]\n\tpath = sbox.gitconfig\n",
    );
    expect(await home.file(".config/git/sbox.gitconfig")).toContain('\tline-numbers-minus-style = "#2a2a2a"\n');
  });

  test("selects delta's light mode for a light theme", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });

    // Act
    await home.run(["themes", "white"]);

    // Assert
    expect(await home.file(".config/git/sbox.gitconfig")).toStartWith(
      "[delta]\n\tlight = true\n\tsyntax-theme = sbox\n",
    );
  });

  test("renders fzf's options file with the palette's colors", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });

    // Act
    await home.run(["themes", "nord"]);

    // Assert
    expect(await home.file(".config/fzf/sbox")).toBe(
      [
        "--color=fg:#d8dee9,bg:-1,hl:#81a1c1,fg+:#d8dee9,bg+:#434c5e,hl+:#81a1c1",
        "--color=info:#949aa5,prompt:#88c0d0,pointer:#b48ead,marker:#a3be8c",
        "--color=spinner:#b48ead,header:#ebcb8b,border:#616773,gutter:-1",
        "",
      ].join("\n"),
    );
  });

  test("renders lazygit's theme into its own config file with the palette's colors", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });

    // Act
    await home.run(["themes", "nord"]);

    // Assert
    const theme = Bun.YAML.parse(await home.file(".config/lazygit/sbox.yml")) as {
      gui: { theme: Record<string, string[]> };
    };
    expect(theme.gui.theme.activeBorderColor).toEqual(["#81a1c1", "bold"]);
    expect(theme.gui.theme.inactiveBorderColor).toEqual(["#616773"]);
    expect(theme.gui.theme.searchingActiveBorderColor).toEqual(["#d5967a", "bold"]);
    expect(theme.gui.theme.selectedLineBgColor).toEqual(["#434c5e"]);
    expect(theme.gui.theme.inactiveViewSelectedLineBgColor).toEqual(["#3f4551"]);
    expect(theme.gui.theme.defaultFgColor).toEqual(["#d8dee9"]);
  });

  test("leaves lazygit's config.yml to the user and creates an empty one for LG_CONFIG_FILE", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });

    // Act
    await home.run(["themes", "nord"]);
    const created = await home.file(".config/lazygit/config.yml");
    await home.write(".config/lazygit/config.yml", "gui:\n  showIcons: true\n");
    await home.run(["themes", "white"]);

    // Assert
    expect(created).toBe("");
    expect(await home.file(".config/lazygit/config.yml")).toBe("gui:\n  showIcons: true\n");
  });

  test("renders yazi's theme with the palette's colors", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage });

    // Act
    await home.run(["themes", "nord"]);

    // Assert
    const theme = Bun.TOML.parse(await home.file(".config/yazi/theme.toml")) as Record<
      string,
      Record<string, unknown>
    >;
    expect(theme.mgr!.cwd).toEqual({ fg: "#88c0d0", italic: true });
    expect(theme.mgr!.find_position).toEqual({ fg: "#8fbcbb", bold: true });
    expect(theme.tabs!.inactive).toEqual({ fg: "#81a1c1", bg: "#3f4551" });
    expect(theme.status!.overall).toEqual({ fg: "#d8dee9", bg: "#383e4a" });
    expect(theme.filetype!.rules).toContainEqual({ url: "*/", fg: "#81a1c1" });
  });
});


const probe = "mode={{ mode }}\nforeground={{ foreground }}\n";

const dusk = ["background", "foreground", "red", "green", "yellow", "blue", "magenta", "cyan"]
  .map((key, i) => `${key} = "#10101${i}"`)
  .join("\n");

function probeApp(extra = "") {
  return {
    "apps/probe/app.yaml": `files:\n  - template: probe.liquid\n    target: ~/probe.txt\n${extra}`,
    "apps/probe/probe.liquid": probe,
  };
}

describe("app templates", () => {
  test("run the app's commands after rendering", async () => {
    // Arrange
    await home.install("themes", {
      package: themesPackage,
      files: { ...probeApp("run:\n  - fake-bat cache --build\n"), "themes/dusk/colors.toml": dusk },
    });
    await home.tool("fake-bat", `#!/bin/sh\necho "$PWD $@ $(cat probe.txt | head -1)" > ran.txt\n`);

    // Act
    const result = await home.run(["themes", "dusk"]);

    // Assert
    expect(result.code).toBe(0);
    expect(await home.file("ran.txt")).toBe(`${home.path} cache --build mode=dark\n`);
  });

  test("report a failing command and fail after rendering", async () => {
    // Arrange
    await home.install("themes", {
      package: themesPackage,
      files: { ...probeApp("run:\n  - echo broken >&2; exit 3\n"), "themes/dusk/colors.toml": dusk },
    });

    // Act
    const result = await home.run(["themes", "dusk"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toBe(
      "sbox themes: probe: 'echo broken >&2; exit 3' failed with exit code 3:\nbroken\n",
    );
    expect(await home.file("probe.txt")).toStartWith("mode=dark\n");
  });

  test("reject an app template with an unknown key", async () => {
    // Arrange
    await home.install("themes", {
      package: themesPackage,
      files: { ...probeApp("colours: []\n"), "themes/dusk/colors.toml": dusk },
    });

    // Act
    const result = await home.run(["themes", "dusk"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toContain("probe/app.yaml: unknown key 'colours'");
  });

  test("reject an app template that still names an app to restart", async () => {
    // Arrange
    await home.install("themes", {
      package: themesPackage,
      files: { ...probeApp("restart: Probe\n"), "themes/dusk/colors.toml": dusk },
    });

    // Act
    const result = await home.run(["themes", "dusk"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toContain("probe/app.yaml: unknown key 'restart'");
  });

  test("name the template that uses a key outside the palette", async () => {
    // Arrange
    await home.install("themes", {
      package: themesPackage,
      files: {
        ...probeApp(),
        "apps/probe/probe.liquid": "{{ active_border_color }}",
        "themes/dusk/colors.toml": `${dusk}\nactive_border_color = "#101010"\n`,
      },
    });

    // Act
    const result = await home.run(["themes", "dusk"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toStartWith("sbox themes: probe/probe.liquid: undefined variable: active_border_color");
  });
});

const dawn = ["background", "foreground", "red", "green", "yellow", "blue", "magenta", "cyan"]
  .map((key, i) => `${key} = "#e0e0e${i}"`)
  .join("\n");

/** Two apps, alpha with a file and a settings merge, beta with a file; `beta` replaces beta's files. */
function twoApps(beta: Record<string, string> = {}) {
  return {
    "themes/dusk/colors.toml": dusk,
    "themes/dawn/colors.toml": dawn,
    "apps/alpha/app.yaml":
      "files:\n  - template: alpha.liquid\n    target: ~/alpha.txt\n" +
      'settings:\n  - file: ~/alpha.json\n    merge:\n      theme: "{{ background }}"\n',
    "apps/alpha/alpha.liquid": probe,
    "apps/beta/app.yaml": "files:\n  - template: beta.liquid\n    target: ~/beta.txt\n",
    "apps/beta/beta.liquid": probe,
    ...beta,
  };
}

/** What a theme switch may change: the apps' files, their settings and the saved choice. */
async function snapshot() {
  const paths = ["alpha.txt", "alpha.json", "beta.txt", "beta.json", ".local/state/sbox/themes/current"];
  return Promise.all(paths.map(async (path) => ((await home.exists(path)) ? home.file(path) : null)));
}

/** Applying first dawn, then dusk: by switching, or with --apply and the kit's default theme. */
const ways = [
  { way: "sbox themes <name>", from: ["themes", "dawn"], to: ["themes", "dusk"], env: (_: string) => ({}) },
  {
    way: "sbox themes --apply",
    from: ["themes", "--apply"],
    to: ["themes", "--apply"],
    env: (theme: string) => ({ SBOX_THEME: theme }),
  },
];

describe.each(ways)("$way", ({ from, to, env }) => {
  test("changes nothing when a later app's template fails", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage, files: twoApps() });
    await home.run(from, { env: env("dawn") });
    const before = await snapshot();
    await home.install("themes", {
      package: themesPackage,
      files: twoApps({ "apps/beta/beta.liquid": "{{ nope }}" }),
    });

    // Act
    const result = await home.run(to, { env: env("dusk") });

    // Assert
    expect(result.code).toBe(1);
    expect(result.stdout).toBe("");
    expect(result.stderr).toStartWith("sbox themes: beta/beta.liquid: undefined variable: nope");
    expect(await snapshot()).toEqual(before);
    expect(before[0]).toStartWith("mode=light\n");
  });

  test("changes nothing when a later app's settings file is not a JSON object", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage, files: twoApps() });
    await home.run(from, { env: env("dawn") });
    await home.write("beta.json", "[]\n");
    const before = await snapshot();
    const settings = 'settings:\n  - file: ~/beta.json\n    merge:\n      theme: "{{ background }}"\n';
    await home.install("themes", {
      package: themesPackage,
      files: twoApps({ "apps/beta/app.yaml": settings }),
    });

    // Act
    const result = await home.run(to, { env: env("dusk") });

    // Assert
    expect(result.code).toBe(1);
    expect(result.stdout).toBe("");
    expect(result.stderr).toBe(`sbox themes: ${home.path}/beta.json does not hold a JSON object\n`);
    expect(await snapshot()).toEqual(before);
  });

  test("changes nothing when a later app's definition is malformed", async () => {
    // Arrange
    await home.install("themes", { package: themesPackage, files: twoApps() });
    await home.run(from, { env: env("dawn") });
    const before = await snapshot();
    await home.install("themes", {
      package: themesPackage,
      files: twoApps({ "apps/beta/app.yaml": "colours: []\n" }),
    });

    // Act
    const result = await home.run(to, { env: env("dusk") });

    // Assert
    expect(result.code).toBe(1);
    expect(result.stdout).toBe("");
    expect(result.stderr).toContain("beta/app.yaml: unknown key 'colours'");
    expect(await snapshot()).toEqual(before);
  });
});

describe("sbox themes <name> with two apps", () => {
  test("merges the settings of both into one settings file", async () => {
    // Arrange
    const settings = 'settings:\n  - file: ~/alpha.json\n    merge:\n      mode: "{{ mode }}"\n';
    await home.install("themes", {
      package: themesPackage,
      files: twoApps({ "apps/beta/app.yaml": settings }),
    });
    await home.write("alpha.json", JSON.stringify({ keep: true }));

    // Act
    const result = await home.run(["themes", "dusk"]);

    // Assert
    expect(result.code).toBe(0);
    expect(await home.json("alpha.json")).toEqual({ keep: true, theme: "#101010", mode: "dark" });
  });

  test("runs every app's commands when an earlier one fails and keeps the switch", async () => {
    // Arrange
    await home.install("themes", {
      package: themesPackage,
      files: twoApps({
        "apps/alpha/app.yaml": "run:\n  - echo broken >&2; exit 3\n",
        "apps/beta/app.yaml":
          "files:\n  - template: beta.liquid\n    target: ~/beta.txt\nrun:\n  - head -1 beta.txt > ran.txt\n",
      }),
    });

    // Act
    const result = await home.run(["themes", "dusk"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toBe("sbox themes: alpha: 'echo broken >&2; exit 3' failed with exit code 3:\nbroken\n");
    expect(result.stdout).toBe("Switched to the theme 'dusk'. Some apps show it only after a restart.\n");
    expect(await home.file("ran.txt")).toBe("mode=dark\n");
    expect(await home.file(".local/state/sbox/themes/current")).toBe("dusk\n");
  });
});
