import { describe, expect, test } from "bun:test";
import { join } from "node:path";
import { useCommandHome } from "../src/testing";

const home = useCommandHome();

describe("the command home", () => {
  test("runs a test tool from its isolated PATH with literal arguments", async () => {
    // Arrange
    await home.write("match.x", "");
    await home.tool("sbox-test-probe", '#!/bin/sh\nprintf "%s\\n" "$@"\ncat\n');
    await home.install("probe", {
      module: `export default {
        summary: "Run the test tool",
        run(args, ctx) {
          const tool = Bun.which("sbox-test-probe", { PATH: ctx.env.PATH });
          if (!tool) throw new Error("test tool not found");
          const result = Bun.spawnSync([tool, ...args], {
            cwd: ctx.paths.home,
            env: ctx.env,
            stdin: new TextEncoder().encode("piped input\\n"),
          });
          ctx.stdout.write(result.stdout.toString());
          ctx.stderr.write(result.stderr.toString());
          return result.exitCode;
        },
      };`,
    });

    // Act
    const result = await home.run(["probe", "--prompt=theme> ", "two words", "{}", "*.x", "{a,b}", "it's literal", ""]);

    // Assert
    expect(result).toEqual({
      code: 0,
      stdout: "--prompt=theme> \ntwo words\n{}\n*.x\n{a,b}\nit's literal\n\npiped input\n",
      stderr: "",
    });
  });

  test("fails to install a package whose declared data folder is missing, naming the folder", async () => {
    // Arrange
    await home.write("sbox-hello/package.json", JSON.stringify({ sbox: { data: ["greetings", "voices"] } }));
    await home.write("sbox-hello/src/command.ts", `export default { summary: "Say hello", run() {} };\n`);
    await home.write("sbox-hello/greetings/en.txt", "hello\n");

    // Act
    const install = home.install("hello", { package: join(home.path, "sbox-hello") });

    // Assert
    await expect(install).rejects.toThrow("sbox-hello declares the data folder 'voices', but it is missing");
  });
});
