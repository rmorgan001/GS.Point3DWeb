/* Copyright(C) 2026  Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

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
