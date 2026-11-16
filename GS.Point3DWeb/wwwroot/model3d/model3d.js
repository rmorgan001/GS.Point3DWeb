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

// Telescope 3D viewer (Babylon.js) for the Home page.
//
// The scene follows GSServer's Helix 3D view (GS.Server/Controls/HelixViewport3D.xaml and
// Model3D/Model3DVM.cs) so its .obj models and axis offsets work unchanged:
//   - Helix is right-handed with Z up. The Babylon scene is right-handed too, and everything
//     mount-related lives under a root node that turns Helix Z-up into Babylon Y-up and scales
//     Helix units (pier = 500) down to scene units.
//   - The model is moved by GSServer's transform group: translate (1,0,0), then rotate about
//     Z by XAxisOffset, about Y by YAxisOffset, about X by ZAxisOffset, all about the centre
//     (0, YAxisCentre, 525).
//   - The pier is GSServer's LoadPierModel: a plain pier for Alt-Az, a pivot arm for German
//     polar and a latitude-canted pier for polar (fork) mounts.

const base = new URL('./', import.meta.url).href;
let babylonReady = null;

// Every viewer on this page, so a face-north click turns all the views of the same server (setServerKey).
const viewers = new Set();
// The same for the views in other tabs of this app: a click is passed on to them too.
const faceNorthChannel = typeof BroadcastChannel === 'function' ? new BroadcastChannel('gs-point3d-web-face-north') : null;

/** Turns this page's views of the server (by its id) to face north. */
function faceNorthHere(serverKey) {
    for (const viewer of viewers) {
        if (viewer.serverKey === serverKey) viewer.faceNorth();
    }
}

if (faceNorthChannel) {
    faceNorthChannel.onmessage = e => {
        if (typeof e.data?.faceNorth === 'string') faceNorthHere(e.data.faceNorth);
    };
}

// GSServer defaults (Server.settings): ModelPierHeight, ModelBeamWidth, ModelPierPivot.
const PIER_HEIGHT = 500;
const BEAM_WIDTH = 50;
const PIER_PIVOT = 130;
const ROTATION_CENTRE_Z = 525;
const HELIX_TO_SCENE = 7 / PIER_HEIGHT; // pier is 7 scene units tall

// Ground grid (scene units; the pier is 7 tall and the compass base 15 across).
const GRID_SIZE = 160;          // width of the ground plane
const GRID_CELL = 1;            // minor grid spacing
const GRID_MAJOR_EVERY = 5;     // a major line every 5 cells
const GRID_Y = -1.01;           // just under the compass base (its underside is at -1)

// Alignment modes (ASCOM AlignmentModes)
const ALT_AZ = 0, POLAR = 1;

function loadScript(src) {
    return new Promise((resolve, reject) => {
        const el = document.createElement('script');
        el.src = src;
        el.onload = resolve;
        el.onerror = () => {
            el.remove();
            reject(new Error(`Failed to load ${src}`));
        };
        document.head.appendChild(el);
    });
}

function ensureBabylon() {
    if (window.BABYLON && window.BABYLON.SceneLoader) return Promise.resolve();
    // Trimmed Babylon.js build with only what this viewer uses (tools/babylon-bundle).
    // A failed download isn't kept: the next view tries again instead of failing until a reload.
    babylonReady ??= loadScript(base + 'babylon.custom.min.js').catch(error => {
        babylonReady = null;
        throw error;
    });
    return babylonReady;
}

const rad = deg => deg * Math.PI / 180;
const deg = r => r * 180 / Math.PI;

// Default camera: a little above the mount head, from the east-north-east (looking west-south-west) with the
// north compass, from the west-south-west with the south one (see Compass directions below).
const DEFAULT_CAMERA = { alpha: 150, beta: 60, radius: 40, targetX: 0, targetY: 6, targetZ: 0 };

// Compass directions in the scene. The top of the compass image (compassN.png: N, compassS.png: S; the pole the
// mount's polar axis points to) lies along scene +Z, and the image is mirrored onto the disc so east and west
// come out right: with the north compass N = +Z, E = -X, S = -Z, W = +X; with the south one each is turned 180°.
// The bearing (0 = north, 90 = east) of a level direction (x, z) in the scene:
const bearingOf = (x, z, northern) => {
    const b = northern ? deg(Math.atan2(-x, z)) : deg(Math.atan2(x, -z));
    return ((b % 360) + 360) % 360;
};
// The level direction (x, z) of a bearing, the inverse of bearingOf.
const directionOf = (bearing, northern) => {
    const s = Math.sin(rad(bearing)), c = Math.cos(rad(bearing));
    return northern ? { x: -s, z: c } : { x: s, z: -c };
};

// How long the camera takes to swing round to north when the compass readout is clicked.
const FACE_NORTH_MS = 700;

// Distant N E S W letters: standing on the ground this far out (scene units; the compass disc is 15 across).
const CARDINAL_DISTANCE = 50;
const CARDINAL_SIZE = 14; // the letters are built this tall, then scaled to the chosen size
const CARDINAL_SIZES = { small: 8, medium: 11, large: 14, xlarge: 20 };
// See-through, so the letters sit back as landmarks rather than compete with the mount.
const CARDINAL_ALPHA = 0.45;
const CARDINALS = [
    { letter: 'N', bearing: 0, color: '#ef6461' }, // red, like the heading needle's north tip
    { letter: 'E', bearing: 90, color: '#e8e6df' },
    { letter: 'S', bearing: 180, color: '#e8e6df' },
    { letter: 'W', bearing: 270, color: '#e8e6df' }
];

