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
const CAMERA_MIN_HEIGHT = 1;    // the camera stays at least this far above the ground
const CAMERA_MIN_Y = GRID_Y + CAMERA_MIN_HEIGHT;

// Assembled mount (EXPERIMENTAL; loadAssembly): a support, a mount and an OTA, built here from simple shapes (the OTA
// can also be an .obj model's tube). Millimetres throughout.
const ASSEMBLY_SCALE = 0.6;             // Helix units per millimetre: the tripod's feet well inside the compass
const ASSEMBLY_COLUMN_TOP = 1248;       // top of the full-size tripod's centre column (and the piers), where a mount sits
// The supports (the AssemblySupport names in lower case): the tripod (buildTripod) at a scale (its top scales with
// it), or a pier (buildPier) of a diameter in millimetres (pier tops are at ASSEMBLY_COLUMN_TOP).
const ASSEMBLY_SUPPORTS = {
    // thick: the legs' thickness on top of the scale, so a bigger tripod is sturdier, not just taller.
    smalltripod: { tripod: 0.82, thick: 0.9 },
    mediumtripod: { tripod: 1, thick: 1.25 },
    largetripod: { tripod: 1.2, thick: 1.6 },
    smallpier: { pier: 152 },      // 6 inches
    mediumpier: { pier: 254 },     // 10 inches
    largepier: { pier: 305 }       // 12 inches
};
// The OTA: an .obj model's tube (its first object), scaled, its lowest point sat on the saddle. The .obj models
// put their Dec axis at y = -102; each tube keeps its own place along it.
const ASSEMBLY_OTA = { scale: 1.5, decAxisY: -102 };   // scale: unless the mount sets its own otaScale
// The OTAs an assembled model can carry: the .obj models (German polar) whose first object is the tube.
const ASSEMBLY_OTAS = ['Refractor', 'Reflector', 'SchmidtCassegrain', 'RitcheyChretien', 'RitcheyChretienTruss'];
// OTAs built here from simple shapes instead (the AssemblyOta names): each one's builder.
const ASSEMBLY_BUILT_OTAS = {
    Newtonian: (...a) => buildNewtonian(...a),
    MaksutovCassegrain: (...a) => buildMaksutov(...a)
};
// The mounts an assembled model can have: each one's builders.
//
// Each German equatorial mount's measurements (millimetres) and builders:
//   hinge       the latitude hinge, on the polar axis, above the support's top (the builders get it as mount.top)
//   decAlongRa  where the Dec axis crosses the polar axis, up the polar axis from the hinge
//   saddleTop   the top of the saddle's jaws, up the Dec axis from the polar axis (the OTA sits on it)
//   otaScale    how big the OTA is drawn on this mount (the .obj tube's scale), to suit the mount's size
//   base(s, holder, m, mount)          the fixed mount base, in the holder's frame
//   head(s, { latitude, ra, dec }, m, mount)  the parts on each joint, in that joint's frame
// To add a mount: an entry here (its key is the AssemblyMount name in lower case), its builders, and the
// AssemblyMount value and display name in Model3D.cs.
// Centre-balanced mounts: how far their counterweight bar sits along the polar axis toward the pole, from the Dec
// axis, so the bar and weight clear the pier through a full turn of RA at any latitude from 10° to 70° (about 70 mm
// to spare for the CEM40, 90 mm for the CEM70).
const CEM40_BAR_Y = -140;
const CEM70_BAR_Y = -160;
// The CQ350 Pro is laid out the same way: its bar hangs from an arm at the RA drum's north face (about 50 mm to spare).
const CQ350_BAR_Y = -130;

const ASSEMBLY_MOUNTS = {
    eq6rpro: { hinge: 142, decAlongRa: 230, saddleTop: 222, otaScale: 1.5, base: (...a) => buildEq6rBase(...a), head: (...a) => buildEq6rHead(...a) },
    heq5pro: { hinge: 116, decAlongRa: 176, saddleTop: 168, otaScale: 1.15, base: (...a) => buildHeq5Base(...a), head: (...a) => buildHeq5Head(...a) },
    eq8rpro: { hinge: 212, decAlongRa: 300, saddleTop: 314, otaScale: 2.1, base: (...a) => buildEq8Base(...a), head: (...a) => buildEq8Head(...a) },
    am5: { hinge: 96, decAlongRa: 150, saddleTop: 190, otaScale: 1.05, base: (...a) => buildAm5Base(...a), head: (...a) => buildAm5Head(...a) },
    // Centre-balanced: the Dec axis crosses the polar axis just above the hinge, so the load sits over the pier.
    cem70: { hinge: 172, decAlongRa: 40, saddleTop: 300, otaScale: 1.75, base: (...a) => buildCem70Base(...a), head: (...a) => buildCem70Head(...a) },
    // A fork: the OTA hangs between two arms on the Dec axis (centred on it, no saddle or counterweights), on a
    // wedge for a polar mount or straight up for Alt-Az.
    fork: { hinge: 200, decAlongRa: 300, saddleTop: 0, otaScale: 0.8, fork: true, base: (...a) => buildForkBase(...a), head: (...a) => buildForkHead(...a) },
    cem40: { hinge: 124, decAlongRa: 30, saddleTop: 216, otaScale: 1.35, base: (...a) => buildCem40Base(...a), head: (...a) => buildCem40Head(...a) },
    cq350pro: { hinge: 180, decAlongRa: 20, saddleTop: 248, otaScale: 1.8, base: (...a) => buildCq350Base(...a), head: (...a) => buildCq350Head(...a) },
    eqm35pro: { hinge: 100, decAlongRa: 150, saddleTop: 140, otaScale: 0.95, base: (...a) => buildEqm35Base(...a), head: (...a) => buildEqm35Head(...a) },
    wave150ipro: { hinge: 96, decAlongRa: 150, saddleTop: 140, otaScale: 1.2, base: (...a) => buildWave150Base(...a), head: (...a) => buildWave150Head(...a) }
};

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
    { letter: 'N', bearing: 0, role: 'red' }, // red, like the heading needle's north tip
    { letter: 'E', bearing: 90, role: 'text' },
    { letter: 'S', bearing: 180, role: 'text' },
    { letter: 'W', bearing: 270, role: 'text' }
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

// The star field background (a background of { generated: 'stars' }): drawn here onto an equirectangular texture, the
// same layout the panoramas have, so it needs no download and no extra Babylon class.
const STAR_TEXTURE_WIDTH = 4096;    // the height is half of it; lowered for a GPU whose largest texture is smaller
const STAR_COUNT = 3000;            // all above the horizon
const HORIZON_GLOW_DEGREES = 7;     // a faint glow this high above the horizon (light pollution, airglow)
const STAR_EXTINCTION_DEGREES = 15; // stars dim towards the horizon, below this elevation
// The ground below the star field's horizon is the theme's well colour. Over the star field the ground's floor is that colour
// nearly solid, so the grid's fade into the distance meets it without a seam; over a picture the floor stays faint.
const GROUND_FLOOR_SOLID_ALPHA = 0.92;
const GROUND_FLOOR_FAINT_ALPHA = 0.45;
const STAR_SEED = 0x50494E54;       // fixed, so the sky is the same on every load and in every view

// ----- The colour theme (wwwroot/theme.js sets the CSS variables; the scene follows them) -----
const themeProbe = document.createElement('span');
themeProbe.style.display = 'none';
let themeSwatch = null;

/** A CSS variable's colour as { r, g, b } bytes, whatever form it is written in (hex, color-mix and so on). */
function themeColour(name) {
    try {
        if (!themeProbe.isConnected) document.documentElement.appendChild(themeProbe);
        themeProbe.style.color = `var(${name})`;
        if (!themeSwatch) {
            const c = document.createElement('canvas');
            c.width = c.height = 1;
            themeSwatch = c.getContext('2d', { willReadFrequently: true });
        }
        themeSwatch.clearRect(0, 0, 1, 1);
        themeSwatch.fillStyle = getComputedStyle(themeProbe).color;
        themeSwatch.fillRect(0, 0, 1, 1);
        const [r, g, b] = themeSwatch.getImageData(0, 0, 1, 1).data;
        return { r, g, b };
    } catch {
        return { r: 7, g: 11, b: 14 };
    }
}

/** The colours the scene is drawn in: the page's own palette, so Day, Dusk, Night and Custom carry into the 3D view. */
function themePalette() {
    return {
        theme: document.documentElement.dataset.theme || 'dark',
        sky: themeColour('--sky'), panel: themeColour('--panel'), raised: themeColour('--panel-raised'),
        well: themeColour('--well'), line: themeColour('--line'), accent: themeColour('--green'),
        red: themeColour('--red'), text: themeColour('--moon')
    };
}

const rgba = (c, a = 1) => `rgba(${c.r}, ${c.g}, ${c.b}, ${a})`;
const hexOf = c => '#' + [c.r, c.g, c.b].map(v => v.toString(16).padStart(2, '0')).join('');
const color4 = c => window.BABYLON.Color4.FromHexString(hexOf(c) + 'ff');
const isDarkColour = c => (0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b) < 128;

// Night shows red only, Dusk amber only (rods barely respond to red, so the screen doesn't spoil dark adaptation): the
// whole picture, mount included, is turned into brightness in that one colour. sRGB so the maths is on the values as drawn.
const LOOK_FILTERS = {
    night: { id: 'gs-look-night', green: 0, blue: 0 },
    dusk: { id: 'gs-look-dusk', green: 0.69, blue: 0.4 }
};

function ensureLookFilters() {
    if (document.getElementById('gs-look-filters')) return;
    const gain = [0.4, 0.85, 0.2];
    const row = k => [...gain.map(v => (v * k).toFixed(3)), 0, 0].join(' ');
    const filters = Object.values(LOOK_FILTERS).map(f =>
        `<filter id="${f.id}" color-interpolation-filters="sRGB"><feColorMatrix type="matrix" values="` +
        `${row(1)} ${row(f.green)} ${row(f.blue)} 0 0 0 1 0"/></filter>`).join('');
    const holder = document.createElement('div');
    holder.id = 'gs-look-filters';
    holder.style.cssText = 'position:absolute;width:0;height:0;overflow:hidden';
    holder.setAttribute('aria-hidden', 'true');
    holder.innerHTML = `<svg width="0" height="0" focusable="false">${filters}</svg>`;
    document.body.appendChild(holder);
}

/** A small seeded random number generator (mulberry32): the same seed always gives the same numbers, 0 up to 1. */
function seededRandom(seed) {
    return () => {
        let t = seed += 0x6D2B79F5;
        t = Math.imul(t ^ t >>> 15, t | 1);
        t ^= t + Math.imul(t ^ t >>> 7, t | 61);
        return ((t ^ t >>> 14) >>> 0) / 4294967296;
    };
}

/**
 * A night sky on an equirectangular texture: the top half is black with randomly placed stars, the bottom half is the
 * ground (the theme's well colour), and the picture's middle row is the horizon. Stars are many faint ones, fewer medium and very
 * few bright ones (the brightness is a random number cubed), each with a slight warm or cool tint, dimmed towards the
 * horizon. They are spread evenly over the sky, not over the flat picture (which would crowd them at the zenith), and
 * each is stretched sideways by the picture's own stretch there, so it comes out round on the sky.
 */
