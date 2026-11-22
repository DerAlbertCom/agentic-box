import { afterEach, beforeEach, describe, expect, test } from "bun:test";
import { mkdir, mkdtemp, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { syncThemes, type ThemeSource } from "../src/sync";

let themes: string;

beforeEach(async () => {
  themes = join(await mkdtemp(join(tmpdir(), "sync-themes-")), "themes");
  await mkdir(themes);
});

afterEach(async () => {
  await rm(join(themes, ".."), { recursive: true, force: true });
});

const collection: ThemeSource = { repo: "example/collection", ref: "main" };
const single: ThemeSource = { repo: "someone/jade-theme", ref: "trunk", theme: "jade" };

/** A GitHub stand-in serving the trees API and raw files of the given repositories. */
function gitHub(repos: Record<string, Record<string, string>>): typeof fetch {
  return (async (input: string | URL | Request) => {
    const url = String(input);
    const tree = url.match(
      /^https:\/\/api\.github\.com\/repos\/([^/]+\/[^/]+)\/git\/trees\/([^?]+)\?recursive=1$/,
    );
    if (tree) {
      const files = repos[`${tree[1]}@${tree[2]}`];
      if (!files) return new Response("not found", { status: 404 });
      const paths = Object.keys(files).flatMap((path) => [path, ...parents(path)]);
      const entries = [...new Set(paths)].map((path) => ({ path, type: path in files ? "blob" : "tree" }));
      return Response.json({ tree: entries, truncated: false });
    }
    const raw = url.match(/^https:\/\/raw\.githubusercontent\.com\/([^/]+\/[^/]+)\/([^/]+)\/(.+)$/);
    const content = raw && repos[`${raw[1]}@${raw[2]}`]?.[raw[3]!];
    return content === undefined || content === null
      ? new Response("not found", { status: 404 })
      : new Response(content);
  }) as typeof fetch;
}

function parents(path: string): string[] {
  const parts = path.split("/").slice(0, -1);
  return parts.map((_, index) => parts.slice(0, index + 1).join("/"));
}

/** The text of a complete colors.toml with the given background. */
function colors(background: string) {
  const others = ["foreground", "red", "green", "yellow", "blue", "magenta", "cyan"];
  return [`background = "${background}"`, ...others.map((key) => `${key} = "#c0c0c0"`)].join("\n") + "\n";
}

async function local(path: string) {
  return readFile(join(themes, path), "utf8");
}

describe("syncThemes", () => {
  test("adds every theme with the licenses and reports them", async () => {
    // Arrange
    const fetch = gitHub({
      "example/collection@main": {
        LICENSE: "MIT, the collection\n",
        "themes/nord/colors.toml": colors("#2e3440"),
        "themes/nord/preview.png": "png",
        "themes/dusk/colors.toml": colors("#101010"),
        "themes/dusk/LICENSE": "MIT, dusk\n",
        "docs/colors.toml": "not a theme",
      },
      "someone/jade-theme@trunk": {
        LICENSE: "MIT, jade\n",
        "colors.toml": colors("#111c18"),
        "README.md": "jade",
      },
    });

    // Act
    const report = await syncThemes({ themes, sources: [collection, single], fetch });

    // Assert
    expect(report).toEqual({ added: ["dusk", "jade", "nord"], updated: [], unchanged: [], removed: [] });
    expect(await readdir(themes)).toEqual(expect.arrayContaining(["LICENSE", "dusk", "jade", "nord"]));
    expect(await local("LICENSE")).toBe("MIT, the collection\n");
    expect(await local("nord/colors.toml")).toBe(colors("#2e3440"));
    expect(await readdir(join(themes, "nord"))).toEqual(["colors.toml"]);
    expect(await local("dusk/LICENSE")).toBe("MIT, dusk\n");
    expect(await local("jade/colors.toml")).toBe(colors("#111c18"));
    expect(await local("jade/LICENSE")).toBe("MIT, jade\n");
  });

  test("updates changed themes, keeps the ones removed upstream and reports both", async () => {
    // Arrange
    await mkdir(join(themes, "nord"));
    await writeFile(join(themes, "nord/colors.toml"), colors("#000000"));
    await mkdir(join(themes, "dusk"));
    await writeFile(join(themes, "dusk/colors.toml"), colors("#101010"));
    await mkdir(join(themes, "gone"));
    await writeFile(join(themes, "gone/colors.toml"), colors("#202020"));
    const fetch = gitHub({
      "example/collection@main": {
        LICENSE: "MIT, the collection\n",
        "themes/nord/colors.toml": colors("#2e3440"),
        "themes/dusk/colors.toml": colors("#101010"),
      },
    });

    // Act
    const report = await syncThemes({ themes, sources: [collection], fetch });

    // Assert
    expect(report).toEqual({ added: [], updated: ["nord"], unchanged: ["dusk"], removed: ["gone"] });
    expect(await local("nord/colors.toml")).toBe(colors("#2e3440"));
    expect(await local("gone/colors.toml")).toBe(colors("#202020"));
  });

  test("fails without touching the themes when a source cannot be read", async () => {
    // Arrange
    const fetch = gitHub({ "example/collection@main": { "themes/nord/colors.toml": "x" } });

    // Act
    const sync = syncThemes({ themes, sources: [collection, single], fetch });

    // Assert
    await expect(sync).rejects.toThrow("someone/jade-theme");
    expect(await readdir(themes)).toEqual([]);
  });

  test("fails without touching the themes when a downloaded palette is invalid", async () => {
    // Arrange
    await mkdir(join(themes, "nord"));
    await writeFile(join(themes, "nord/colors.toml"), colors("#000000"));
    const fetch = gitHub({
      "example/collection@main": {
        "themes/nord/colors.toml": colors("#2e3440"),
        "themes/dusk/colors.toml": `${colors("#101010")}color13 = "#fff; rm -rf ~"\n`,
      },
    });

    // Act
    const sync = syncThemes({ themes, sources: [collection], fetch });

    // Assert
    await expect(sync).rejects.toThrow(
      `example/collection@main: the theme 'dusk': 'color13' must be a color like #rrggbb, not "#fff; rm -rf ~"`,
    );
    expect(await readdir(themes)).toEqual(["nord"]);
    expect(await local("nord/colors.toml")).toBe(colors("#000000"));
  });
});