// Highest pixel density drawn at: sharp on high-DPI screens (Retina, 150% Windows scaling) without
// paying for 3x or 4x phone screens.
const MAX_PIXEL_RATIO = 2;

// After the mouse, a finger or a key moves the camera, keep drawing this long (covers the camera's
// inertia, which eases out after the input stops).
const INTERACTION_RENDER_MS = 1000;

// A new position glides from the last one over the time since the previous update (so it moves at the
// mount's own pace), but at most this long, so the model is never more than this far behind the mount.
const MAX_GLIDE_MS = 1000;
// Updates closer together than this, or after the page was hidden, jump instead of gliding.
const MIN_GLIDE_MS = 50;
// Moves smaller than this (degrees, on every axis) jump too: tracking moves the axes a few
// thousandths of a degree per update, which can't be seen, so gliding would only keep the view drawing.
const MIN_GLIDE_DEGREES = 0.1;

/** The signed change from angle a to angle b the short way round, in degrees (-180 to 180). */
const angleDelta = (a, b) => ((((b - a) % 360) + 540) % 360) - 180;

class TelescopeViewer {
    constructor(canvas, options) {
        const B = window.BABYLON;
        this.canvas = canvas;
        // Green Swamp palette: primary = telescope (green), secondary = light (moon white).
        this.primaryColor = options.primaryColor || '#52b788';
        this.secondaryColor = options.secondaryColor || '#e8e6df';
        this.textures = {
            compassN: base + 'compassN.png',
            compassS: base + 'compassS.png'
        };
        this.compassRequest = 0;    // so a compass image that loads late can't replace a newer one
        this.backgroundRequest = 0; // the same for background images
        this.modelMeshes = [];
        this.modelMaterials = [];
        this.pierMeshes = [];
        this.loadCount = 0; // so an older loadModel that finishes late is dropped

        // The pose on screen, and the glide towards the latest one (see setPose).
        this.pose = { x: 0, y: 0, z: 0, yCentre: 0 };
        this.glide = null;
        this.lastPoseAt = 0;

        // A panel over one edge of the scene (the coordinates grid): the model is moved away from it, into
        // the space beside it, by shifting the camera's picture (not the camera), so a saved view is unchanged.
        this.insetElement = null;
        this.insetSide = 'left';
        this.insetObserver = null;
        this.insetPx = 0;              // how far the panel reaches into the canvas, in CSS pixels
        this.screenShift = { x: 0, y: 0 };       // the shift on screen now (px, right / down)
        this.screenShiftTarget = { x: 0, y: 0 }; // where it eases to; set only when the panel or canvas changes
        this.reducedMotion = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;

        // Drawing on demand: a frame is drawn only when something changed (a new pose, a camera move,
        // a texture or model arriving, a resize), not 60 times a second for a scene at rest.
        this.needsRender = true;
        this.interactUntil = 0;

        // No preserveDrawingBuffer or stencil: nothing reads frames back or uses stencil effects.
        // The pixel density is set in resize(), capped at MAX_PIXEL_RATIO.
        this.engine = new B.Engine(canvas, true, { antialias: true, preserveDrawingBuffer: false, stencil: false }, false);
        this.scene = this.createScene();
        this.engine.runRenderLoop(() => this.renderFrame());

        // The canvas lives in a resizable panel, not the whole window. The window's resize event also
        // fires when the page moves to a screen with a different pixel density, or is zoomed.
        this.onWindowResize = () => this.resize();
        this.resizeObserver = new ResizeObserver(this.onWindowResize);
        this.resizeObserver.observe(canvas);
        window.addEventListener('resize', this.onWindowResize);
        this.resize();

        // Camera input: keep drawing while the user moves the camera (and while it eases out).
        this.onInteract = e => {
            if (e.type === 'pointermove' && e.buttons === 0) return; // just hovering
            if (e.type !== 'pointermove') this.turn = null; // the user takes the camera back from a face-north turn
            this.interactUntil = performance.now() + INTERACTION_RENDER_MS;
        };
        for (const type of ['pointerdown', 'pointermove', 'pointerup', 'wheel', 'keydown']) {
            canvas.addEventListener(type, this.onInteract, { passive: true });
        }

        // The compass readout (setHeadingElement): the element, and the bearing it shows now.
        this.headingElement = null;
        this.shownHeading = null;
        this.serverKey = options.serverKey || null;
        viewers.add(this);

        this.setHemisphere(options.northern !== false);
        // Background image URL (relative to the page), or null for a plain dark sky.
        this.setBackground(options.background ?? null);
        this.setGrid(options.grid !== false);
        this.setCardinalSize(options.cardinalSize);
        this.setCardinals(options.cardinals === true);

        // Camera: start from the server's saved view (saved / reset from the back of the card).
        this.setCamera(options.camera);
    }

    /** Asks for a frame to be drawn (the scene changed). */
    requestRender() {
        this.needsRender = true;
    }

    /** The render loop: moves the glide on, and draws a frame only when something changed. */
    renderFrame() {
        if (!this.scene || this.scene.isDisposed) return;
        const now = performance.now();
        const gliding = this.stepGlide(now);
        const shifting = this.stepScreenShift();
        const turning = this.stepTurn(now);
        const c = this.camera;
        const cameraMoving = c.inertialAlphaOffset || c.inertialBetaOffset || c.inertialRadiusOffset
            || c.inertialPanningX || c.inertialPanningY;
        if (!this.needsRender && !gliding && !shifting && !turning && !cameraMoving && now >= this.interactUntil) return;

        this.needsRender = false;
        this.applyScreenShift();
        this.scene.render();
        this.updateHeading();
        // Materials compile in the background: until everything is ready, some meshes are skipped,
        // so keep drawing until they appear.
        if (!this.scene.isReady()) this.needsRender = true;
    }

