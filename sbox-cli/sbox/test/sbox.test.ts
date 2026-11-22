import { describe, expect, test } from "bun:test";
import { join } from "node:path";
import { useCommandHome } from "../src/testing";

const home = useCommandHome();

function withSummary(summary: string) {
  return `export default { summary: ${JSON.stringify(summary)}, run() {} };\n`;
}

describe("sbox <command>", () => {
  test("runs the command's module with the remaining arguments and returns its exit code", async () => {
    // Arrange
    await home.install("hello", {
      module: `export default {
        summary: "Say hello",
        run(args, ctx) {
          ctx.stdout.write("hello " + JSON.stringify(args) + "\\n");
          ctx.stderr.write("from " + ctx.name + "\\n");
          return 3;
        },
      };\n`,
    });

    // Act
    const result = await home.run(["hello", "world", "--loud"]);

    // Assert
    expect(result.code).toBe(3);
    expect(result.stdout).toBe('hello ["world","--loud"]\n');
    expect(result.stderr).toBe("from hello\n");
  });

  test("reports an error thrown by the command and fails", async () => {
    // Arrange
    await home.install("hello", {
      module: `export default { summary: "Fail", run() { throw new Error("no theme named 'x'"); } };\n`,
    });

    // Act
    const result = await home.run(["hello"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toBe("sbox hello: no theme named 'x'\n");
  });

  test.each([
    [
      "throws while loading",
      `throw new Error("Cannot find package 'smol-toml'\\n    at x.js:1");\n`,
      "failed to load: Cannot find package 'smol-toml'",
    ],
    [
      "has no default export",
      `export const summary = "Say oops";\n`,
      "command.js does not export a command: it has no default export",
    ],
  ])("reports a command that %s in one line and fails", async (_, module, reason) => {
    // Arrange
    await home.install("oops", { module });

    // Act
    const result = await home.run(["oops", "now"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stdout).toBe("");
    expect(result.stderr).toBe(`sbox oops: ${reason}\n`);
  });

  test("runs a working command next to a broken one", async () => {
    // Arrange
    await home.install("hello", {
      module: `export default { summary: "Say hello", run(args, ctx) { ctx.stdout.write("hello\\n"); return 4; } };\n`,
    });
    await home.install("oops", { module: `throw new Error("boom");\n` });

    // Act
    const result = await home.run(["hello"]);

    // Assert
    expect(result.code).toBe(4);
    expect(result.stdout).toBe("hello\n");
    expect(result.stderr).toBe("");
  });

  const reportContext = `export default {
    summary: "Report the context",
    run(args, ctx) {
      ctx.stdout.write(JSON.stringify({ paths: ctx.paths, theme: ctx.env.SBOX_THEME, isTTY: ctx.isTTY }));
    },
  };\n`;

  test("hands the command its state and data folders and the environment", async () => {
    // Arrange
    await home.install("hello", { module: reportContext });

    // Act
    const result = await home.run(["hello"], { env: { SBOX_THEME: "nord" } });

    // Assert
    expect(JSON.parse(result.stdout)).toEqual({
      paths: {
        home: home.path,
        state: join(home.path, ".local/state/sbox/hello"),
        data: join(home.path, ".local/share/sbox/commands/hello"),
      },
      theme: "nord",
      isTTY: false,
    });
  });

  test("tells the command when it runs in a terminal", async () => {
    // Arrange
    await home.install("hello", { module: reportContext });

    // Act
    const result = await home.run(["hello"], { tty: true });

    // Assert
    expect(JSON.parse(result.stdout).isTTY).toBe(true);
  });

  const styled = `export default {
    summary: "Style a word",
    run(args, ctx) {
      ctx.stdout.write(ctx.style.green("ok"));
    },
  };\n`;

  test("styles the output in a terminal", async () => {
    // Arrange
    await home.install("hello", { module: styled });

    // Act
    const result = await home.run(["hello"], { tty: true });

    // Assert
    expect(result.stdout).toBe("\x1b[32mok\x1b[39m");
  });

  test("leaves the output plain when it is not a terminal or NO_COLOR is set", async () => {
    // Arrange
    await home.install("hello", { module: styled });

    // Act
    const piped = await home.run(["hello"]);
    const noColor = await home.run(["hello"], { tty: true, env: { NO_COLOR: "1" } });

    // Assert
    expect(piped.stdout).toBe("ok");
    expect(noColor.stdout).toBe("ok");
  });
});

describe("sbox <unknown>", () => {
  test("fails and lists the installed commands", async () => {
    // Arrange
    await home.install("hello", { module: withSummary("Say hello") });

    // Act
    const result = await home.run(["themes", "nord"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stdout).toBe("");
    expect(result.stderr).toContain("sbox: the command 'themes' is not installed.");
    expect(result.stderr).toMatch(/hello\s+Say hello/);
  });

  test("still lists the installed commands when one of them is broken", async () => {
    // Arrange
    await home.install("hello", { module: withSummary("Say hello") });
    await home.install("oops", { module: `export default {};\n` });

    // Act
    const result = await home.run(["themes"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toBe(
      "sbox: the command 'themes' is not installed.\n\n" +
        "Commands:\n" +
        "  hello  Say hello\n" +
        "  oops   broken: command.js does not export a command: its summary is not a string\n",
    );
  });

  test("does not treat a path as a command name", async () => {
    // Arrange
    await home.install("hello", { module: withSummary("Say hello") });

    // Act
    const result = await home.run(["../commands/hello"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toContain("is not installed");
  });
});

describe("sbox --help", () => {
  test("lists the installed commands with their summaries, sorted by name", async () => {
    // Arrange
    await home.install("themes", { module: withSummary("Switch the color theme") });
    await home.install("hello", { module: withSummary("Say hello") });

    // Act
    const result = await home.run(["--help"]);

    // Assert
    expect(result.code).toBe(0);
    expect(result.stdout).toMatch(/hello\s+Say hello\n\s+themes\s+Switch the color theme\n/);
    expect(result.stdout).not.toContain("No commands are installed.");
  });


  test("is also shown without arguments", async () => {
    // Arrange
    await home.install("hello", { module: withSummary("Say hello") });

    // Act
    const result = await home.run([]);

    // Assert
    expect(result.code).toBe(0);
    expect(result.stdout).toMatch(/hello\s+Say hello/);
  });

  test("is also shown with -h", async () => {
    // Arrange

    // Act
    const result = await home.run(["-h"]);

    // Assert
    expect(result.code).toBe(0);
    expect(result.stdout).toContain("Usage: sbox <command> [args...]");
  });

  test("is shown on stderr after an unknown option", async () => {
    // Arrange

    // Act
    const result = await home.run(["--bogus"]);

    // Assert
    expect(result.code).toBe(2);
    expect(result.stdout).toBe("");
    expect(result.stderr).toContain("sbox: Unknown option '--bogus'");
    expect(result.stderr).toContain("Usage: sbox <command> [args...]");
  });

  test("lists the working commands and marks one that throws while loading as broken with the error", async () => {
    // Arrange
    await home.install("hello", { module: withSummary("Say hello") });
    await home.install("oops", { module: `throw new Error("Cannot find package 'smol-toml'\\n    at x.js:1");\n` });

    // Act
    const result = await home.run(["--help"]);

    // Assert
    expect(result.code).toBe(0);
    expect(result.stderr).toBe("");
    expect(result.stdout).toEndWith(
      "Commands:\n" +
        "  hello  Say hello\n" +
        "  oops   broken: failed to load: Cannot find package 'smol-toml'\n",
    );
  });

  test.each([
    ["no default export", `export const summary = "Say oops";\n`, "it has no default export"],
    ["no summary", `export default { run() {} };\n`, "its summary is not a string"],
    ["no run function", `export default { summary: "Say oops" };\n`, "it has no run function"],
  ])("marks a command with %s as broken, as not being a command", async (_, module, reason) => {
    // Arrange
    await home.install("hello", { module: withSummary("Say hello") });
    await home.install("oops", { module });

    // Act
    const result = await home.run(["--help"]);

    // Assert
    expect(result.code).toBe(0);
    expect(result.stderr).toBe("");
    expect(result.stdout).toEndWith(
      "Commands:\n" + "  hello  Say hello\n" + `  oops   broken: command.js does not export a command: ${reason}\n`,
    );
  });

  test("styles the broken marker in a terminal only", async () => {
    // Arrange
    await home.install("oops", { module: `throw new Error("boom");\n` });

    // Act
    const terminal = await home.run(["--help"], { tty: true });
    const noColor = await home.run(["--help"], { tty: true, env: { NO_COLOR: "1" } });

    // Assert
    expect(terminal.stdout).toEndWith("  oops  \x1b[31mbroken\x1b[39m: failed to load: boom\n");
    expect(noColor.stdout).toEndWith("  oops  broken: failed to load: boom\n");
  });

  test("says that no commands are installed when the commands folder is missing", async () => {
    // Arrange

    // Act
    const result = await home.run(["--help"]);

    // Assert
    expect(result.code).toBe(0);
    expect(result.stdout).toContain("Usage: sbox <command> [args...]");
    expect(result.stdout).toContain("No commands are installed.");
  });

  test("says that no commands are installed when the commands folder is a file", async () => {
    // Arrange
    await home.write(".local/share/sbox/commands", "");

    // Act
    const result = await home.run(["--help"]);

    // Assert
    expect(result.code).toBe(0);
    expect(result.stderr).toBe("");
    expect(result.stdout).toContain("No commands are installed.");
  });
});
