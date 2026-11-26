import { beforeEach } from "bun:test";
import { join } from "node:path";
import { useCommandHome } from "sbox/testing";
import { workingDirectory } from "../../test/shell";

/** This package, to install the themes command from with `home.install("themes", { package: themesPackage })`. */
export const themesPackage = join(import.meta.dir, "..");

/**
 * The command home for each test, with a fake of the tool the apps run first on PATH: `bat` logs
 * its arguments to `~/bat.log` and copies the bat themes it finds to `~/bat.cache`.
 */
export function useThemeHome() {
  const home = useCommandHome();

  beforeEach(async () => {
    await home.tool(
      "bat",
      `#!/bin/sh\necho "${workingDirectory}: bat $@" >> bat.log\ncp .config/bat/themes/*.tmTheme bat.cache 2>/dev/null || true\n`,
    );
  }, process.platform === "win32" ? 60_000 : undefined);

  return home;
}
