# Babylon.js bundle for GS Point3D Web

The 3D viewer (`GS.Point3DWeb/wwwroot/model3d/model3d.js`) uses only a small part of
Babylon.js. This folder builds a trimmed, minified bundle with just those classes, into

    GS.Point3DWeb/wwwroot/model3d/babylon.custom.min.js

The bundle is committed, so the web app builds and runs without Node.js. You only need this
folder when changing what the bundle contains or updating Babylon.js.

## Rebuild

    cd tools/babylon-bundle
    npm ci
    npm run build

## Adding a Babylon class

If `model3d.js` starts using another Babylon class (`window.BABYLON.Something`), import it in
`entry.js`, add it to the `window.BABYLON` object there, and rebuild.

## Updating Babylon.js

Change the `@babylonjs/core` and `@babylonjs/loaders` versions in `package.json` (keep them the
same), run `npm install`, rebuild, and check the 3D views in the app.