    /** The compass bearing the camera faces, 0-360 (0 = north, 90 = east). */
    cameraBearing() {
        // An ArcRotateCamera sits at target + (cos alpha, ., sin alpha) and looks back at the target.
        return bearingOf(-Math.cos(this.camera.alpha), -Math.sin(this.camera.alpha), this.northern);
    }

    /**
     * The compass readout over the scene (Model3DView.razor), or null for none: its needle points north on screen
     * and its value says the bearing the camera faces. Updated as frames are drawn, so a still scene costs nothing.
     */
    setHeadingElement(element) {
        if (this.headingElement && this.onHeadingClick) this.headingElement.removeEventListener('click', this.onHeadingClick);
        this.headingElement = element || null;
        this.shownHeading = null;
        // Clicking the readout turns the view to face north (handled here, so it answers at once).
        this.onHeadingClick ??= () => this.faceNorthWithServer();
        if (this.headingElement) this.headingElement.addEventListener('click', this.onHeadingClick);
        this.updateHeading();
    }

    /** The server this view shows (its id), so views of the same server turn north together; null for none. */
    setServerKey(key) {
        this.serverKey = key || null;
    }

    /** Turns this view, and every other view of the same server (here and in the app's other tabs), to face north. */
    faceNorthWithServer() {
        if (!this.serverKey) {
            this.faceNorth();
            return;
        }
        faceNorthHere(this.serverKey);
        faceNorthChannel?.postMessage({ faceNorth: this.serverKey });
    }

    /**
     * Turns the camera about the mount until it faces north, so north is at the top of the screen. The tilt,
     * distance and target stay. It swings the short way round, unless the viewer prefers reduced motion.
     */
    faceNorth() {
        const north = directionOf(0, this.northern);
        // The camera faces (-cos alpha, -sin alpha); make that north.
        const target = Math.atan2(-north.z, -north.x);
        const from = this.camera.alpha;
        const delta = rad(angleDelta(deg(from), deg(target)));
        // Stop any spin the mouse left, so it doesn't carry the camera off north.
        this.camera.inertialAlphaOffset = 0;
        if (Math.abs(delta) < 1e-4) return;
        const reduced = this.reducedMotion?.matches;
        this.turn = reduced ? null : { from, delta, start: performance.now(), duration: FACE_NORTH_MS };
        if (reduced) this.camera.alpha = from + delta;
        this.requestRender();
    }

    /** Moves the face-north turn on. Returns whether a frame needs drawing for it. */
    stepTurn(now) {
        const t = this.turn;
        if (!t) return false;
        const p = Math.min((now - t.start) / t.duration, 1);
        const eased = 1 - Math.pow(1 - p, 3); // ease out
        this.camera.alpha = t.from + t.delta * eased;
        if (p >= 1) this.turn = null;
        return true;
    }

    updateHeading() {
        const el = this.headingElement;
        if (!el || !el.isConnected) return;
        const bearing = Math.round(this.cameraBearing()) % 360;
        if (bearing === this.shownHeading) return;
        this.shownHeading = bearing;
        // Up the screen is the way the camera faces, so north is `bearing` degrees anticlockwise from up.
        const needle = el.querySelector('.heading-needle');
        if (needle) needle.style.transform = `rotate(${-bearing}deg)`;
        const value = el.querySelector('.heading-value');
        if (value) value.textContent = `${String(bearing).padStart(3, '0')}°`;
    }

    /** Matches the canvas to its size on screen and the screen's pixel density (up to MAX_PIXEL_RATIO). */
    resize() {
        const ratio = Math.min(window.devicePixelRatio || 1, MAX_PIXEL_RATIO);
        this.engine.setHardwareScalingLevel(1 / ratio);
        this.engine.resize();
        this.measureInset();
        this.requestRender();
    }

    /**
     * Keeps the model clear of a panel over one edge of the scene (side: 'left', 'right', 'top' or 'bottom'),
     * or of nothing for a null element: the picture is shifted away from that edge by half the panel's reach,
     * so the model sits in the middle of the space that's left.
     */
    setInset(element, side) {
        if (this.insetObserver) this.insetObserver.disconnect();
        this.insetObserver = null;
        this.insetElement = element || null;
        this.insetSide = side || 'left';
        if (this.insetElement) {
            this.insetObserver = new ResizeObserver(() => {
                this.measureInset();
                this.requestRender();
            });
            this.insetObserver.observe(this.insetElement);
        }
        this.measureInset();
        this.requestRender();
    }

    /**
     * How far the panel reaches into the canvas. Layout sizes (offsetLeft / offsetWidth), not on-screen boxes:
     * the card turning over is a 3D transform, which would skew a bounding box but not these. Capped, so a
     * panel filling a narrow view can't push the model off the other side.
     */
    measureInset() {
        const el = this.insetElement;
        const width = this.canvas.clientWidth || 0;
        const height = this.canvas.clientHeight || 0;
        if (!el || !el.isConnected) {
            this.insetPx = 0;
            this.updateScreenShiftTarget();
            return;
        }
        let reach, size;
        switch (this.insetSide) {
            case 'right': reach = width - el.offsetLeft; size = width; break;
            case 'top': reach = el.offsetTop + el.offsetHeight; size = height; break;
            case 'bottom': reach = height - el.offsetTop; size = height; break;
            default: reach = el.offsetLeft + el.offsetWidth; size = width; break;
        }
        this.insetPx = Math.max(0, Math.min(reach, size * 0.55));
        this.updateScreenShiftTarget();
    }

