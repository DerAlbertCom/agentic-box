import { describe, expect, test } from "bun:test";
import { join } from "node:path";
import { useCommandHome } from "../src/testing";

const entry = join(import.meta.dir, "../src/main.ts");

const home = useCommandHome();

describe("sbox on a pipe", () => {
  test("stops quietly when the reader closes the pipe early", async () => {
    // Arrange
    await home.install("many", {
      module: `export default {
  summary: "Prints many lines",
  run(args, ctx) {
    for (let i = 0; i < 100000; i++) ctx.stdout.write("line " + i + "\\n");
  },
};
`,
    });

    // Act
    const result = Bun.spawnSync(["sh", "-c", `"$BUN" "$ENTRY" many | head -n 1`], {
      env: { HOME: home.path, PATH: process.env.PATH, BUN: process.execPath, ENTRY: entry },
    });

    // Assert
    expect(result.stdout.toString()).toBe("line 0\n");
    expect(result.stderr.toString()).toBe("");
  });
});
