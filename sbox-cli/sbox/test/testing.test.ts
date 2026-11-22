import { describe, expect, test } from "bun:test";
import { join } from "node:path";
import { useCommandHome } from "../src/testing";

const home = useCommandHome();

describe("the command home", () => {
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