    /** The shift the panel asks for, in screen pixels (x right, y down): away from its edge by half its reach. */
    updateScreenShiftTarget() {
        const half = this.insetPx / 2;
        const t = this.screenShiftTarget;
        switch (this.insetSide) {
            case 'right': t.x = -half; t.y = 0; break;
            case 'top': t.x = 0; t.y = half; break;
            case 'bottom': t.x = 0; t.y = -half; break;
            default: t.x = half; t.y = 0; break;
        }
    }

    /**
     * Eases the shift on screen towards the panel's (true while it is still moving). Runs every frame, so a
     * scene at rest costs two comparisons and nothing more.
     */
    stepScreenShift() {
        const target = this.screenShiftTarget;
        const s = this.screenShift;
        if (s.x === target.x && s.y === target.y) return false;
        const dx = target.x - s.x, dy = target.y - s.y;
        const reduced = this.reducedMotion !== null && this.reducedMotion.matches;
        if (reduced || Math.hypot(dx, dy) < 0.5) {
            s.x = target.x;
            s.y = target.y;
        } else {
            s.x += dx * 0.18;
            s.y += dy * 0.18;
        }
        return true;
    }

    /**
     * Turns the shift in pixels into the camera's targetScreenOffset (view-space units at the target's
     * distance, y up), so it stays the same on screen however far the camera is zoomed out.
     */
    applyScreenShift() {
        const c = this.camera;
        const height = this.canvas.clientHeight || 1;
        const perPx = 2 * c.radius * Math.tan(c.fov / 2) / height;
        const x = this.screenShift.x * perPx;
        const y = -this.screenShift.y * perPx;
        if (c.targetScreenOffset.x !== x) c.targetScreenOffset.x = x;
        if (c.targetScreenOffset.y !== y) c.targetScreenOffset.y = y;
    }

    /** Applies a saved camera ({ alpha, beta (degrees), radius, targetX/Y/Z }), or the default. */
    setCamera(view) {
        const B = window.BABYLON;
        const v = view || DEFAULT_CAMERA;
        // Target first: setTarget works alpha, beta and radius out again from where the camera is now, so set
        // after it they would be overwritten (a saved view would keep only its target).
        this.camera.setTarget(new B.Vector3(v.targetX, v.targetY, v.targetZ));
        this.camera.alpha = rad(v.alpha);
        this.camera.beta = rad(v.beta);
        this.camera.radius = v.radius;
        this.requestRender();
    }

    getCamera() {
        const c = this.camera;
        const round = (n, d) => Math.round(n * 10 ** d) / 10 ** d;
        return {
            alpha: round(((deg(c.alpha) % 360) + 360) % 360, 2), // the camera can wind past 360
            beta: round(deg(c.beta), 2),
            radius: round(c.radius, 3),
            targetX: round(c.target.x, 3),
            targetY: round(c.target.y, 3),
            targetZ: round(c.target.z, 3)
        };
    }


    createScene() {
        const B = window.BABYLON;
        const scene = new B.Scene(this.engine);
        scene.useRightHandedSystem = true; // same handedness as Helix / WPF 3D
        scene.clearColor = B.Color4.FromHexString('#070b0eff');

        this.camera = new B.ArcRotateCamera('Camera', rad(DEFAULT_CAMERA.alpha), rad(DEFAULT_CAMERA.beta),
            DEFAULT_CAMERA.radius, new B.Vector3(DEFAULT_CAMERA.targetX, DEFAULT_CAMERA.targetY, DEFAULT_CAMERA.targetZ), scene);
        this.camera.attachControl(this.canvas, true);
        this.camera.lowerRadiusLimit = 5;
        this.camera.upperRadiusLimit = 200;
        // The wheel zooms by a share of the distance, so it feels the same near and far; a pinch zooms
        // so the model follows the fingers (both, unlike the default fixed steps, scale with distance).
        this.camera.wheelDeltaPercentage = 0.02;
        this.camera.useNaturalPinchZoom = true;

        // Lighting
        const hemi = new B.HemisphericLight('hemisphericLight', new B.Vector3(0, 1, 0), scene);
        hemi.intensity = 0.75;
        hemi.diffuse = B.Color3.FromHexString(this.secondaryColor);
        hemi.specular = new B.Color3(0.2, 0.2, 0.2);
        hemi.groundColor = B.Color3.FromHexString('#172226');

        const dir = new B.DirectionalLight('directionalLight', new B.Vector3(-1, -2, -1), scene);
        dir.position = new B.Vector3(10, 20, 10);
        dir.intensity = 0.55;

        // Skybox: the background image wrapped around the scene (hidden for a plain dark sky).
        this.skybox = B.MeshBuilder.CreateBox('SkyBox', { size: 400 }, scene);
        const skyMat = new B.StandardMaterial('SkyBox', scene);
        skyMat.backFaceCulling = false;
        skyMat.disableLighting = true;
        skyMat.diffuseColor = new B.Color3(0, 0, 0);
        skyMat.specularColor = new B.Color3(0, 0, 0);
        this.skybox.material = skyMat;
        this.skybox.isPickable = false;
        this.skybox.setEnabled(false); // shown once a background image has loaded (setBackground)

        // Root in Helix coordinates: Z up -> scene Y up, Helix units -> scene units.
        this.root = new B.TransformNode('Helix', scene);
        this.root.rotation.x = -Math.PI / 2;
        this.root.scaling = new B.Vector3(HELIX_TO_SCENE, HELIX_TO_SCENE, HELIX_TO_SCENE);

        // Compass base: a disc whose top face is the Helix z = 0 plane (GSServer's compass box).
        const compass = new B.StandardMaterial('Compass', scene);
        // The compass image is drawn inverted (light markings on a dark disc); see setHemisphere.
        compass.diffuseColor = new B.Color3(1, 1, 1);
        compass.emissiveColor = new B.Color3(0.04, 0.06, 0.06);
        compass.specularColor = new B.Color3(0.05, 0.05, 0.05);
        const faceUv = [];
        faceUv[1] = new B.Vector4(0, 0, 0, 0);
        const thickness = 1 / HELIX_TO_SCENE;
        this.compassBase = B.MeshBuilder.CreateCylinder('Compass', {
            height: thickness, diameter: 15 / HELIX_TO_SCENE, faceUV: faceUv, tessellation: 64
        }, scene);
        this.compassBase.material = compass;
        this.compassBase.parent = this.root;
        this.compassBase.rotation.x = Math.PI / 2; // cylinder axis along Helix Z
        this.compassBase.position.z = -thickness / 2;

        this.ground = this.createGround(scene);
        this.cardinals = this.createCardinals(scene);

        this.pierMaterial = new B.StandardMaterial('Pier', scene);
        this.pierMaterial.diffuseColor = B.Color3.FromHexString('#34474f');
        this.pierMaterial.specularColor = new B.Color3(0.1, 0.1, 0.1);

        // The moving part of the mount (the .obj model) hangs off this node.
        this.modelNode = new B.TransformNode('Model', scene);
        this.modelNode.parent = this.root;
        this.modelNode.rotationQuaternion = B.Quaternion.Identity();

        return scene;
    }

