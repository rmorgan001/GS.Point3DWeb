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

// GSServer defaults (Server.settings): ModelPierHeight, ModelBeamWidth, ModelPierPivot.
const PIER_HEIGHT = 500;
const BEAM_WIDTH = 50;
const PIER_PIVOT = 130;
const ROTATION_CENTRE_Z = 525;
const HELIX_TO_SCENE = 7 / PIER_HEIGHT; // pier is 7 scene units tall

// Alignment modes (ASCOM AlignmentModes)
const ALT_AZ = 0, POLAR = 1;

function loadScript(src) {
    return new Promise((resolve, reject) => {
        const el = document.createElement('script');
        el.src = src;
        el.onload = resolve;
        el.onerror = () => reject(new Error(`Failed to load ${src}`));
        document.head.appendChild(el);
    });
}

function ensureBabylon() {
    if (window.BABYLON && window.BABYLON.SceneLoader) return Promise.resolve();
    // Trimmed Babylon.js build with only what this viewer uses (tools/babylon-bundle).
    babylonReady ??= loadScript(base + 'babylon.custom.min.js');
    return babylonReady;
}

const rad = deg => deg * Math.PI / 180;
const deg = r => r * 180 / Math.PI;

// Default camera: looking at the mount head from the south-west, a little above.
const DEFAULT_CAMERA = { alpha: 150, beta: 60, radius: 40, targetX: 0, targetY: 6, targetZ: 0 };
const CAMERA_SAVE_DELAY_MS = 700;

class TelescopeViewer {
    constructor(canvas, options) {
        const B = window.BABYLON;
        this.canvas = canvas;
        // Green Swamp palette: primary = telescope (green), secondary = light (moon white).
        this.primaryColor = options.primaryColor || '#52b788';
        this.secondaryColor = options.secondaryColor || '#e8e6df';
        // Background image URL (relative to the page), or null for a plain dark sky.
        this.backgroundUrl = options.background === undefined ? 'model3d/backgrounds/NorthAmerica.jpg' : options.background;
        this.textures = {
            compassN: base + 'compassN.png',
            compassS: base + 'compassS.png'
        };
        this.modelMeshes = [];
        this.pierMeshes = [];
        this.pose = { x: 0, y: 0, z: 0, yCentre: 0 };

        this.engine = new B.Engine(canvas, true, { preserveDrawingBuffer: true, stencil: true, antialias: true });
        this.scene = this.createScene();
        this.engine.runRenderLoop(() => this.scene && this.scene.render());

        // The canvas lives in a resizable panel, not the whole window.
        this.resizeObserver = new ResizeObserver(() => this.engine.resize());
        this.resizeObserver.observe(canvas);

        this.setHemisphere(options.northern !== false);
        this.setBackground(this.backgroundUrl);

        // Camera: start from the server's saved view (saved / reset from the back of the card).
        this.setCamera(options.camera);
    }

    /** Applies a saved camera ({ alpha, beta (degrees), radius, targetX/Y/Z }), or the default. */
    setCamera(view) {
        const B = window.BABYLON;
        const v = view || DEFAULT_CAMERA;
        this.camera.alpha = rad(v.alpha);
        this.camera.beta = rad(v.beta);
        this.camera.radius = v.radius;
        this.camera.setTarget(new B.Vector3(v.targetX, v.targetY, v.targetZ));
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
        this.camera.wheelDeltaPercentage = 0.02;

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

        this.pierMaterial = new B.StandardMaterial('Pier', scene);
        this.pierMaterial.diffuseColor = B.Color3.FromHexString('#34474f');
        this.pierMaterial.specularColor = new B.Color3(0.1, 0.1, 0.1);

        // The moving part of the mount (the .obj model) hangs off this node.
        this.modelNode = new B.TransformNode('Model', scene);
        this.modelNode.parent = this.root;
        this.modelNode.rotationQuaternion = B.Quaternion.Identity();

        return scene;
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

    async loadModel(url, mount) {
        const B = window.BABYLON;
        this.modelMeshes.forEach(m => m.dispose());
        this.modelMeshes = [];
        this.buildPier(mount?.mode ?? 2, mount?.latitude ?? 0);

        // Materials are set below, so don't request a .mtl file (avoids a 404).
        if (B.OBJFileLoader) B.OBJFileLoader.SKIP_MATERIALS = true;

        const slash = url.lastIndexOf('/') + 1;
        const result = await B.SceneLoader.ImportMeshAsync('', url.substring(0, slash), url.substring(slash), this.scene, null, '.obj');
        const meshes = result.meshes.filter(m => m.getTotalVertices() > 0);
        if (meshes.length === 0) return 'Error: model has no meshes';

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
     */
    setPose(xAxisOffset, yAxisOffset, zAxisOffset, yAxisCentre) {
        this.pose = { x: xAxisOffset, y: yAxisOffset, z: zAxisOffset, yCentre: yAxisCentre };
        this.applyPose();
    }

    applyPose() {
        const B = window.BABYLON;
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
    }

    /** Background image URL (relative to the page), or null for a plain dark sky. */
    setBackground(url) {
        const B = window.BABYLON;
        this.backgroundUrl = url;
        const material = this.skybox.material;
        const old = material.reflectionTexture;
        if (url) {
            const texture = new B.Texture(new URL(url, document.baseURI).href, this.scene, true);
            texture.coordinatesMode = B.Texture.FIXED_EQUIRECTANGULAR_MODE;
            texture.level = 0.55; // dim the image so the mount stands out
            material.reflectionTexture = texture;
            this.skybox.setEnabled(true);
        } else {
            material.reflectionTexture = null;
            this.skybox.setEnabled(false);
        }
        if (old) old.dispose();
    }

    setHemisphere(isNorthern) {
        const B = window.BABYLON;
        const url = isNorthern ? this.textures.compassN : this.textures.compassS;
        const image = new Image();
        image.onload = () => {
            if (this.scene.isDisposed) return;
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
        };
        image.src = url;
    }

    dispose() {
        this.resizeObserver.disconnect();
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

export function setHemisphere(viewer, isNorthern) {
    viewer.setHemisphere(isNorthern);
}

export function dispose(viewer) {
    viewer.dispose();
}
