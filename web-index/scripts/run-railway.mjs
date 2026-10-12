import { fileURLToPath } from "node:url";
const [command, ...args] = process.argv.slice(2);
if (!["build", "start"].includes(command)) throw new Error("Expected build or start");
if (Number(process.versions.node.split(".")[0]) < 24) throw new Error("Railway requires Node.js 24+");
process.env.KACHALKA_TARGET = "railway";
const cli = new URL("../node_modules/vinext/dist/cli.js", import.meta.url);
process.argv = [process.execPath, fileURLToPath(cli), command, ...(command === "start" ? ["--hostname", "0.0.0.0"] : []), ...args];
await import(cli.href);
