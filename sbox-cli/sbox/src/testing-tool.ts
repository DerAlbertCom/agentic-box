// Native Windows entry for a test tool: Windows cannot exec a POSIX shebang file.
// Each hard-linked .exe runs the script beside it, preserving literal arguments.
export {};

declare const TEST_SHELL: string;
const quote = (argument: string) => `'${argument.replaceAll("'", "'\"'\"'")}'`;
// Git's MSYS startup expands globs in native argv. Pass one quoted exec command instead.
const command = ["exec", process.execPath.slice(0, -4), ...process.argv.slice(2)].map(quote).join(" ");
const result = Bun.spawnSync([TEST_SHELL, "-c", command], {
  stdin: "inherit",
  stdout: "inherit",
  stderr: "inherit",
});
process.exit(result.exitCode ?? 1);
