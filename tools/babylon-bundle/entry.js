// The parts of Babylon.js that GS Point3D Web's 3D viewer (wwwroot/model3d/model3d.js) uses,
// exposed as window.BABYLON like the full UMD build. Add an import here if the viewer starts
// using another Babylon class, then run `npm run build`.

import { Engine } from "@babylonjs/core/Engines/engine";
import { Scene } from "@babylonjs/core/scene";
import { ArcRotateCamera } from "@babylonjs/core/Cameras/arcRotateCamera";
import { HemisphericLight } from "@babylonjs/core/Lights/hemisphericLight";
import { DirectionalLight } from "@babylonjs/core/Lights/directionalLight";
import { Color3, Color4 } from "@babylonjs/core/Maths/math.color";
import { Matrix, Quaternion, Vector3, Vector4 } from "@babylonjs/core/Maths/math.vector";
import { CreateBox } from "@babylonjs/core/Meshes/Builders/boxBuilder";
import { CreateCylinder } from "@babylonjs/core/Meshes/Builders/cylinderBuilder";
import { CreateGround } from "@babylonjs/core/Meshes/Builders/groundBuilder";
import { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import { StandardMaterial } from "@babylonjs/core/Materials/standardMaterial";
import { Texture } from "@babylonjs/core/Materials/Textures/texture";
import { DynamicTexture } from "@babylonjs/core/Materials/Textures/dynamicTexture";
import { SceneLoader } from "@babylonjs/core/Loading/sceneLoader";

// The OBJ loader registers itself with SceneLoader when imported.
import { OBJFileLoader } from "@babylonjs/loaders/OBJ/objFileLoader";

window.BABYLON = {
    Engine,
    Scene,
    ArcRotateCamera,
    HemisphericLight,
    DirectionalLight,
    Color3,
    Color4,
    Matrix,
    Quaternion,
    Vector3,
    Vector4,
    MeshBuilder: { CreateBox, CreateCylinder, CreateGround },
    TransformNode,
    StandardMaterial,
    Texture,
    DynamicTexture,
    SceneLoader,
    OBJFileLoader
};
