// Bundles entry.js (the Babylon.js classes the viewer uses) into one minified script for the web app.
import { build } from "esbuild";
import { readFileSync, statSync } from "node:fs";

const babylonVersion = JSON.parse(readFileSync("node_modules/@babylonjs/core/package.json", "utf8")).version;
const outfile = "../../GS.Point3DWeb/wwwroot/model3d/babylon.custom.min.js";

await build({
    entryPoints: ["entry.js"],
    outfile,
    bundle: true,
    minify: true,
    format: "iife",
    target: "es2020",
    legalComments: "eof", // keep Babylon.js's licence notices (Apache 2.0)
    banner: {
        js: `/* Babylon.js ${babylonVersion} (trimmed for GS Point3D Web: tools/babylon-bundle). Apache License 2.0. */`
    },
    logLevel: "info"
});

console.log(`Babylon.js ${babylonVersion}: ${(statSync(outfile).size / 1024 / 1024).toFixed(2)} MB -> ${outfile}`);
