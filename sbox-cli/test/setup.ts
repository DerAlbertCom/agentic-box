import { existsSync } from "node:fs";
import { delimiter, dirname, join, resolve } from "node:path";
import { prepareTestTools } from "../sbox/src/testing-tools";

// The sandbox's scripts are POSIX shell programs. On Windows, use Git's native shell,
// not the System32 bash.exe launcher, which runs in a different WSL filesystem.
if (process.platform === "win32") {
  const git = Bun.which("git");
  const candidates = [
    ...(git ? [resolve(dirname(git), "../usr/bin"), resolve(dirname(git), "../../usr/bin")] : []),
    join(process.env.ProgramFiles ?? "C:/Program Files", "Git/usr/bin"),
  ];
  const tools = candidates.find((folder) => existsSync(join(folder, "sh.exe")));
  if (tools) process.env.PATH = [tools, process.env.PATH ?? ""].join(delimiter);
}

await prepareTestTools();
