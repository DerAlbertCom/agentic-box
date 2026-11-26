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
    const child = Bun.spawn([process.execPath, entry, "many"], {
      env: { HOME: home.path, PATH: process.env.PATH },
      stdout: "pipe",
      stderr: "pipe",
    });
    const reader = child.stdout.getReader();
    const first = await reader.read();
    await reader.cancel();
    const [code, stderr] = await Promise.all([child.exited, new Response(child.stderr).text()]);

    // Assert
    expect(new TextDecoder().decode(first.value)).toStartWith("line 0\n");
    expect(code).toBe(0);
    expect(stderr).toBe("");
  });
});