    /**
     * The ground: a large plane under the compass base with a tiled grid, fading out towards its
     * edge so it has no hard border. It is see-through, so the background shows between the lines.
     */
    createGround(scene) {
        const B = window.BABYLON;

        // One texture tile = GRID_MAJOR_EVERY x GRID_MAJOR_EVERY cells, major lines on its edges.
        const px = 512, step = px / GRID_MAJOR_EVERY;
        const grid = new B.DynamicTexture('GridTexture', { width: px, height: px }, scene, true);
        const ctx = grid.getContext();
        ctx.clearRect(0, 0, px, px);
        ctx.fillStyle = 'rgba(16, 24, 28, 0.45)'; // faint floor so the mount stands on something
        ctx.fillRect(0, 0, px, px);
        const line = (pos, width, color) => {
            ctx.fillStyle = color;
            ctx.fillRect(pos - width / 2, 0, width, px);
            ctx.fillRect(0, pos - width / 2, px, width);
        };
        for (let i = 1; i < GRID_MAJOR_EVERY; i++) line(i * step, 2, 'rgba(82, 183, 136, 0.28)');
        // Major lines straddle the tile edges, so each tile draws half of them.
        line(0, 6, 'rgba(82, 183, 136, 0.65)');
        line(px, 6, 'rgba(82, 183, 136, 0.65)');
        grid.update();
        grid.hasAlpha = true;
        grid.wrapU = grid.wrapV = B.Texture.WRAP_ADDRESSMODE;
        grid.uScale = grid.vScale = GRID_SIZE / (GRID_CELL * GRID_MAJOR_EVERY);
        grid.anisotropicFilteringLevel = 8; // keeps lines crisp at low viewing angles

        // Radial fade (not tiled): solid in the middle, gone at the edge.
        const fadePx = 256;
        const fade = new B.DynamicTexture('GridFade', { width: fadePx, height: fadePx }, scene, false);
        const fctx = fade.getContext();
        const gradient = fctx.createRadialGradient(fadePx / 2, fadePx / 2, 0, fadePx / 2, fadePx / 2, fadePx / 2);
        gradient.addColorStop(0, 'rgba(255, 255, 255, 1)');
        gradient.addColorStop(0.3, 'rgba(255, 255, 255, 1)');
        gradient.addColorStop(1, 'rgba(255, 255, 255, 0)');
        fctx.clearRect(0, 0, fadePx, fadePx);
        fctx.fillStyle = gradient;
        fctx.fillRect(0, 0, fadePx, fadePx);
        fade.update();
        fade.hasAlpha = true;

        const material = new B.StandardMaterial('Ground', scene);
        material.diffuseTexture = grid;
        material.useAlphaFromDiffuseTexture = true;
        material.opacityTexture = fade;
        // Unshaded: with lighting off the texture shows through the emissive color, so make it white.
        material.disableLighting = true;
        material.emissiveColor = new B.Color3(1, 1, 1);
        material.specularColor = new B.Color3(0, 0, 0);
        material.backFaceCulling = false;   // the camera can tilt below the ground

        const ground = B.MeshBuilder.CreateGround('Ground', { width: GRID_SIZE, height: GRID_SIZE }, scene);
        ground.position.y = GRID_Y;
        ground.material = material;
        ground.isPickable = false;
        return ground;
    }

    /** Shows or hides the ground grid. */
    setGrid(visible) {
        this.ground.setEnabled(!!visible);
        this.requestRender();
    }