function buildStarTexture(scene, palette) {
    const B = window.BABYLON;
    const maxSize = scene.getEngine().getCaps().maxTextureSize || STAR_TEXTURE_WIDTH;
    const width = Math.min(STAR_TEXTURE_WIDTH, maxSize), height = width / 2;
    const texture = new B.DynamicTexture('StarField', { width, height }, scene, false, B.Texture.BILINEAR_SAMPLINGMODE);
    const ctx = texture.getContext();
    ctx.fillStyle = '#000';
    ctx.fillRect(0, 0, width, height / 2);
    ctx.fillStyle = rgba(palette.well);
    ctx.fillRect(0, height / 2, width, height / 2);
    const glow = height * HORIZON_GLOW_DEGREES / 180;
    const haze = ctx.createLinearGradient(0, height / 2 - glow, 0, height / 2);
    haze.addColorStop(0, rgba(palette.line, 0));
    haze.addColorStop(1, rgba(palette.line, 0.85));
    ctx.fillStyle = haze;
    ctx.fillRect(0, height / 2 - glow, width, glow);

    const random = seededRandom(STAR_SEED);
    const pixel = width / STAR_TEXTURE_WIDTH;   // a star is as big on screen whatever the texture's size
    const tint = (level, warm) => Math.min(255, Math.round(level * (1 + warm))); // clamped, so a bright star doesn't wrap
    // Stars stop at the horizon: one on its edge isn't drawn over the ground.
    ctx.save();
    ctx.beginPath();
    ctx.rect(0, 0, width, height / 2);
    ctx.clip();
    for (let i = 0; i < STAR_COUNT; i++) {
        const x = random() * width;
        const elevation = Math.asin(random());   // evenly over the sky above the horizon
        const y = (0.5 - elevation / Math.PI) * height;
        const magnitude = Math.pow(random(), 3);
        const near = Math.min(elevation / rad(STAR_EXTINCTION_DEGREES), 1);
        const level = (90 + magnitude * 165) * (0.25 + 0.75 * near * near * (3 - 2 * near));   // dimmer near the horizon
        const warm = (random() * 2 - 1) * 0.18;
        const radius = (0.4 + magnitude * 1.6) * pixel;
        const stretch = 1 / Math.max(Math.cos(elevation), 0.15);
        ctx.fillStyle = `rgb(${tint(level, warm)},${tint(level, 0)},${tint(level, -warm)})`;
        // A star over the picture's left or right edge is drawn again on the other side, so it isn't cut in half.
        for (const shift of [0, -width, width]) {
            const cx = x + shift;
            if (cx + radius * stretch < 0 || cx - radius * stretch > width) continue;
            ctx.save();
            ctx.translate(cx, y);
            ctx.scale(stretch, 1);
            ctx.beginPath();
            ctx.arc(0, 0, radius, 0, Math.PI * 2);
            ctx.fill();
            ctx.restore();
        }
    }
    ctx.restore();
    texture.update();
    return texture;
}

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
        this.palette = themePalette();
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

        // The theme can change while the view is open (the header switch, the custom colour editor).
        this.onThemeChange = () => this.applyTheme();
        window.addEventListener('gsthemechange', this.onThemeChange);
        this.applyLook();
    }

    /** The scene's colours again, after a theme change: the sky, ground, grid, letters, compass and the Night / Dusk tint. */
    applyTheme() {
        if (this.scene.isDisposed) return;
        this.palette = themePalette();
        this.hemi.groundColor = window.BABYLON.Color3.FromHexString(hexOf(this.palette.raised));
        this.paintGrid(this.groundSolid);
        this.paintCardinals();
        this.setHemisphere(this.northern);
        const spec = this.backgroundSpec;
        const panorama = spec && !(typeof spec === 'object' && (spec.flat || spec.generated));
        if (panorama) this.scene.clearColor = color4(this.palette.sky);
        else this.setBackground(spec); // a plain sky, the star field (drawn in the theme's ground) or the user's picture
        this.applyLook();
        this.requestRender();
    }

    /** Night and Dusk tint the whole picture to one colour; the other themes draw it as it is. */
    applyLook() {
        const look = LOOK_FILTERS[this.palette.theme];
        if (look) ensureLookFilters();
        this.canvas.style.filter = look ? `url(#${look.id})` : '';
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

    /**
     * Applies a saved camera ({ alpha, beta (degrees), radius, targetX/Y/Z }), or the default: the default view's
     * angle and distance, looking at the top of the pier (centreOnMount) once a model is loaded.
     */
    setCamera(view) {
        const B = window.BABYLON;
        const v = view || DEFAULT_CAMERA;
        this.savedCamera = !!view;
        const target = view || !this.mountCentre
            ? new B.Vector3(v.targetX, v.targetY, v.targetZ)
            : this.mountCentre.clone();
        this.aimCamera(target, rad(v.alpha), rad(v.beta), v.radius);
    }

    /**
     * Points the camera at `target` from the given angles and distance. Target first: setTarget works alpha, beta
     * and radius out again from where the camera is now, so set after it they would be overwritten.
     */
    aimCamera(target, alpha, beta, radius) {
        this.lastAim = target.clone();
        this.camera.setTarget(target);
        this.camera.alpha = alpha;
        this.camera.beta = beta;
        this.camera.radius = radius;
        this.requestRender();
    }

    /**
     * The bottom of the OTA, where it sits on the saddle over the Dec axis, at the home position (the OTA along the
     * polar axis): without a saved camera, the camera looks at it (keeping its angle and distance), so the
     * telescope sits in the middle of the view whatever the model and its size. Worked out from the mount's
     * latitude, not the pose on screen, so the view stays put while the mount moves.
     *   assembled model: the saddle top (`otaBottom`, up the Dec axis from where it crosses the polar axis)
     *   .obj model: the lowest point of its tube (`otaBottom`, above GSServer's rotation centre)
     */
    centreOnMount(mount, otaBottom) {
        const B = window.BABYLON;
        // The polar axis rises by the latitude toward the pole (-Y); "up" the Dec axis at home leans back from it.
        const tilt = (mount?.mode ?? 2) === 0 ? 90 : Math.abs(mount?.latitude ?? 0);   // Alt-Az: the axis is vertical
        const lat = rad(tilt), sin = Math.sin(lat), cos = Math.cos(lat);
        if (this.assembly) this.assembly.south = (mount?.latitude ?? 0) < 0;
        let helix;
        if (this.assembly) {
            const a = this.assemblyAt, d = a.decAlongRa;
            helix = new B.Vector3(0, -d * cos + otaBottom * sin, a.top + a.hinge + d * sin + otaBottom * cos)
                .scale(ASSEMBLY_SCALE);
        } else {
            const yCentre = (mount?.mode ?? 2) === 2 ? -102 : 0, up = otaBottom - ROTATION_CENTRE_Z;
            helix = new B.Vector3(0, yCentre + up * sin, ROTATION_CENTRE_Z + up * cos);
        }
        this.root.computeWorldMatrix(true);
        const centre = B.Vector3.TransformCoordinates(helix, this.root.getWorldMatrix());
        this.mountCentre = centre;
        this.centreOtaBottom = otaBottom;
        // Only a view still looking where it was last aimed: one the user has panned, or has saved (setCamera or
        // getCamera), keeps its own target.
        const c = this.camera, moved = this.lastAim && B.Vector3.Distance(c.target, this.lastAim) > 1e-6;
        if (!this.savedCamera && !moved) this.aimCamera(centre.clone(), c.alpha, c.beta, c.radius);
    }

    /** The camera as it is now, to be saved: from then on, a model reload doesn't re-aim it. */
    getCamera() {
        this.savedCamera = true;
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


    /**
     * Holds the camera at least CAMERA_MIN_HEIGHT above the ground: the target is kept above it too (panning, a saved
     * view), and the tilt is limited to the angle at which the camera, at this distance from the target, reaches
     * the minimum height. Runs as the camera updates, before its view is worked out, so it never shows a frame below.
     */
    keepCameraAboveGround() {
        const B = window.BABYLON;
        const c = this.camera;
        // Keeping alpha, beta and radius (the last argument), so only the target moves.
        if (c.target.y < CAMERA_MIN_Y) c.setTarget(new B.Vector3(c.target.x, CAMERA_MIN_Y, c.target.z), false, false, true);
        // The camera's height is target.y + radius * cos(beta); this beta puts it at the minimum.
        const limit = Math.acos(Math.max(-1, Math.min(1, (CAMERA_MIN_Y - c.target.y) / c.radius)));
        c.upperBetaLimit = limit;
        if (c.beta > limit) c.beta = limit;
    }

    createScene() {
        const B = window.BABYLON;
        const scene = new B.Scene(this.engine);
        scene.useRightHandedSystem = true; // same handedness as Helix / WPF 3D
        scene.clearColor = color4(this.palette.sky);

        this.camera = new B.ArcRotateCamera('Camera', rad(DEFAULT_CAMERA.alpha), rad(DEFAULT_CAMERA.beta),
            DEFAULT_CAMERA.radius, new B.Vector3(DEFAULT_CAMERA.targetX, DEFAULT_CAMERA.targetY, DEFAULT_CAMERA.targetZ), scene);
        this.camera.attachControl(this.canvas, true);
        this.camera.lowerRadiusLimit = 5;
        this.camera.upperRadiusLimit = 200;
        // The wheel zooms by a share of the distance, so it feels the same near and far; a pinch zooms
        // so the model follows the fingers (both, unlike the default fixed steps, scale with distance).
        this.camera.wheelDeltaPercentage = 0.02;
        this.camera.useNaturalPinchZoom = true;
        // Never below the ground: after the controls have moved the camera, its target and tilt are held (see
        // keepCameraAboveGround), so it can't be orbited, panned or zoomed under the floor.
        this.camera.onAfterCheckInputsObservable.add(() => this.keepCameraAboveGround());

        // Lighting
        const hemi = this.hemi = new B.HemisphericLight('hemisphericLight', new B.Vector3(0, 1, 0), scene);
        hemi.intensity = 0.75;
        hemi.diffuse = B.Color3.FromHexString(this.secondaryColor);
        hemi.specular = new B.Color3(0.2, 0.2, 0.2);
        hemi.groundColor = B.Color3.FromHexString(hexOf(this.palette.raised));

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
        this.skybox.infiniteDistance = true; // stays centred on the camera, so the sky has no parallax and can't be reached
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
        this.createShadow(scene);
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
     * edge so it has no hard border. It is see-through, so the background shows between the lines (less so over the
     * star field: see setGroundFloor).
     */
    createGround(scene) {
        const B = window.BABYLON;

        // One texture tile = GRID_MAJOR_EVERY x GRID_MAJOR_EVERY cells, major lines on its edges.
        const px = 512, step = px / GRID_MAJOR_EVERY;
        const grid = new B.DynamicTexture('GridTexture', { width: px, height: px }, scene, true);
        const ctx = grid.getContext();
        const line = (pos, width, color) => {
            ctx.fillStyle = color;
            ctx.fillRect(pos - width / 2, 0, width, px);
            ctx.fillRect(0, pos - width / 2, px, width);
        };
        // Painted again when the floor changes (setGroundFloor).
        this.paintGrid = solid => {
            const p = this.palette;
            ctx.clearRect(0, 0, px, px);
            // A floor, so the mount stands on something.
            ctx.fillStyle = solid ? rgba(p.well, GROUND_FLOOR_SOLID_ALPHA) : rgba(p.panel, GROUND_FLOOR_FAINT_ALPHA);
            ctx.fillRect(0, 0, px, px);
            for (let i = 1; i < GRID_MAJOR_EVERY; i++) line(i * step, 2, rgba(p.accent, 0.28));
            // Major lines straddle the tile edges, so each tile draws half of them.
            line(0, 6, rgba(p.accent, 0.65));
            line(px, 6, rgba(p.accent, 0.65));
            grid.update();
        };
        this.groundSolid = false;
        this.paintGrid(false);
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

    /**
     * A soft shadow on the compass under the mount: a dark disc fading out at its edge, so the support looks as if
     * it stands on the ground rather than floats over it. Drawn once; costs one transparent quad.
     */
    createShadow(scene) {
        const B = window.BABYLON;
        const px = 256;
        const texture = new B.DynamicTexture('ShadowTexture', { width: px, height: px }, scene, false);
        const ctx = texture.getContext();
        const gradient = ctx.createRadialGradient(px / 2, px / 2, 0, px / 2, px / 2, px / 2);
        gradient.addColorStop(0, 'rgba(0, 0, 0, 0.55)');
        gradient.addColorStop(0.45, 'rgba(0, 0, 0, 0.35)');
        gradient.addColorStop(1, 'rgba(0, 0, 0, 0)');
        ctx.clearRect(0, 0, px, px);
        ctx.fillStyle = gradient;
        ctx.fillRect(0, 0, px, px);
        texture.update();
        texture.hasAlpha = true;
        const material = new B.StandardMaterial('Shadow', scene);
        material.diffuseTexture = texture;
        material.useAlphaFromDiffuseTexture = true;
        material.disableLighting = true;
        material.emissiveColor = new B.Color3(0, 0, 0);
        material.specularColor = new B.Color3(0, 0, 0);
        const shadow = B.MeshBuilder.CreateGround('Shadow', { width: 11, height: 11 }, scene);
        shadow.position.y = 0.02;   // just above the compass's top face
        shadow.material = material;
        shadow.isPickable = false;
        return shadow;
    }

    /** Over the star field the ground's floor is nearly solid, so it reads as ground; over a picture it is faint. */
    setGroundFloor(solid) {
        if (this.groundSolid === !!solid) return;
        this.groundSolid = !!solid;
        this.paintGrid(this.groundSolid);
        this.requestRender();
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
            this.paintCardinal(texture, c, px);
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
            return { ...c, plane, texture };
        });
    }

    /** One letter, in the theme's colours: north in its red, the others in its text colour, edged in the sky colour. */
    paintCardinal(texture, c, px = 256) {
        const ctx = texture.getContext();
        ctx.clearRect(0, 0, px, px);
        ctx.font = `600 ${Math.round(px * 0.8)}px Barlow, "Segoe UI", system-ui, sans-serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.lineWidth = px * 0.06;
        ctx.strokeStyle = rgba(this.palette.sky, 0.85); // an edge, so a letter reads over a background of either kind
        ctx.strokeText(c.letter, px / 2, px * 0.54);
        ctx.fillStyle = rgba(this.palette[c.role]);
        ctx.fillText(c.letter, px / 2, px * 0.54);
        texture.update();
    }

    paintCardinals() {
        for (const c of this.cardinals) this.paintCardinal(c.texture, c);
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
        // A mount built from parts (tripod and base, head) rather than one .obj: see loadAssembly.
        // A mount assembled from parts: see loadAssembly. The url is then its OTA's usual .obj model.
        if (mount?.assembly) return this.loadAssembly(url, mount);

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

        // Replace the previous model (or assembly), its materials and the pier.
        this.disposeAssembly();
        this.modelMeshes.forEach(m => m.dispose());
        this.modelMeshes = [];
        this.modelMaterials.forEach(m => m.dispose(false, true));   // with their textures (labels)
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
        // The OTA is the first object; its lowest point (the .obj's own coordinates) is where the camera looks.
        this.centreOnMount(mount, meshes[0].getBoundingInfo().boundingBox.minimum.z);
        return 'Model loaded successfully';
    }

    /**
     * A German equatorial mount assembled from parts (EXPERIMENTAL), chosen on the back of the card
     * (mount.assembly: { support, mount, ota }):
     *   support  a tripod or a pier, built here (ASSEMBLY_SUPPORTS)
     *   mount    a German equatorial mount's base and head, built here (ASSEMBLY_MOUNTS)
     *   ota      an .obj model's tube from `url`'s folder (ASSEMBLY_OTAS), or one built here (ASSEMBLY_BUILT_OTAS)
     *   otaSize  its size in percent of the mount's own OTA size (otaScale); 100 if not given
     * The head hangs from three joints, driven by the same pose numbers as an .obj model, so it moves the same way:
     *
     *   latitude joint  tilts the RA body about east-west by the pose's z (GSServer's ZAxisOffset, -latitude)
     *     RA joint      turns the head (Dec housing, counterweights) about the polar axis by y (YAxisOffset)
     *       Dec joint   turns the puck (saddle and OTA) about the Dec axis by x (XAxisOffset)
     *
     * Everything is in millimetres, Z up, the pole toward -Y: the scene's Helix frame (north along -Y with the
     * north compass), so it needs only scaling. The joints' frames are drawn at the home position: the polar axis
     * level along -Y, the Dec axis standing along +Z (saddle up, counterweights down).
     */
    async loadAssembly(url, mount) {
        const B = window.BABYLON;
        const load = ++this.loadCount;
        const folder = url.substring(0, url.lastIndexOf('/') + 1);
        if (B.OBJFileLoader) B.OBJFileLoader.SKIP_MATERIALS = true;

        const parts = mount?.assembly ?? {};
        const support = ASSEMBLY_SUPPORTS[parts.support] ?? ASSEMBLY_SUPPORTS.mediumtripod;
        const onPier = !!support.pier;
        const build = ASSEMBLY_MOUNTS[parts.mount] ?? ASSEMBLY_MOUNTS.eq6rpro;
        const builtOta = ASSEMBLY_BUILT_OTAS[parts.ota];
        const otaName = builtOta ? null : ASSEMBLY_OTAS.includes(parts.ota) ? parts.ota : 'Refractor';

        // The only download: an .obj OTA's model. A failed one is an error, unless a newer load has replaced this one.
        let ota = null;
        try {
            if (otaName) ota = await B.SceneLoader.ImportMeshAsync('', folder, `${otaName}.obj`, this.scene, null, '.obj');
        } catch (error) {
            return load !== this.loadCount ? 'Superseded' : `Error: ${error?.message ?? error}`;
        }
        const results = [ota].filter(Boolean);
        if (load !== this.loadCount || this.scene.isDisposed) {
            results.forEach(r => {
                r.meshes.forEach(m => m.dispose());
                (r.transformNodes || []).forEach(n => n.dispose());
            });
            return 'Superseded';
        }

        // Replace the previous model (or assembly), its materials and the pier (the tripod stands in for it).
        this.disposeAssembly();
        this.modelMeshes.forEach(m => m.dispose());
        this.modelMeshes = [];
        this.modelMaterials.forEach(m => m.dispose(false, true));   // with their textures (labels)
        this.pierMeshes.forEach(m => m.dispose());
        this.pierMeshes = [];

        // `power`: how tight the highlight is; metal gets a bright, tight one, paint a soft one.
        const mat = (name, hex, specular = 0.15, power = 24) => {
            const m = new B.StandardMaterial(name, this.scene);
            m.diffuseColor = B.Color3.FromHexString(hex);
            m.specularColor = new B.Color3(specular, specular, specular);
            m.specularPower = power;
            return m;
        };
        const materials = {
            tripod: mat('Tripod', '#34474f', 0.1),
            white: mat('MountWhite', '#ecece7', 0.3, 40), // the EQ6-R Pro's castings, weights and saddle
            black: mat('MountBlack', '#1b1f21', 0.25),   // star knobs and levers
            steel: mat('MountSteel', '#b3b9bd', 1, 90),  // shaft, rod, hinge caps: polished
            green: mat('MountGreen', '#2fbf55', 0.3),    // the RA and Dec setting circles
            grey: mat('MountGrey', '#5f676b', 0.3),      // the control panel's caps
            dark: mat('MountDark', '#2a2e31', 0.3),      // black castings (EQ8-R, CEM70)
            red: mat('MountRed', '#cf2a22', 0.5, 60),    // anodised red (AM5, CEM40)
            brass: mat('MountBrass', '#b8913f', 0.8, 70), // brass fittings (CEM40's latitude screw)
            glass: mat('MountGlass', '#13232d', 0.9),    // a built OTA's glass and open aperture
            ota: mat('OTA', this.primaryColor, 0.3)
        };
        this.otaMaterial = materials.ota;
        this.modelMaterials = Object.values(materials);

        // Nodes: holder (millimetres) > tripod and base, and latitude > RA > Dec joints for the head.
        const holder = new B.TransformNode('Assembly', this.scene);
        holder.parent = this.root;
        holder.scaling = new B.Vector3(ASSEMBLY_SCALE, ASSEMBLY_SCALE, ASSEMBLY_SCALE);
        const latitude = new B.TransformNode('LatitudeJoint', this.scene);
        latitude.parent = holder;
        // Where the mount stands: the support's top. The builders get it with the mount's measurements.
        const top = onPier ? ASSEMBLY_COLUMN_TOP : ASSEMBLY_COLUMN_TOP * support.tripod;
        const at = { ...build, top, mode: mount?.mode ?? 2 };
        this.assemblyAt = at;
        latitude.position = new B.Vector3(0, 0, top + build.hinge);
        const ra = new B.TransformNode('RaJoint', this.scene);
        ra.parent = latitude;
        ra.position = new B.Vector3(0, -build.decAlongRa, 0);
        const dec = new B.TransformNode('DecJoint', this.scene);
        dec.parent = ra;
        for (const n of [latitude, ra, dec]) n.rotationQuaternion = B.Quaternion.Identity();
        this.assembly = { holder, latitude, ra, dec };

        const keep = mesh => {
            mesh.isPickable = false;
            this.modelMeshes.push(mesh);
        };

        // The support: a pier, or the tripod at its scale.
        const shapes = new AssemblyShapes(B, this.scene, keep);
        shapes.otaSize = Number.isFinite(parts.otaSize) && parts.otaSize > 0 ? parts.otaSize / 100 : 1;
        if (onPier) {
            buildPier(shapes, holder, materials, top, support.pier);
        } else {
            const tripodNode = new B.TransformNode('Tripod', this.scene);
            tripodNode.parent = holder;
            tripodNode.scaling = new B.Vector3(support.tripod, support.tripod, support.tripod);
            buildTripod(shapes, tripodNode, materials, support.thick ?? 1);
        }

        // The labels' materials and textures are the model's too, so they go with it.
        this.modelMaterials.push(...shapes.labelMaterials);
        shapes.labelMaterials = this.modelMaterials;
        build.base(shapes, holder, materials, at);

        // The mount's own OTA size, times the size chosen on the back of the card (percent; 100 by default).
        const size = Number.isFinite(parts.otaSize) && parts.otaSize > 0 ? parts.otaSize / 100 : 1;
        const o = ASSEMBLY_OTA, scale = (build.otaScale ?? o.scale) * size;
        const otaNode = new B.TransformNode('Ota', this.scene);
        otaNode.parent = dec;

        // A built OTA: standing on the saddle, at the size an .obj OTA has at this scale.
        if (builtOta) {
            const node = new B.TransformNode('BuiltOta', this.scene);
            node.parent = otaNode;
            node.position = new B.Vector3(0, 0, build.saddleTop);
            node.scaling = new B.Vector3(scale / o.scale, scale / o.scale, scale / o.scale);
            builtOta(shapes, node, materials);
        }

        // An .obj OTA: its tube (the first object), its lowest point on the saddle, its own place along the Dec axis.
        const tube = ota ? ota.meshes.filter(m => m.getTotalVertices() > 0) : [];
        if (tube.length > 0) {
            const bottom = tube[0].getBoundingInfo().boundingBox.minimum.z;
            tube[0].parent = otaNode;
            tube[0].scaling = new B.Vector3(scale, scale, scale);
            tube[0].position = new B.Vector3(0, -o.decAxisY * scale, build.saddleTop - bottom * scale);
            tube[0].material = materials.ota;
            keep(tube[0]);
        }
        tube.slice(1).forEach(m => m.dispose());

        // The OTA's extent on the Dec joint (millimetres). A fork centres it on the Dec axis, and sizes its arms to it.
        const extent = () => {
            dec.computeWorldMatrix(true);
            const { min, max } = otaNode.getHierarchyBoundingVectors(true);
            const toDec = dec.getWorldMatrix().clone().invert();
            const lo = new B.Vector3(Infinity, Infinity, Infinity), hi = lo.scale(-1);
            for (const cx of [min.x, max.x]) for (const cy of [min.y, max.y]) for (const cz of [min.z, max.z]) {
                const c = B.Vector3.TransformCoordinates(new B.Vector3(cx, cy, cz), toDec);
                lo.minimizeInPlace(c);
                hi.maximizeInPlace(c);
            }
            return { lo, hi };
        };
        if (build.fork) {
            const { lo, hi } = extent();
            otaNode.position = lo.add(hi).scale(-0.5);
            at.otaHalfWidth = Math.max(hi.x - lo.x, hi.z - lo.z) / 2;
        }
        build.head(shapes, { latitude, ra, dec }, materials, at);

        // The loaders' own root nodes are now empty.
        for (const r of results) {
            r.meshes.filter(m => m.getTotalVertices() === 0).forEach(m => m.dispose());
            (r.transformNodes || []).forEach(n => n.dispose());
        }

        this.assembly.mode = at.mode;
        this.applyPose();
        // A fork's OTA is centred on its Dec axis; others stand on the saddle.
        this.centreOnMount(mount, build.fork ? 0 : build.saddleTop);
        return 'Model loaded successfully';
    }
    /** Removes the assembled mount (loadAssembly), if there is one. */
    disposeAssembly() {
        if (!this.assembly) return;
        this.assembly.holder.dispose(false, true);
        this.assembly = null;
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

        // An assembled mount: the same three angles, each turning its own joint (see loadAssembly).
        if (this.assembly) {
            const turn = (node, matrix) => { node.rotationQuaternion = B.Quaternion.FromRotationMatrix(matrix); };
            // The joints take the same pose numbers as an .obj model. German equatorial: as they are. Alt-Az: the
            // altitude joint is a quarter turn on (its zero is the OTA level, the pose's straight up). A polar fork in
            // the southern hemisphere: both turn the other way, its polar axis pointing at the other pole.
            let ra = y, dec = x;
            if (this.assembly.mode === 0) dec = x + 90;
            else if (this.assembly.mode === 1 && this.assembly.south) { ra = -y; dec = -x; }
            turn(this.assembly.latitude, B.Matrix.RotationX(rad(z)));
            turn(this.assembly.ra, B.Matrix.RotationY(rad(ra)));
            turn(this.assembly.dec, B.Matrix.RotationZ(rad(dec)));
            return;
        }

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
     * Background image URL (relative to the page), { url, flat } for the user's own picture, { generated: 'stars' } for
     * the star field, or null for a plain dark sky. The current
     * background stays until the new image has downloaded, so switching doesn't flash black.
     */
    setBackground(url) {
        const B = window.BABYLON;
        const request = ++this.backgroundRequest;
        this.backgroundSpec = url; // kept, so a theme change can draw it again
        const material = this.skybox.material;
        // The user's own picture ({ url, flat }): shown flat behind the scene, dimmed a little like the panoramas,
        // rather than wrapped round the sky, which would stretch an ordinary photo.
        const flat = url && typeof url === 'object' && url.flat;
        const generated = url && typeof url === 'object' ? url.generated : null;
        if (url && typeof url === 'object') url = url.url;
        this.canvas.style.backgroundImage = '';
        this.scene.clearColor = flat ? B.Color4.FromHexString('#00000000') : color4(this.palette.sky);
        if (flat) {
            const href = new URL(url, document.baseURI).href;
            this.canvas.style.background =
                `linear-gradient(${rgba(this.palette.sky, 0.45)}, ${rgba(this.palette.sky, 0.45)}), url("${href}") center / cover no-repeat, ${rgba(this.palette.sky)}`;
            const old = material.reflectionTexture;
            material.reflectionTexture = null;
            this.skybox.setEnabled(false);
            this.setGroundFloor(false);
            if (old) old.dispose();
            this.requestRender();
            return;
        }
        this.canvas.style.background = '';
        const swap = (texture, solidGround = false) => {
            const old = material.reflectionTexture;
            material.reflectionTexture = texture;
            this.setGroundFloor(solidGround);
            this.skybox.setEnabled(!!texture);
            if (old && old !== texture) old.dispose();
            this.requestRender();
        };

        if (generated === 'stars') {
            const stars = buildStarTexture(this.scene, this.palette);
            stars.coordinatesMode = B.Texture.FIXED_EQUIRECTANGULAR_MODE;
            swap(stars, true);
            return;
        }

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
            // The compass image is dark markings on a transparent background. Over a dark panel colour draw it inverted
            // (light markings), so it reads as a dark disc; over a light one (Day) draw it as it is.
            const size = 1024;
            const texture = new B.DynamicTexture('CompassTexture', { width: size, height: size }, this.scene, true);
            const ctx = texture.getContext();
            ctx.fillStyle = rgba(this.palette.panel);
            ctx.fillRect(0, 0, size, size);
            // The right-handed scene shows the cap texture mirrored; draw it mirrored so the
            // letters read correctly and E / W are on the right sides.
            ctx.save();
            ctx.translate(size, 0);
            ctx.scale(-1, 1);
            ctx.filter = isDarkColour(this.palette.panel) ? 'invert(1)' : 'none';
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
        window.removeEventListener('gsthemechange', this.onThemeChange);
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

/**
 * Simple shapes for the built mount, placed in a joint's frame (millimetres): boxes by their extents, cylinders by
 * their axis (X, Y or Z), diameter and the span along it. Only the builders the trimmed Babylon bundle has.
 */
class AssemblyShapes {
    constructor(B, scene, keep) {
        this.B = B;
        this.scene = scene;
        this.keep = keep;
        this.count = 0;
        this.labelMaterials = [];   // the labels' own materials (with their textures), disposed with the model
        this.otaSize = 1;           // the OTA's size against the mount's own: the counterweights follow it
    }

    add(mesh, parent, material) {
        mesh.parent = parent;
        mesh.material = material;
        this.keep(mesh);
        return mesh;
    }

    /** A box from x0..x1, y0..y1, z0..z1. */
    box(parent, material, [x0, x1], [y0, y1], [z0, z1]) {
        const m = this.B.MeshBuilder.CreateBox(`Eq6r${this.count++}`,
            { width: Math.abs(x1 - x0), height: Math.abs(y1 - y0), depth: Math.abs(z1 - z0) }, this.scene);
        m.position = new this.B.Vector3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2);
        return this.add(m, parent, material);
    }

    /**
     * A cylinder along `axis` ('x', 'y' or 'z') from `from` to `to`, centred on (a, b) across it (the other two
     * coordinates in x, y, z order). `top` narrows it to a cone toward `to`.
     */
    cylinder(parent, material, axis, diameter, from, to, [a, b] = [0, 0], { top = diameter, tessellation = 40 } = {}) {
        const B = this.B;
        const m = B.MeshBuilder.CreateCylinder(`Eq6r${this.count++}`,
            { height: Math.abs(to - from), diameterBottom: diameter, diameterTop: top, tessellation }, this.scene);
        const mid = (from + to) / 2, up = to >= from ? 1 : -1;
        // A Babylon cylinder stands along its own Y, top at +Y: turn that onto the axis, top toward `to`.
        if (axis === 'x') {
            m.position = new B.Vector3(mid, a, b);
            m.rotation.z = -up * Math.PI / 2;
        } else if (axis === 'y') {
            m.position = new B.Vector3(a, mid, b);
            if (up < 0) m.rotation.x = Math.PI;
        } else {
            m.position = new B.Vector3(a, b, mid);
            m.rotation.x = up * Math.PI / 2;
        }
        return this.add(m, parent, material);
    }

    /** A rod (a cylinder) from point `p0` to point `p1`, each [x, y, z], at any angle. */
    rod(parent, material, diameter, p0, p1, { top = diameter, tessellation = 24 } = {}) {
        const B = this.B;
        const a = new B.Vector3(...p0), b = new B.Vector3(...p1), d = b.subtract(a), length = d.length();
        const m = B.MeshBuilder.CreateCylinder(`Eq6r${this.count++}`,
            { height: length, diameterBottom: diameter, diameterTop: top, tessellation }, this.scene);
        m.position = a.add(b).scale(0.5);
        // Turn the cylinder's own axis (+Y) onto the rod's direction.
        const dir = d.scale(1 / length), axis = B.Vector3.Cross(new B.Vector3(0, 1, 0), dir), s = axis.length();
        m.rotationQuaternion = s < 1e-9
            ? (dir.y > 0 ? B.Quaternion.Identity() : B.Quaternion.RotationAxis(new B.Vector3(1, 0, 0), Math.PI))
            : B.Quaternion.RotationAxis(axis.scale(1 / s), Math.acos(Math.max(-1, Math.min(1, dir.y))));
        return this.add(m, parent, material);
    }

    /**
     * A box with rounded edges: the four edges along `axis` ('x', 'y' or 'z') rounded to radius `r`, so a casting
     * reads as a casting rather than a crate. Extents as for box.
     */
    rbox(parent, material, X, Y, Z, r, axis = 'z') {
        const R = { x: X, y: Y, z: Z };
        const [a, b] = { x: ['y', 'z'], y: ['x', 'z'], z: ['x', 'y'] }[axis];   // the rounded cross-section
        const lo = k => Math.min(...R[k]), hi = k => Math.max(...R[k]);
        const box = (ra, rb) => {
            const q = { ...R, [a]: ra, [b]: rb };
            this.box(parent, material, q.x, q.y, q.z);
        };
        box([lo(a) + r, hi(a) - r], [lo(b), hi(b)]);
        box([lo(a), hi(a)], [lo(b) + r, hi(b) - r]);
        for (const ca of [lo(a) + r, hi(a) - r]) {
            for (const cb of [lo(b) + r, hi(b) - r]) {
                this.cylinder(parent, material, axis, 2 * r, lo(axis), hi(axis), [ca, cb], { tessellation: 16 });
            }
        }
    }

    /**
     * A dovetail saddle along the polar axis (Y), standing on the Dec axis (Z): a rounded body `w` either side of
     * the axis and `len` either way along it, from `base` to `mid`, and two jaws from `mid` to `top`: a fixed one on
     * the west edge and a moving one `inset` in from the east edge, each `jaw` wide.
     */
    saddle(parent, material, { w, len, base, mid, top, jaw = 16, inset = 6, r = 6 }) {
        this.rbox(parent, material, [-w, w], [-len, len], [base, mid], r, 'y');
        this.box(parent, material, [-w, -w + jaw], [-len, len], [mid, top]);
        this.box(parent, material, [w - inset - jaw, w - inset], [-len, len], [mid, top]);
    }

    /** Star knobs along `axis` from `from` to `to`, one at each of `places` (each across the axis, as for knob). */
    knobs(parent, m, axis, from, to, places, knob = m.black) {
        for (const across of places) this.knob(parent, m, axis, from, to, across, knob);
    }

    /**
     * A printed label (a maker's name or a model): text on a thin plate at `at`, facing along `normal` ('x', '-x',
     * 'y', '-y', 'z' or '-z'), its text upright along `up`, `width` by `height` millimetres. Lines split on '\n'; a
     * line starting '~' is drawn in the accent colour. Seen from the front only.
     */
    label(parent, text, { at, normal, up, width, height, color = '#f2f2ee', accent = '#2fbf55', weight = 700 }) {
        const B = this.B;
        const axis = d => {
            const v = { x: [1, 0, 0], y: [0, 1, 0], z: [0, 0, 1] }[d.replace('-', '')].map(c => d.startsWith('-') ? -c : c);
            return new B.Vector3(...v);
        };
        const px = 512, ph = Math.max(16, Math.round(px * height / width));
        const texture = new B.DynamicTexture(`Label${this.count}`, { width: px, height: ph }, this.scene, true);
        const ctx = texture.getContext();
        ctx.clearRect(0, 0, px, ph);
        // The plate maps the texture's rows upside down: draw the text mirrored top to bottom to cancel that.
        ctx.save();
        ctx.translate(0, ph);
        ctx.scale(1, -1);
        const lines = text.split('\n');
        const size = Math.min(ph / lines.length * 0.78, px / Math.max(...lines.map(l => l.length)) * 1.7);
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        lines.forEach((line, i) => {
            const accented = line.startsWith('~');
            ctx.font = `${weight} ${Math.round(size)}px Barlow, "Segoe UI", system-ui, sans-serif`;
            ctx.fillStyle = accented ? accent : color;
            ctx.fillText(accented ? line.slice(1) : line, px / 2, (i + 0.5) * ph / lines.length);
        });
        ctx.restore();
        texture.update();
        texture.hasAlpha = true;
        const material = new B.StandardMaterial(`Label${this.count}`, this.scene);
        material.diffuseTexture = texture;
        material.useAlphaFromDiffuseTexture = true;
        material.emissiveColor = new B.Color3(0.35, 0.35, 0.35);
        material.specularColor = new B.Color3(0, 0, 0);
        this.labelMaterials.push(material);
        // A ground plate lies in its X-Z plane facing +Y: turn it to face `normal` with the text's top toward `up`.
        const plate = B.MeshBuilder.CreateGround(`Label${this.count++}`, { width, height }, this.scene);
        const n = axis(normal), u = axis(up), right = B.Vector3.Cross(u, n);
        const turn = B.Matrix.FromValues(
            right.x, right.y, right.z, 0,
            n.x, n.y, n.z, 0,
            -u.x, -u.y, -u.z, 0,
            0, 0, 0, 1);
        plate.rotationQuaternion = B.Quaternion.FromRotationMatrix(turn);
        plate.position = new B.Vector3(...at);
        return this.add(plate, parent, material);
    }

    /** A star knob along `axis`: a steel stem from `from`, the knob (black unless `knob` says) at `to`. */
    knob(parent, m, axis, from, to, across, knob = m.black) {
        const toward = to >= from ? 1 : -1;
        this.cylinder(parent, m.steel, axis, 10, from, to - toward * 12, across, { tessellation: 12 });
        this.cylinder(parent, knob, axis, 34, to - toward * 12, to, across, { tessellation: 5 });
        this.cylinder(parent, knob, axis, 18, to, to + toward * 4, across, { tessellation: 12 });
    }
}

/**
 * The full-size tripod (holder frame, millimetres, Z up; scaled for the small and large tripods): a hub on three
 * splayed legs (an upper and a lower section each, a hinge at the top and a foot), a spreader with an accessory
 * tray between the legs, and a centre column up to ASSEMBLY_COLUMN_TOP, where a mount's base stands.
 */
function buildTripod(s, node, m, thick = 1) {
    const at = (r, az, z) => [r * Math.cos(az * Math.PI / 180), r * Math.sin(az * Math.PI / 180), z];
    const t = thick, core = Math.min(thick, 1.15);     // the centre parts grow less, to keep the weights clear
    s.cylinder(node, m.tripod, 'z', 156 * core, 1008, 1072);                        // hub
    s.cylinder(node, m.tripod, 'z', 124 * core, 1071, 1089);                        // mount plate
    s.cylinder(node, m.tripod, 'z', 90 * core, 1072, ASSEMBLY_COLUMN_TOP);          // centre column
    s.cylinder(node, m.tripod, 'z', 96 * t, 625, 675);                              // spreader collar
    s.cylinder(node, m.tripod, 'z', 380, 619, 631, [0, 0], { tessellation: 48 });  // accessory tray
    for (const az of [90, 212, 328]) {                                              // one leg south, two north
        s.rod(node, m.tripod, 64 * t, at(36, az, 1060), at(62, az, 984));           // hinge
        s.rod(node, m.tripod, 46 * t, at(30, az, 1030), at(400, az, 400));          // upper section
        s.rod(node, m.tripod, 28 * t, at(350, az, 480), at(575, az, 60));           // lower section
        const [x, y] = at(572, az, 0);
        s.cylinder(node, m.tripod, 'z', 76 * t, 11, 49, [x, y]);                    // foot
        s.rod(node, m.tripod, 30 * t, at(48, az, 650), at(320, az, 650));           // spreader arm
    }
}

/**
 * A pier for an assembled model (holder frame, millimetres, Z up), `diameter` across its whole height: a foot plate
 * on the ground, a flared foot, the pier tube up to a top plate of the same width, and the steel mount adapter at
 * `top`. On the large (12-inch) pier the weights of some small, low mounts (CEM40, CQ350 Pro, EQM-35 Pro) reach it,
 * as they would on a real one without an extension.
 */
function buildPier(s, holder, m, top, diameter) {
    const d = diameter;
    s.cylinder(holder, m.tripod, 'z', Math.max(460, d * 1.9), 0, 24, [0, 0], { tessellation: 6 });   // foot plate
    s.cylinder(holder, m.tripod, 'z', d * 1.3, 24, 70, [0, 0], { top: d });         // flared foot
    s.cylinder(holder, m.tripod, 'z', d, 70, top - 40);                             // pier tube
    s.cylinder(holder, m.tripod, 'z', d, top - 40, top - 22);                       // top plate
    s.cylinder(holder, m.tripod, 'z', d + 4, top - 30, top - 26);                   // the plate's seam
    for (const a of [30, 150, 270]) {                                               // levelling bolts in the plate
        const r = d / 2 - 14, x = r * Math.cos(a * Math.PI / 180), y = r * Math.sin(a * Math.PI / 180);
        s.cylinder(holder, m.steel, 'z', 14, top - 22, top - 10, [x, y], { tessellation: 12 });
    }
    s.cylinder(holder, m.steel, 'z', 150, top - 22, top);                           // mount adapter
}

/**
 * The EQ6-R Pro's mount base, fixed on the tripod's centre column (holder frame, millimetres, Z up, pole -Y):
 * the white azimuth drum, the central latitude tower with a silver hinge cap each side, the latitude rod and
 * L-lever on the south side, the latitude lock knob on the north side, and the azimuth adjuster knobs.
 */
function buildEq6rBase(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    // Azimuth drum on the column.
    s.cylinder(holder, m.white, 'z', 150, z0, z0 + 44);
    s.cylinder(holder, m.white, 'z', 136, z0 + 44, z0 + 56);
    // Latitude tower: a white upright under the RA body, rounded over the hinge, a silver cap on each side.
    s.box(holder, m.white, [-44, 44], [-58, 58], [z0 + 56, hinge]);
    s.cylinder(holder, m.white, 'x', 116, -44, 44, [0, hinge]);
    for (const side of [-1, 1]) {
        s.cylinder(holder, m.white, 'x', 70, side * 44, side * 48, [0, hinge]);
        s.cylinder(holder, m.steel, 'x', 46, side * 48, side * 54, [0, hinge], { top: 34 });
    }
    // Latitude rod on the south side, with its black L-lever; the lock knob on the north side.
    s.box(holder, m.white, [-20, 20], [58, 80], [z0 + 60, z0 + 92]);
    s.cylinder(holder, m.steel, 'y', 12, 80, 190, [0, z0 + 76]);
    s.cylinder(holder, m.black, 'y', 22, 190, 204, [0, z0 + 76], { tessellation: 16 });
    s.box(holder, m.black, [-6, 6], [198, 210], [z0 + 20, z0 + 82]);
    s.knob(holder, m, 'y', -58, -92, [0, hinge - 40]);
    // Azimuth adjusters: a knob out each side at the tower's foot.
    for (const side of [-1, 1]) s.knob(holder, m, 'x', side * 44, side * 96, [0, z0 + 66]);
}

/**
 * The EQ6-R Pro's head on the three joints (each joint's frame, millimetres; home position: the polar axis level
 * along -Y toward the pole, the Dec axis along +Z):
 *   latitude: the RA body (tilts, doesn't turn in RA): housing, ribbed polar scope cover, carry handle on top,
 *             RA clutch lever, the green RA setting circle
 *   ra:       the head: one rounded white block with the control panel on its west face, a nose underneath
 *             with the counterweight shaft and two white weights, the green Dec setting circle
 *   dec:      the puck: a short white body, the Dec clutch lever and a white saddle with star knobs
 *             (the OTA is added after)
 */
function buildEq6rHead(s, j, m, mount) {
    const D = mount.decAlongRa, saddleTop = mount.saddleTop;
    const junction = -(D - 92);   // where the RA body meets the head, in the latitude joint's frame

    // RA body (latitude joint; the hinge is at the origin, on the polar axis).
    s.cylinder(j.latitude, m.white, 'y', 132, 150, junction);             // housing
    s.cylinder(j.latitude, m.white, 'y', 124, 150, 164);                  // step to the cover
    for (let y = 164; y < 212; y += 8) {                                  // ribbed polar scope cover
        s.cylinder(j.latitude, m.white, 'y', 118, y, y + 5, [0, 0], { tessellation: 32 });
        s.cylinder(j.latitude, m.white, 'y', 110, y + 5, y + 8, [0, 0], { tessellation: 32 });
    }
    s.cylinder(j.latitude, m.white, 'y', 110, 212, 226, [0, 0], { top: 70 });
    s.cylinder(j.latitude, m.green, 'y', 140, junction + 14, junction);   // green RA setting circle
    // Carry handle on top: two posts and a grip along the polar axis.
    for (const y of [20, 112]) s.box(j.latitude, m.white, [-12, 12], [y - 9, y + 9], [60, 104]);
    s.box(j.latitude, m.white, [-13, 13], [6, 126], [100, 118]);
    // RA clutch lever on the east side.
    s.cylinder(j.latitude, m.black, 'x', 20, 66, 80, [-50, 0], { tessellation: 16 });
    s.box(j.latitude, m.black, [76, 86], [-56, -44], [0, 58]);

    // Head (RA joint; origin where the Dec axis crosses the polar axis): one big block, its edges rounded.
    s.rbox(j.ra, m.white, [-94, 94], [-102, 104], [-96, 96], 16);
    s.rbox(j.ra, m.white, [-70, 70], [-78, 80], [96, 112], 12);           // its top, stepped in
    s.rbox(j.ra, m.white, [-70, 70], [-78, 80], [-112, -96], 12);         // and bottom
    s.cylinder(j.ra, m.white, 'y', 138, 92, 100);                         // collar meeting the RA body
    // Control panel on the west face: a raised panel, power switch, ports and two grey caps.
    s.rbox(j.ra, m.white, [-98, -94], [-70, 72], [-74, 84], 8, 'x');
    s.box(j.ra, m.black, [-102, -98], [-30, -16], [44, 62]);
    for (const [y, z, d] of [[-46, -10, 14], [-24, -10, 14], [-2, -10, 12], [24, 20, 18]]) {
        s.cylinder(j.ra, m.black, 'x', d, -98, -102, [y, z], { tessellation: 16 });
    }
    for (const [y, z] of [[-60, -86], [62, 92]]) s.cylinder(j.ra, m.grey, 'x', 26, -90, -100, [y, z], { tessellation: 20 });
    // Nose underneath, the counterweight shaft's lock knob, the shaft and its end stop.
    s.cylinder(j.ra, m.white, 'z', 96, -112, -150);
    s.cylinder(j.ra, m.white, 'z', 96, -150, -172, [0, 0], { top: 56 });
    s.knob(j.ra, m, 'x', 40, 80, [0, -140]);
    s.cylinder(j.ra, m.steel, 'z', 20, -172, -560);
    s.cylinder(j.ra, m.steel, 'z', 34, -560, -574);
    // Two white weights with black lock knobs.
    for (const z of [-340, -420]) {
        s.cylinder(j.ra, m.white, 'z', 124, z + 30, z - 30, [0, 0], { tessellation: 48 });
        s.knob(j.ra, m, 'x', 62, 92, [0, z]);
    }
    s.cylinder(j.ra, m.green, 'z', 120, 112, 124);                        // green Dec setting circle
    s.label(j.ra, 'Sky-Watcher\nEQ6-R PRO', { at: [94.5, 0, 24], normal: 'x', up: 'z', width: 150, height: 56, color: '#25292b' });

    // Puck (Dec joint): a short white body, the Dec clutch lever, and the saddle along the polar axis.
    s.cylinder(j.dec, m.white, 'z', 112, 124, 168);
    s.cylinder(j.dec, m.black, 'x', 20, 56, 70, [0, 146], { tessellation: 16 });
    s.box(j.dec, m.black, [66, 76], [-6, 6], [146, 200]);
    s.saddle(j.dec, m.white, { w: 58, len: 104, base: 168, mid: 206, top: saddleTop, jaw: 18 });
    s.knobs(j.dec, m, 'x', 52, 92, [[-52, 214], [52, 214]]);           // clamp knobs, east side
    s.knob(j.dec, m, 'x', -58, -92, [70, 190]);                           // and one on the west side
}

/**
 * Counterweights below the head (RA joint's frame): a shaft from `from` down to `to`, an end stop, and weights
 * centred at `at`, each with a lock knob. The shaft runs down the Dec axis, or at `y` along the polar axis from it
 * (negative: toward the pole), as a centre-balanced mount's does to clear its pier.
 */
function buildWeights(s, ra, m, { from, to, shaft, stop, weight, thick, at, material, knob = m.black, y = 0 }) {
    s.cylinder(ra, m.steel, 'z', shaft, from, to, [0, y]);
    s.cylinder(ra, m.steel, 'z', stop, to, to - 14, [0, y]);
    // The weights slide out along the shaft for a bigger OTA and in for a smaller one (the OTA size on the card),
    // as they would to balance it, staying on the shaft. A bar beside the pier (y off the Dec axis) only slides
    // them out: moving in would bring them against the pier.
    const mean = at.reduce((a, b) => a + b, 0) / at.length, reach = from - mean;
    let shift = reach * 0.45 * ((s.otaSize ?? 1) - 1);
    const out = Math.min(...at) - thick / 2 - (to + 2);
    const inward = y !== 0 ? 0 : (from - 4) - (Math.max(...at) + thick / 2);
    shift = Math.max(-inward, Math.min(out, shift));
    for (const z of at.map(z => z - shift)) {
        s.cylinder(ra, material, 'z', weight, z + thick / 2, z - thick / 2, [0, y], { tessellation: 48 });
        s.knob(ra, m, 'x', weight / 2 - 4, weight / 2 + 30, [y, z], knob);
    }
}

/**
 * Sky-Watcher HEQ5 Pro: the EQ6-R's smaller sibling. White; a round black badge on the latitude tower, silver
 * latitude T-bolts front and back, green bands on the RA housing and the head, a narrow Vixen saddle, two white
 * weights with black T knobs.
 */
function buildHeq5Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.white, 'z', 124, z0, z0 + 34);
    s.box(holder, m.white, [-34, 34], [-44, 44], [z0 + 34, hinge]);
    s.cylinder(holder, m.white, 'x', 92, -34, 34, [0, hinge]);
    for (const side of [-1, 1]) {
        s.cylinder(holder, m.black, 'x', 64, side * 34, side * 38, [0, hinge]);          // badge
        s.label(holder, 'Sky-Watcher\nHEQ5 PRO', { at: [side * 38.5, 0, hinge - 12], normal: side < 0 ? '-x' : 'x', up: 'z', width: 50, height: 22 });
    }
    for (const side of [-1, 1]) {                                                       // latitude T-bolts
        const y1 = side * 112, z = z0 + 64;
        s.cylinder(holder, m.steel, 'y', 10, side * 44, y1, [0, z]);
        s.cylinder(holder, m.steel, 'x', 8, -26, 26, [y1, z]);
    }
    s.knob(holder, m, 'x', 34, 70, [-30, z0 + 50]);                                     // azimuth
}

function buildHeq5Head(s, j, m, mount) {
    const D = mount.decAlongRa, junction = -(D - 70);
    // RA body (latitude joint).
    s.cylinder(j.latitude, m.white, 'y', 104, 112, junction);
    s.cylinder(j.latitude, m.green, 'y', 108, 100, 110);
    s.cylinder(j.latitude, m.white, 'y', 104, 112, 126, [0, 0], { top: 78 });
    s.cylinder(j.latitude, m.black, 'y', 38, 126, 138);                                 // polar scope cap
    s.cylinder(j.latitude, m.green, 'y', 110, junction + 10, junction);
    // Head (RA joint): a tall white block, its edges rounded, grey caps on its east face.
    s.rbox(j.ra, m.white, [-67, 67], [-71, 79], [-96, 92], 14);
    s.cylinder(j.ra, m.white, 'y', 108, 70, 78);
    for (const [y, z] of [[-30, -60], [30, 50]]) s.cylinder(j.ra, m.grey, 'x', 22, 64, 72, [y, z], { tessellation: 20 });
    s.box(j.ra, m.black, [-80, -67], [-8, 8], [-10, 34]);                               // RA clutch
    s.cylinder(j.ra, m.white, 'z', 82, 92, 120);                                        // neck
    s.cylinder(j.ra, m.green, 'z', 86, 100, 108);
    s.cylinder(j.ra, m.white, 'z', 70, -96, -124);                                      // nose
    s.cylinder(j.ra, m.white, 'z', 70, -124, -136, [0, 0], { top: 44 });
    s.knob(j.ra, m, 'x', 30, 62, [0, -116]);
    buildWeights(s, j.ra, m, { from: -136, to: -430, shaft: 18, stop: 30, weight: 110, thick: 46, at: [-290, -340], material: m.white });
    // Puck (Dec joint): a short neck and a narrow Vixen saddle with two knobs.
    s.cylinder(j.dec, m.white, 'z', 76, 120, 140);
    s.saddle(j.dec, m.white, { w: 38, len: 72, base: 140, mid: 158, top: mount.saddleTop, jaw: 12, inset: 4, r: 4 });
    s.knobs(j.dec, m, 'x', 34, 62, [[-32, 162], [32, 162]]);
}

/**
 * Sky-Watcher EQ8-R Pro: a big black observatory mount with bright green knobs. A tall latitude housing with round
 * side covers and a silver T-handle, a large head with a long handle on its west side and an electronics box on its
 * east side, a wide Dec flange under a huge saddle with three green knobs, black weights on a thick shaft.
 */
function buildEq8Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.dark, 'z', 214, z0, z0 + 58);
    s.cylinder(holder, m.black, 'z', 224, z0 + 58, z0 + 70);
    s.box(holder, m.dark, [-72, 72], [-92, 92], [z0 + 70, hinge]);
    s.cylinder(holder, m.dark, 'x', 184, -72, 72, [0, hinge]);
    for (const side of [-1, 1]) {
        s.cylinder(holder, m.dark, 'x', 96, side * 72, side * 84, [0, hinge]);          // side covers
        s.cylinder(holder, m.black, 'x', 56, side * 84, side * 88, [0, hinge]);
        s.knob(holder, m, 'x', side * 72, side * 112, [40, z0 + 96], m.green);          // azimuth
    }
    s.cylinder(holder, m.steel, 'y', 14, -92, -140, [0, hinge - 70]);                   // latitude T-handle
    s.cylinder(holder, m.steel, 'z', 12, hinge - 140, hinge - 10, [0, -140]);
}

function buildEq8Head(s, j, m, mount) {
    const D = mount.decAlongRa, junction = -(D - 140);
    // RA body (latitude joint).
    s.cylinder(j.latitude, m.dark, 'y', 172, 150, junction);
    s.cylinder(j.latitude, m.black, 'y', 150, 150, 176);
    // Head (RA joint): a large block, an electronics box east, a long handle west.
    s.rbox(j.ra, m.dark, [-96, 96], [-124, 140], [-150, 140], 26);
    s.rbox(j.ra, m.black, [92, 152], [-100, 70], [-40, 138], 12, 'y');
    s.box(j.ra, m.black, [152, 156], [-60, 30], [20, 110]);
    s.label(j.ra, 'Sky-Watcher\n~EQ8-R PRO', { at: [156.5, -15, 65], normal: 'x', up: 'z', width: 88, height: 84 });
    for (const z of [-70, 70]) s.box(j.ra, m.black, [-124, -96], [-10, 10], [z - 10, z + 10]);
    s.box(j.ra, m.black, [-140, -124], [-14, 14], [-90, 90]);
    s.cylinder(j.ra, m.dark, 'z', 120, -150, -190);                                     // nose
    buildWeights(s, j.ra, m, { from: -190, to: -720, shaft: 32, stop: 50, weight: 190, thick: 80, at: [-470, -580], material: m.dark });
    s.cylinder(j.ra, m.dark, 'z', 226, 140, 160);                                       // Dec flange
    // Puck (Dec joint): a body and a huge saddle with three green knobs on its west side.
    s.cylinder(j.dec, m.dark, 'z', 184, 160, 200);
    s.rbox(j.dec, m.dark, [-92, 92], [-165, 165], [200, 296], 18, 'y');
    s.rbox(j.dec, m.black, [-82, 82], [-160, 160], [296, mount.saddleTop], 6, 'y');
    s.knobs(j.dec, m, 'x', -92, -132, [[-100, 300], [0, 300], [100, 300]], m.green);
}

/**
 * ZWO AM5: a compact strain-wave mount, red and black, without counterweights. Red side plates with black levers
 * on a red and black base disc, a short black RA barrel into a red cube (the motor housing), and a short black Dec
 * barrel up to a large red saddle with black knobs.
 */
function buildAm5Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.black, 'z', 168, z0, z0 + 10);
    s.cylinder(holder, m.red, 'z', 156, z0 + 10, z0 + 24);
    for (const side of [-1, 1]) {
        const x = side < 0 ? [-58, -46] : [46, 58];
        s.box(holder, m.red, x, [-62, 62], [z0 + 24, hinge]);
        s.cylinder(holder, m.red, 'x', 124, side * 46, side * 58, [0, hinge]);
        s.cylinder(holder, m.steel, 'x', 16, side * 58, side * 64, [0, hinge]);
    }
    s.box(holder, m.black, [58, 66], [-8, 8], [hinge - 46, hinge + 6]);                 // latitude lever
    s.box(holder, m.black, [58, 66], [-34, 30], [hinge - 26, hinge - 14]);
    s.knob(holder, m, 'x', 58, 84, [30, z0 + 44]);
    s.box(holder, m.red, [-26, 26], [-84, -62], [z0 + 24, z0 + 66]);                   // azimuth block
    for (const side of [-1, 1]) s.knob(holder, m, 'x', side * 26, side * 56, [-74, z0 + 44]);
    s.cylinder(holder, m.steel, 'y', 10, -84, -110, [0, z0 + 58]);
}

function buildAm5Head(s, j, m, mount) {
    const D = mount.decAlongRa;
    s.cylinder(j.latitude, m.black, 'y', 104, 42, -(D - 72));                          // RA barrel
    // Head (RA joint): the red cube, a black face on its west side, a cap where a counterweight bar would screw in.
    s.rbox(j.ra, m.red, [-72, 72], [-72, 72], [-72, 72], 8);
    s.label(j.ra, 'AM5', { at: [72.5, 0, 0], normal: 'x', up: 'z', width: 110, height: 52, weight: 800 });
    s.rbox(j.ra, m.black, [-76, -70], [-70, 70], [-70, 70], 6, 'x');
    s.cylinder(j.ra, m.black, 'z', 36, -72, -80);
    // Puck (Dec joint): the Dec barrel and the red saddle with two black knobs.
    s.cylinder(j.dec, m.black, 'z', 104, 72, 132);
    s.saddle(j.dec, m.red, { w: 64, len: 96, base: 132, mid: 174, top: mount.saddleTop, jaw: 16, inset: 8 });
    s.knobs(j.dec, m, 'x', 56, 92, [[-42, 180], [42, 180]]);
}

/**
 * iOptron CEM70: a black centre-balanced mount. Its axes cross nearly over the pier, so the RA housing sits low and
 * short and a tall Dec housing rises from it; a large round RA gear housing, an electronic polar finder's disc, a
 * black saddle, and one big weight on a long shaft.
 */
function buildCem70Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.dark, 'z', 204, z0, z0 + 40);
    s.box(holder, m.dark, [-96, 96], [-46, 46], [z0 + 40, z0 + 70]);                   // azimuth clamp
    s.knob(holder, m, 'y', -46, -80, [60, z0 + 55]);
    s.box(holder, m.dark, [-62, 62], [-74, 74], [z0 + 70, hinge]);
    s.cylinder(holder, m.dark, 'x', 148, -62, 62, [0, hinge]);
    s.cylinder(holder, m.grey, 'x', 70, 62, 68, [-20, hinge - 52]);                     // polar finder disc
    s.cylinder(holder, m.steel, 'x', 20, -62, -70, [0, hinge]);
}

function buildCem70Head(s, j, m, mount) {
    const D = mount.decAlongRa;
    s.cylinder(j.latitude, m.dark, 'y', 150, 84, -(D - 30));                          // RA housing
    s.cylinder(j.latitude, m.dark, 'y', 196, 40, 92);                                  // RA gear housing
    s.cylinder(j.latitude, m.black, 'y', 120, 92, 98);
    // Head (RA joint): the tall Dec housing, its motor box east, and the Dec gear housing near the top.
    s.cylinder(j.ra, m.dark, 'z', 132, -120, 196);
    s.rbox(j.ra, m.dark, [56, 108], [-60, 60], [-40, 120], 14, 'y');
    s.label(j.ra, 'iOptron\nCEM70', { at: [108.5, 0, 40], normal: 'x', up: 'z', width: 96, height: 56 });
    s.cylinder(j.ra, m.dark, 'z', 196, 196, 232);
    // The counterweight bar hangs from a lug at the pole end, off the Dec axis, so it clears the pier (the axes
    // cross nearly over it); an end cap on the RA axis there.
    s.cylinder(j.ra, m.dark, 'y', 150, -60, -104);
    s.box(j.ra, m.dark, [-38, 38], [-190, -60], [-150, -40]);
    s.cylinder(j.ra, m.dark, 'z', 76, -150, -170, [0, CEM70_BAR_Y]);
    buildWeights(s, j.ra, m, { from: -170, to: -720, shaft: 28, stop: 40, weight: 196, thick: 110, at: [-480], material: m.dark, y: CEM70_BAR_Y });
    // Puck (Dec joint): the saddle body and a black dual saddle with knobs east.
    s.cylinder(j.dec, m.dark, 'z', 150, 232, 262);
    s.saddle(j.dec, m.black, { w: 76, len: 150, base: 262, mid: 288, top: mount.saddleTop, jaw: 18, inset: 8 });
    s.knobs(j.dec, m, 'x', 68, 104, [[-60, 294], [60, 294]]);
}

/**
 * Sky-Watcher CQ350 Pro: black with bright green knobs. A short, wide RA drum rides in a curved latitude cradle (an
 * arc under it) rather than on a tower; the flat Dec housing sits on top of the drum, its ports on the east side,
 * with the saddle above; the counterweight bar hangs from an arm at the drum's north face, off the Dec axis, as on a
 * centre-balanced mount; big green azimuth knobs on the base.
 */
function buildCq350Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.dark, 'z', 200, z0, z0 + 36);
    s.cylinder(holder, m.black, 'z', 186, z0 + 36, z0 + 46);
    for (const side of [-1, 1]) s.knob(holder, m, 'x', side * 92, side * 132, [0, z0 + 22], m.green);
    // The cradle: a post up to an arc under the RA drum, in the north-south plane about the hinge.
    s.box(holder, m.dark, [-40, 40], [-40, 40], [z0 + 46, hinge - 118]);
    const r = 122;
    for (let a = 200; a <= 340; a += 10) {
        const t = a * Math.PI / 180;
        s.cylinder(holder, m.dark, 'x', 42, -56, 56, [r * Math.cos(t), hinge + r * Math.sin(t)], { tessellation: 16 });
    }
    s.knob(holder, m, 'x', 56, 96, [0, hinge - 118], m.green);                             // latitude lock
}

function buildCq350Head(s, j, m, mount) {
    const D = mount.decAlongRa, bar = CQ350_BAR_Y;
    // RA drum (latitude joint): short and wide, in the cradle, a round cap on its north face, a green ring south.
    s.cylinder(j.latitude, m.dark, 'y', 210, 56, -46);
    s.cylinder(j.latitude, m.black, 'y', 116, -46, -58);
    s.cylinder(j.latitude, m.green, 'y', 214, 56, 64);
    s.cylinder(j.latitude, m.dark, 'y', 196, 64, 80);
    // Head (RA joint; the axes cross over the drum): the flat Dec housing on top of the drum, its ports east, and
    // an arm down past the drum's north face carrying the counterweight bar, off the Dec axis so it clears the pier.
    s.rbox(j.ra, m.dark, [-82, 82], [bar - 22, 100], [104, 170], 18, 'y');
    s.label(j.ra, 'Sky-Watcher\n~CQ350 PRO', { at: [-82.5, -62, 137], normal: '-x', up: 'z', width: 130, height: 50 });
    s.box(j.ra, m.black, [82, 88], [-60, 70], [116, 160]);
    for (const [y, z] of [[-40, 146], [-16, 146], [8, 146], [40, 130]]) s.cylinder(j.ra, m.steel, 'x', 12, 88, 91, [y, z], { tessellation: 16 });
    s.knob(j.ra, m, 'x', -82, -112, [40, 136], m.green);                                 // Dec clutch
    s.box(j.ra, m.dark, [-44, 44], [bar - 22, bar + 22], [-64, 104]);                    // the arm
    buildWeights(s, j.ra, m, { from: -64, to: -560, shaft: 28, stop: 44, weight: 160, thick: 64, at: [-380, -452], material: m.dark, y: bar });
    // Puck (Dec joint): the Dec plate and the saddle with green knobs on its west side.
    s.cylinder(j.dec, m.dark, 'z', 172, 170, 190);
    s.cylinder(j.dec, m.green, 'z', 176, 170, 176);
    s.rbox(j.dec, m.dark, [-66, 66], [-142, 142], [190, 226], 10, 'y');
    s.rbox(j.dec, m.black, [-60, 60], [-138, 138], [226, mount.saddleTop], 6, 'y');
    s.knobs(j.dec, m, 'x', -66, -104, [[-90, 230], [0, 230], [90, 230]], m.green);
}

/**
 * Sky-Watcher EQM-35 Pro: a small white mount. A white latitude tower with a black dial; a white RA housing with a
 * green ring and its black motor box; a tall white Dec housing with a ribbed conical nose, a round black clutch knob
 * and the black Dec motor box; a green anodised saddle on a short neck, and two white weights.
 */
function buildEqm35Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.white, 'z', 112, z0, z0 + 28);
    s.box(holder, m.white, [-30, 30], [-38, 38], [z0 + 28, hinge]);
    s.cylinder(holder, m.white, 'x', 80, -30, 30, [0, hinge]);
    s.cylinder(holder, m.black, 'x', 58, 30, 36, [0, hinge]);                             // dial
    s.label(holder, 'EQM-35', { at: [36.5, 0, hinge], normal: 'x', up: 'z', width: 46, height: 16 });
    s.cylinder(holder, m.black, 'x', 58, -30, -34, [0, hinge]);
    for (const side of [-1, 1]) {                                                         // latitude T-bolts
        s.cylinder(holder, m.steel, 'y', 9, side * 38, side * 96, [0, z0 + 54]);
        s.cylinder(holder, m.steel, 'x', 7, -22, 22, [side * 96, z0 + 54]);
    }
    for (const side of [-1, 1]) s.knob(holder, m, 'x', side * 30, side * 62, [-24, z0 + 16]);   // azimuth
}

function buildEqm35Head(s, j, m, mount) {
    const D = mount.decAlongRa, junction = -(D - 50);
    // RA housing (latitude joint): white, a green ring near the head, the polar scope cap south, the RA motor east.
    s.cylinder(j.latitude, m.white, 'y', 86, 92, junction);
    s.cylinder(j.latitude, m.green, 'y', 90, junction + 22, junction + 12);
    s.cylinder(j.latitude, m.white, 'y', 86, 92, 104, [0, 0], { top: 64 });
    s.cylinder(j.latitude, m.black, 'y', 44, 104, 116);
    s.rbox(j.latitude, m.black, [42, 86], [-50, 20], [-30, 30], 8, 'y');
    // Head (RA joint): the tall white Dec housing with a ribbed conical nose down to the shaft, a round black Dec
    // clutch knob, the Dec motor box with its ports on the east side near the top, and two white weights.
    s.cylinder(j.ra, m.white, 'y', 92, 50, 34);
    s.cylinder(j.ra, m.white, 'z', 92, -150, 92);
    s.cylinder(j.ra, m.white, 'z', 92, -150, -188, [0, 0], { top: 40 });
    for (let z = -158; z > -184; z -= 8) {
        const d = 92 - (52 * (-150 - z)) / 38;
        s.cylinder(j.ra, m.white, 'z', d + 4, z, z - 3, [0, 0], { tessellation: 32 });
    }
    s.knob(j.ra, m, 'x', 22, 50, [0, -176]);                                             // shaft lock
    s.cylinder(j.ra, m.black, 'x', 40, -44, -60, [-20, -40], { tessellation: 24 });     // Dec clutch knob
    s.rbox(j.ra, m.black, [44, 100], [-40, 46], [10, 92], 8);                            // Dec motor box
    for (const [y, z] of [[-24, 70], [-4, 70], [16, 70], [-14, 34]]) s.cylinder(j.ra, m.steel, 'x', 10, 100, 103, [y, z], { tessellation: 16 });
    buildWeights(s, j.ra, m, { from: -188, to: -420, shaft: 16, stop: 26, weight: 112, thick: 40, at: [-300, -346], material: m.white });
    // Puck (Dec joint): a green ring, a short white neck, and the green anodised saddle with a black knob.
    s.cylinder(j.dec, m.green, 'z', 96, 92, 100);
    s.cylinder(j.dec, m.white, 'z', 74, 100, 114);
    s.saddle(j.dec, m.green, { w: 34, len: 62, base: 114, mid: 128, top: mount.saddleTop, jaw: 10, inset: 4, r: 4 });
    s.knobs(j.dec, m, 'x', 30, 56, [[-26, 132], [26, 132]]);
}

/**
 * Sky-Watcher Wave 150i Pro: a black strain-wave mount with green trim, without counterweights. A black base with
 * green knobs and side plates (the round Wi-Fi badge on its east side), a short RA barrel, a big black drum with a
 * green ring (the RA head, carrying the Dec drive), and the Dec axis out of the drum's side to a black saddle with
 * green knobs and a small port box.
 */
function buildWave150Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.rbox(holder, m.dark, [-62, 62], [-72, 72], [z0, z0 + 40], 12);
    for (const side of [-1, 1]) {
        const x = side < 0 ? [-62, -50] : [50, 62];
        s.box(holder, m.dark, x, [-56, 56], [z0 + 40, hinge]);
        s.cylinder(holder, m.dark, 'x', 112, side * 50, side * 62, [0, hinge]);
        s.knob(holder, m, 'x', side * 62, side * 92, [-40, z0 + 20], m.green);          // azimuth
    }
    s.knob(holder, m, 'x', 62, 94, [30, hinge - 30], m.green);                           // latitude lock
    s.cylinder(holder, m.black, 'x', 52, 62, 66, [-24, z0 + 74], { tessellation: 32 });  // the Wi-Fi badge
    s.cylinder(holder, m.grey, 'x', 18, 66, 67, [-24, z0 + 74], { tessellation: 16 });
    s.knob(holder, m, 'y', -72, -100, [0, z0 + 20], m.green);
}

function buildWave150Head(s, j, m, mount) {
    const D = mount.decAlongRa;
    s.cylinder(j.latitude, m.dark, 'y', 116, 46, -(D - 100));                           // RA barrel
    // Head (RA joint): the big drum along the polar axis, a green ring near its base.
    s.cylinder(j.ra, m.dark, 'y', 172, 100, -84);
    s.cylinder(j.ra, m.dark, 'y', 172, -84, -96, [0, 0], { top: 150 });
    s.cylinder(j.ra, m.green, 'y', 176, 70, 80);
    s.label(j.ra, 'Sky-Watcher\n~WAVE 150i', { at: [87, -6, 0], normal: 'x', up: 'z', width: 116, height: 44 });
    s.cylinder(j.ra, m.black, 'z', 40, -86, -92);                                       // counterweight thread cap
    // Puck (Dec joint): out of the drum's side: the Dec plate, a port box and the saddle with green knobs.
    s.cylinder(j.dec, m.dark, 'z', 108, 84, 104);
    s.rbox(j.dec, m.black, [-46, 46], [-70, -20], [104, 126], 6, 'y');
    s.saddle(j.dec, m.dark, { w: 52, len: 92, base: 104, mid: 128, top: mount.saddleTop, jaw: 14 });
    s.knobs(j.dec, m, 'x', 46, 76, [[-40, 132], [40, 132]], m.green);
}

/**
 * A fork mount (generic, after the classic Schmidt-Cassegrain forks): a drive base, a turntable and two arms in dark
 * grey, the OTA hanging between them on the Dec axis. On a wedge for a polar mount; for Alt-Az the drive base stands
 * on a round pedestal, its axis straight up. No counterweights.
 */
function buildForkBase(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    if (mount.mode === 0) {                                                               // Alt-Az: a pedestal
        s.cylinder(holder, m.dark, 'z', 240, z0, z0 + 24);
        s.cylinder(holder, m.dark, 'z', 200, z0 + 24, hinge - 10);
        return;
    }
    // A wedge: a base plate, two side cheeks either side of the drive base, the latitude bolt and a lock knob.
    s.rbox(holder, m.dark, [-176, 176], [-150, 150], [z0, z0 + 22], 20);
    for (const side of [-1, 1]) {
        const x = side < 0 ? [-176, -158] : [158, 176];
        s.box(holder, m.dark, x, [-120, 120], [z0 + 22, hinge + 30]);
        s.cylinder(holder, m.steel, 'x', 30, side * 176, side * 186, [0, hinge]);
        s.knob(holder, m, 'x', side * 186, side * 214, [-70, hinge - 60]);
    }
    s.cylinder(holder, m.steel, 'z', 16, z0 + 22, hinge - 80, [0, 110]);                 // latitude bolt
}

function buildForkHead(s, j, m, mount) {
    const D = mount.decAlongRa, g = (mount.otaHalfWidth ?? 120) + 22, arm = 46;
    // Drive base (latitude joint; the hinge at its back): a wide disc and its control panel.
    s.cylinder(j.latitude, m.black, 'y', 300, 12, -64);
    s.rbox(j.latitude, m.black, [-60, 60], [-60, -10], [138, 170], 8, 'y');
    for (const x of [-30, 0, 30]) s.cylinder(j.latitude, m.steel, 'z', 10, 170, 173, [x, -35], { tessellation: 12 });
    // Turntable and fork (RA joint; origin where the Dec axis crosses the polar axis).
    s.cylinder(j.ra, m.dark, 'y', 280, D - 64, D - 84);
    s.rbox(j.ra, m.dark, [-60, 60], [D - 120, D - 84], [-(g + arm), g + arm], 12, 'x');
    for (const side of [-1, 1]) {
        const z = side < 0 ? [-(g + arm), -g] : [g, g + arm];
        s.rbox(j.ra, m.dark, [-48, 48], [-40, D - 110], z, 18, 'z');                     // the arm
        s.cylinder(j.ra, m.dark, 'z', 110, side * g, side * (g + arm), [0, 0]);           // its bearing housing
        s.cylinder(j.ra, m.black, 'z', 70, side * (g + arm), side * (g + arm + 4), [0, 0]);
    }
    s.label(j.ra, 'FORK', { at: [0, D - 102, g + arm + 0.5], normal: 'z', up: '-y', width: 60, height: 22 });
    // Dec joint: the trunnions from the arms to the OTA's side plates.
    for (const side of [-1, 1]) {
        s.cylinder(j.dec, m.steel, 'z', 50, side * (g - 26), side * g, [0, 0]);
        s.rbox(j.dec, m.black, [-36, 36], [-90, 90], side < 0 ? [-(g - 20), -(g - 30)] : [g - 30, g - 20], 8, 'z');
    }
}

/**
 * iOptron CEM40: a centre-balanced mount in red and black, the CEM70's smaller sibling. A black base with shaped
 * side plates, a brass latitude screw and a bubble level; a red RA housing with a black octagonal end cap; a red
 * Dec housing between black end blocks; a black saddle, and one black weight on a steel shaft.
 */
function buildCem40Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.dark, 'z', 156, z0, z0 + 24);
    for (const side of [-1, 1]) {
        const x = side < 0 ? [-58, -46] : [46, 58];
        s.box(holder, m.dark, x, [-66, 54], [z0 + 24, hinge]);
        s.cylinder(holder, m.dark, 'x', 110, side * 46, side * 58, [0, hinge]);
        s.cylinder(holder, m.steel, 'x', 18, side * 58, side * 62, [0, hinge]);
    }
    s.cylinder(holder, m.brass, 'z', 10, z0 + 30, hinge - 60, [0, -50]);              // latitude screw
    s.cylinder(holder, m.black, 'x', 26, -12, 12, [-50, z0 + 70], { tessellation: 5 });
    s.cylinder(holder, m.green, 'x', 16, 58, 62, [-40, z0 + 14], { tessellation: 16 });  // bubble level
    s.knob(holder, m, 'y', -66, -92, [30, z0 + 14]);                                     // azimuth
}

function buildCem40Head(s, j, m, mount) {
    const D = mount.decAlongRa;
    // RA housing (latitude joint): red, a black octagonal cap on its south end.
    s.cylinder(j.latitude, m.red, 'y', 118, 92, -(D - 20));
    s.cylinder(j.latitude, m.dark, 'y', 142, 92, 128, [0, 0], { tessellation: 8 });
    // Head (RA joint): the red Dec housing between black end blocks, the Dec drive on top, and the counterweight.
    s.rbox(j.ra, m.dark, [-60, 60], [-62, 62], [-84, -40], 10);
    s.rbox(j.ra, m.red, [-56, 56], [-58, 58], [-40, 78], 8);
    s.label(j.ra, 'iOptron\nCEM40', { at: [-56.5, 0, 20], normal: '-x', up: 'z', width: 90, height: 52 });
    s.rbox(j.ra, m.dark, [-60, 60], [-62, 62], [78, 108], 10);
    s.cylinder(j.ra, m.dark, 'z', 150, 108, 136);
    s.rbox(j.ra, m.dark, [56, 92], [-40, 30], [20, 96], 6, 'y');                         // ports box
    // The counterweight bar hangs from a lug at the pole end, off the Dec axis, so it clears the pier (the axes
    // cross nearly over it); an end cap on the RA axis there.
    s.cylinder(j.ra, m.dark, 'y', 124, -62, -100);
    s.box(j.ra, m.dark, [-32, 32], [-168, -62], [-112, -30]);
    s.cylinder(j.ra, m.dark, 'z', 62, -112, -130, [0, CEM40_BAR_Y]);
    buildWeights(s, j.ra, m, { from: -130, to: -540, shaft: 22, stop: 34, weight: 150, thick: 84, at: [-380], material: m.dark, y: CEM40_BAR_Y });
    // Puck (Dec joint): the Dec plate and a black saddle with a big knob east.
    s.cylinder(j.dec, m.dark, 'z', 128, 136, 156);
    s.saddle(j.dec, m.black, { w: 62, len: 130, base: 156, mid: 196, top: mount.saddleTop, jaw: 14 });
    s.cylinder(j.dec, m.black, 'x', 46, 62, 82, [60, 180], { tessellation: 24 });
}

/**
 * OTAs built from simple shapes (ASSEMBLY_BUILT_OTAS), in their own frame on the saddle (millimetres): the tube
 * along Y with its aperture toward -Y (the pole at the home position), its dovetail standing on the saddle at z = 0.
 * Drawn at the size an .obj OTA has at the default scale (ASSEMBLY_OTA.scale), then scaled with it.
 */

/** A small finder scope on a foot, along the tube's top at height `z`, from y0 to y1. */
function buildFinder(s, ota, m, z, y0, y1) {
    s.box(ota, m.black, [-10, 10], [y0 + 30, y1 - 30], [z, z + 24]);
    s.cylinder(ota, m.black, 'y', 44, y0, y1, [0, z + 46]);
    s.cylinder(ota, m.glass, 'y', 36, y0 - 1, y0, [0, z + 46], { tessellation: 24 });
}

/**
 * Newtonian: a long open tube on two tube rings and a dovetail; the open aperture dark at the front, the focuser on
 * the side near the front, a finder on top, and the mirror cell with collimation knobs at the back.
 */
function buildNewtonian(s, ota, m) {
    const d = 250, half = 520, axis = 20 + 18 + d / 2;          // the tube's axis above the dovetail's foot
    s.box(ota, m.steel, [-40, 40], [-270, 270], [0, 20]);       // dovetail
    for (const y of [-210, 210]) {                              // tube rings and their feet
        s.box(ota, m.black, [-18, 18], [y - 14, y + 14], [20, 40]);
        s.cylinder(ota, m.black, 'y', d + 26, y - 16, y + 16, [0, axis], { tessellation: 48 });
    }
    // The tube stops just inside the front ring and mirror cell, so their faces don't fight over the same plane.
    s.cylinder(ota, m.ota, 'y', d, -half + 4, half - 4, [0, axis], { tessellation: 48 });
    s.cylinder(ota, m.black, 'y', d + 8, -half, -half + 16, [0, axis], { tessellation: 48 });   // front ring
    s.cylinder(ota, m.glass, 'y', d - 14, -half - 1, -half, [0, axis], { tessellation: 48 });   // the open end, dark
    s.cylinder(ota, m.black, 'y', d + 6, half - 22, half, [0, axis], { tessellation: 48 });     // mirror cell
    for (const a of [90, 210, 330]) {                           // collimation knobs
        const t = a * Math.PI / 180;
        s.cylinder(ota, m.black, 'y', 22, half, half + 16, [d * 0.32 * Math.cos(t), axis + d * 0.32 * Math.sin(t)], { tessellation: 12 });
    }
    // Focuser out of the east side near the front, its draw tube and a knob.
    s.box(ota, m.black, [d / 2 - 6, d / 2 + 18], [-half + 70, -half + 150], [axis - 40, axis + 40]);
    s.cylinder(ota, m.black, 'x', 64, d / 2 + 18, d / 2 + 70, [-half + 110, axis]);
    s.cylinder(ota, m.steel, 'x', 48, d / 2 + 70, d / 2 + 110, [-half + 110, axis]);
    s.cylinder(ota, m.black, 'z', 20, axis - 50, axis + 50, [d / 2 + 44, -half + 110], { tessellation: 12 });
    buildFinder(s, ota, m, axis + d / 2, -half + 60, -half + 260);
}

/**
 * Maksutov-Cassegrain: a short, fat tube; the glass meniscus at the front with its silvered secondary spot, a
 * mounting rail, and at the back the visual back, a star diagonal, an eyepiece and the focus knob.
 */
function buildMaksutov(s, ota, m) {
    const d = 210, front = -240, back = 230, axis = 18 + 14 + d / 2;
    s.box(ota, m.steel, [-36, 36], [-170, 170], [0, 18]);       // dovetail
    for (const y of [-120, 120]) s.box(ota, m.black, [-24, 24], [y - 22, y + 22], [18, 40]);    // mounting blocks
    s.cylinder(ota, m.ota, 'y', d, front + 4, back, [0, axis], { tessellation: 48 });   // just inside the front cell
    s.cylinder(ota, m.black, 'y', d + 12, front, front + 34, [0, axis], { tessellation: 48 });  // front cell
    s.cylinder(ota, m.glass, 'y', d - 24, front - 2, front, [0, axis], { tessellation: 48 });   // meniscus
    s.cylinder(ota, m.steel, 'y', 60, front - 3, front - 2, [0, axis], { tessellation: 32 });  // secondary spot
    s.cylinder(ota, m.black, 'y', d - 8, back, back + 30, [0, axis], { tessellation: 48 });    // rear cell
    s.cylinder(ota, m.black, 'y', 56, back + 30, back + 74, [0, axis]);                        // visual back
    s.box(ota, m.black, [-30, 30], [back + 74, back + 134], [axis - 30, axis + 30]);           // star diagonal
    s.cylinder(ota, m.black, 'z', 46, axis + 30, axis + 74, [0, back + 104]);
    s.cylinder(ota, m.steel, 'z', 40, axis + 74, axis + 130, [0, back + 104]);                 // eyepiece
    s.cylinder(ota, m.black, 'x', 38, d / 2 - 30, d / 2 + 6, [back + 14, axis - 40], { tessellation: 16 });  // focus knob
    buildFinder(s, ota, m, axis + d / 2, front + 40, front + 200);
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

/** mount: as for loadModel. Aims the camera again for a new latitude, without reloading the model (centreOnMount). */
export function recentre(viewer, mount) {
    if (viewer.centreOtaBottom !== undefined) viewer.centreOnMount(mount, viewer.centreOtaBottom);
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