    /**
     * The distant N E S W letters: upright on the ground well out from the compass, each turning about the
     * vertical to face the camera, so they read from any side. Hidden until setCardinals; placed by placeCardinals.
     */
    createCardinals(scene) {
        const B = window.BABYLON;
        return CARDINALS.map(c => {
            const px = 256;
            const texture = new B.DynamicTexture(`Cardinal${c.letter}`, { width: px, height: px }, scene, true);
            const ctx = texture.getContext();
            ctx.clearRect(0, 0, px, px);
            ctx.font = `600 ${Math.round(px * 0.8)}px Barlow, "Segoe UI", system-ui, sans-serif`;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.lineWidth = px * 0.06;
            ctx.strokeStyle = 'rgba(7, 11, 14, 0.85)'; // a dark edge, so a letter reads over a light background
            ctx.strokeText(c.letter, px / 2, px * 0.54);
            ctx.fillStyle = c.color;
            ctx.fillText(c.letter, px / 2, px * 0.54);
            texture.update();
            texture.hasAlpha = true;

            const material = new B.StandardMaterial(`Cardinal${c.letter}`, scene);
            material.diffuseTexture = texture;
            material.useAlphaFromDiffuseTexture = true;
            material.alpha = CARDINAL_ALPHA;
            material.disableLighting = true;
            material.emissiveColor = new B.Color3(1, 1, 1);
            material.specularColor = new B.Color3(0, 0, 0);
            material.backFaceCulling = false;

            // The trimmed Babylon build has no CreatePlane: a ground square stood up (baked, so the billboard
            // turning below starts from upright) does the same.
            const plane = B.MeshBuilder.CreateGround(`Cardinal${c.letter}`, { width: CARDINAL_SIZE, height: CARDINAL_SIZE }, scene);
            plane.rotation.x = -Math.PI / 2;
            plane.bakeCurrentTransformIntoVertices();
            plane.material = material;
            plane.billboardMode = B.TransformNode.BILLBOARDMODE_Y; // (BABYLON.Mesh isn't in the trimmed build)
            plane.isPickable = false;
            plane.setEnabled(false);
            return { ...c, plane };
        });
    }

    /** Puts each letter at its bearing for the compass shown (north and south compasses are turned 180°). */
    placeCardinals() {
        if (!this.cardinals) return;
        const size = this.cardinalSize ?? CARDINAL_SIZE;
        for (const c of this.cardinals) {
            const d = directionOf(c.bearing, this.northern);
            c.plane.scaling.setAll(size / CARDINAL_SIZE);
            // Standing on the ground whatever its size.
            c.plane.position.set(d.x * CARDINAL_DISTANCE, GRID_Y + size / 2, d.z * CARDINAL_DISTANCE);
        }
        this.requestRender();
    }

    /** Letter size: 'small', 'medium', 'large' (the default) or 'xlarge'. */
    setCardinalSize(key) {
        this.cardinalSize = CARDINAL_SIZES[key] ?? CARDINAL_SIZES.large;
        this.placeCardinals();
    }

    /** Shows or hides the distant N E S W letters. */
    setCardinals(visible) {
        for (const c of this.cardinals) c.plane.setEnabled(!!visible);
        this.requestRender();
    }

    /** Helix BoxVisual3D: Length = X, Width = Y, Height = Z, rotated about the X axis around a pivot. */
    addBeam(name, size, centre, angleDeg, pivot) {
        const B = window.BABYLON;
        const box = B.MeshBuilder.CreateBox(name, { width: size.x, height: size.y, depth: size.z }, this.scene);
        box.material = this.pierMaterial;
        box.parent = this.root;
        const rotation = B.Matrix.RotationX(rad(angleDeg));
        const offset = B.Vector3.TransformCoordinates(centre.subtract(pivot), rotation);
        box.position = offset.add(pivot);
        box.rotation.x = rad(angleDeg);
        this.pierMeshes.push(box);
    }

    /** Port of GSServer Model3DVM.LoadPierModel. */
    buildPier(mode, latitude) {
        const B = window.BABYLON;
        this.pierMeshes.forEach(m => m.dispose());
        this.pierMeshes = [];

        const h = PIER_HEIGHT, bw = BEAM_WIDTH, half = bw / 2;
        let lowerLength = h, lowerAngle = 90;
        let upperLength = 0, upperAngle = 90;

        if (mode === POLAR) {
            // Latitude-canted two-beam pier
            const pier = { x: 0, y: h - bw };
            upperLength = 0.8 * h;
            const lat = Math.abs(latitude);
            const len = upperLength - bw;
            const upper = { x: Math.cos(rad(lat)) * len, y: -Math.sin(rad(lat)) * len };
            upperAngle = lat - 90;
            const lower = { x: pier.x + upper.x, y: pier.y + upper.y };
            lowerLength = Math.hypot(lower.x, lower.y);
            // WPF Vector.AngleBetween(lower, pier): signed angle from lower to pier, degrees.
            const angleBetween = Math.atan2(lower.x * pier.y - lower.y * pier.x, lower.x * pier.x + lower.y * pier.y) * 180 / Math.PI;
            lowerAngle = 90 - angleBetween;
        } else if (mode !== ALT_AZ) {
            // German polar: vertical pier with a 90 degree pivot arm
            upperLength = PIER_PIVOT;
        }

        this.addBeam('LowerPierBeam', new B.Vector3(bw, lowerLength, bw),
            new B.Vector3(0, lowerLength / 2, half), lowerAngle, new B.Vector3(0, 0, half));
        if (upperLength > 0) {
            // Rotated about -X, so the angle is negated for a rotation about +X.
            this.addBeam('UpperPierBeam', new B.Vector3(bw, bw, upperLength),
                new B.Vector3(0, 0, h - upperLength / 2), -upperAngle, new B.Vector3(0, 0, h));
        }
    }

    /**
     * Loads a model and its pier. Calls can overlap (the model can change while one is downloading);
     * only the latest one is kept, and the old model stays on screen until the new one is ready.
     * Returns 'Superseded' for a load that a newer one replaced.
     */
    async loadModel(url, mount) {
        const B = window.BABYLON;
        const load = ++this.loadCount;

        // Materials are set below, so don't request a .mtl file (avoids a 404).
        if (B.OBJFileLoader) B.OBJFileLoader.SKIP_MATERIALS = true;

        const slash = url.lastIndexOf('/') + 1;
        const result = await B.SceneLoader.ImportMeshAsync('', url.substring(0, slash), url.substring(slash), this.scene, null, '.obj');
        if (load !== this.loadCount || this.scene.isDisposed) {
            result.meshes.forEach(m => m.dispose());
            return 'Superseded';
        }
        const meshes = result.meshes.filter(m => m.getTotalVertices() > 0);
        if (meshes.length === 0) {
            result.meshes.forEach(m => m.dispose());
            return 'Error: model has no meshes';
        }

        // Replace the previous model, its materials and the pier.
        this.modelMeshes.forEach(m => m.dispose());
        this.modelMeshes = [];
        this.modelMaterials.forEach(m => m.dispose());
        this.buildPier(mount?.mode ?? 2, mount?.latitude ?? 0);

        // As GSServer: object 0 is the OTA, 1 the counterweights (German polar) or a second OTA,
        // 2 the bar.
        const ota = new B.StandardMaterial('OTA', this.scene);
        ota.diffuseColor = B.Color3.FromHexString(this.primaryColor);
        this.otaMaterial = ota;
        ota.specularColor = new B.Color3(0.3, 0.3, 0.3);
        const weights = new B.StandardMaterial('Weights', this.scene);
        weights.diffuseColor = new B.Color3(0.25, 0.27, 0.28);
        const bar = new B.StandardMaterial('Bar', this.scene);
        bar.diffuseColor = new B.Color3(0.86, 0.86, 0.84);
        this.modelMaterials = [ota, weights, bar];
        const germanPolar = (mount?.mode ?? 2) === 2;

        meshes.forEach((mesh, i) => {
            mesh.parent = this.modelNode;
            mesh.material = i === 1 && germanPolar ? weights : i === 2 ? bar : ota;
            this.modelMeshes.push(mesh);
        });
        result.meshes.filter(m => m.getTotalVertices() === 0).forEach(m => m.dispose());

        this.applyPose();
        return 'Model loaded successfully';
    }

    /**
     * GSServer's model transform: XAxisOffset (about Z), YAxisOffset (about Y),
     * ZAxisOffset (about X), centred on (0, yCentre, 525).
     *
     * The model glides to the new pose rather than jumping: each angle moves the short way round,
     * like the mount's own axes, over the time since the previous update (at most MAX_GLIDE_MS).
     */
    setPose(xAxisOffset, yAxisOffset, zAxisOffset, yAxisCentre) {
        const target = { x: xAxisOffset, y: yAxisOffset, z: zAxisOffset, yCentre: yAxisCentre };
        const now = performance.now();
        const sincePrevious = now - this.lastPoseAt;
        const first = this.lastPoseAt === 0;
        this.lastPoseAt = now;

        // The same pose as on screen (a parked or still mount, every poll): nothing to draw.
        if (!first && !this.glide && ['x', 'y', 'z', 'yCentre'].every(k => this.pose[k] === target[k])) return;

        const duration = Math.min(sincePrevious, MAX_GLIDE_MS);
        const largest = Math.max(...['x', 'y', 'z'].map(k => Math.abs(angleDelta(this.pose[k], target[k]))));
        if (first || document.hidden || duration < MIN_GLIDE_MS || largest < MIN_GLIDE_DEGREES
            || target.yCentre !== this.pose.yCentre) {
            // First pose, a hidden page, updates too close together, a move too small to see (tracking),
            // or a different mount type: jump.
            this.glide = null;
            this.pose = target;
            this.applyPose();
            return;
        }
        this.glide = { from: { ...this.pose }, to: target, start: now, duration };
        this.requestRender();
    }

    /** Moves the glide on to time `now`. Returns whether a frame needs drawing for it. */
    stepGlide(now) {
        const g = this.glide;
        if (!g) return false;
        const t = Math.min((now - g.start) / g.duration, 1);
        // Shortest way round, so 359 to 1 degree moves 2 degrees, not 358.
        const angle = (a, b) => a + angleDelta(a, b) * t;
        this.pose = { x: angle(g.from.x, g.to.x), y: angle(g.from.y, g.to.y), z: angle(g.from.z, g.to.z), yCentre: g.to.yCentre };
        if (t >= 1) {
            this.pose = g.to;
            this.glide = null;
        }
        this.applyPose();
        return true;
    }

    applyPose() {
        const B = window.BABYLON;
        this.requestRender();
        const { x, y, z, yCentre } = this.pose;
        const centre = new B.Vector3(0, yCentre, ROTATION_CENTRE_Z);
        const toCentre = B.Matrix.Translation(-centre.x, -centre.y, -centre.z);
        const back = B.Matrix.Translation(centre.x, centre.y, centre.z);
        const about = r => toCentre.multiply(r).multiply(back);

        // Row vectors, applied left to right like a WPF Transform3DGroup.
        const m = B.Matrix.Translation(1, 0, 0)
            .multiply(about(B.Matrix.RotationZ(rad(x))))
            .multiply(about(B.Matrix.RotationY(rad(y))))
            .multiply(about(B.Matrix.RotationX(rad(z))));

        const scale = new B.Vector3(), rotation = new B.Quaternion(), position = new B.Vector3();
        m.decompose(scale, rotation, position);
        this.modelNode.position = position;
        this.modelNode.rotationQuaternion = rotation;
    }

    /** Telescope (OTA) color, #rrggbb. */
    setModelColor(hex) {
        this.primaryColor = hex;
        if (this.otaMaterial) this.otaMaterial.diffuseColor = window.BABYLON.Color3.FromHexString(hex);
        this.requestRender();
    }

    /**
     * Background image URL (relative to the page), or null for a plain dark sky. The current
     * background stays until the new image has downloaded, so switching doesn't flash black.
     */
    setBackground(url) {
        const B = window.BABYLON;
        const request = ++this.backgroundRequest;
        const material = this.skybox.material;
        const swap = texture => {
            const old = material.reflectionTexture;
            material.reflectionTexture = texture;
            this.skybox.setEnabled(!!texture);
            if (old && old !== texture) old.dispose();
            this.requestRender();
        };

        if (!url) {
            swap(null);
            return;
        }
        const href = new URL(url, document.baseURI).href;
        const texture = new B.Texture(href, this.scene, true, true, B.Texture.TRILINEAR_SAMPLINGMODE,
            () => {
                // A newer choice (or a closed view) wins over an image that arrives late.
                if (request !== this.backgroundRequest || this.scene.isDisposed) {
                    texture.dispose();
                    return;
                }
                swap(texture);
            },
            (message) => {
                if (request === this.backgroundRequest) console.warn(`GS Point3D Web: background image ${href} failed to load`, message);
                texture.dispose();
            });
        texture.coordinatesMode = B.Texture.FIXED_EQUIRECTANGULAR_MODE;
        texture.level = 0.55; // dim the image so the mount stands out
    }

    setHemisphere(isNorthern) {
        const B = window.BABYLON;
        // The letters and the heading follow the compass at once; the image follows when it has loaded.
        this.northern = !!isNorthern;
        this.placeCardinals();
        this.shownHeading = null;
        this.updateHeading();
        const url = isNorthern ? this.textures.compassN : this.textures.compassS;
        const request = ++this.compassRequest;
        const image = new Image();
        image.onload = () => {
            if (this.scene.isDisposed || request !== this.compassRequest) return;
            // The compass image is dark markings on a transparent background. Draw it inverted
            // (light markings) over the panel color, so it reads as a dark disc.
            const size = 1024;
            const texture = new B.DynamicTexture('CompassTexture', { width: size, height: size }, this.scene, true);
            const ctx = texture.getContext();
            ctx.fillStyle = '#10181c';
            ctx.fillRect(0, 0, size, size);
            // The right-handed scene shows the cap texture mirrored; draw it mirrored so the
            // letters read correctly and E / W are on the right sides.
            ctx.save();
            ctx.translate(size, 0);
            ctx.scale(-1, 1);
            ctx.filter = 'invert(1)';
            ctx.drawImage(image, 0, 0, size, size);
            ctx.restore();
            texture.update();
            const old = this.compassBase.material.diffuseTexture;
            this.compassBase.material.diffuseTexture = texture;
            if (old) old.dispose();
            this.requestRender();
        };
        image.onerror = () => {
            if (request === this.compassRequest) console.warn(`GS Point3D Web: compass image ${url} failed to load`);
        };
        image.src = url;
    }

    dispose() {
        viewers.delete(this);
        this.setHeadingElement(null);
        this.resizeObserver.disconnect();
        if (this.insetObserver) this.insetObserver.disconnect();
        window.removeEventListener('resize', this.onWindowResize);
        for (const type of ['pointerdown', 'pointermove', 'pointerup', 'wheel', 'keydown']) {
            this.canvas.removeEventListener(type, this.onInteract);
        }
        this.engine.stopRenderLoop();
        this.scene.dispose();
        this.engine.dispose();
    }
}

// ===== Blazor interop =====

export async function create(canvas, options) {
    await ensureBabylon();
    return new TelescopeViewer(canvas, options || {});
}

/** mount: { mode: ASCOM alignment mode (0 Alt-Az, 1 Polar, 2 German polar), latitude } */
export function loadModel(viewer, url, mount) {
    return viewer.loadModel(new URL(url, document.baseURI).href, mount);
}

export function setPose(viewer, xAxisOffset, yAxisOffset, zAxisOffset, yAxisCentre) {
    viewer.setPose(xAxisOffset, yAxisOffset, zAxisOffset, yAxisCentre);
}

export function setCamera(viewer, view) {
    viewer.setCamera(view);
}

export function getCamera(viewer) {
    return viewer.getCamera();
}

export function setModelColor(viewer, hex) {
    viewer.setModelColor(hex);
}

export function setBackground(viewer, url) {
    viewer.setBackground(url);
}

export function setGrid(viewer, visible) {
    viewer.setGrid(visible);
}

export function setCardinals(viewer, visible) {
    viewer.setCardinals(visible);
}

/** size: 'small', 'medium', 'large' or 'xlarge'. */
export function setCardinalSize(viewer, size) {
    viewer.setCardinalSize(size);
}

/** element: the compass readout (a .heading-needle and a .heading-value inside), or null for none. */
export function setHeadingElement(viewer, element) {
    viewer.setHeadingElement(element);
}

/** key: the id of the server the view shows (views of the same server turn north together), or null. */
export function setServerKey(viewer, key) {
    viewer.setServerKey(key);
}

/** element: a panel over one edge of the scene to keep the model clear of, or null for none; side: left, right, top or bottom. */
export function setInset(viewer, element, side) {
    viewer.setInset(element, side);
}

export function setHemisphere(viewer, isNorthern) {
    viewer.setHemisphere(isNorthern);
}

export function dispose(viewer) {
    viewer.dispose();
}
