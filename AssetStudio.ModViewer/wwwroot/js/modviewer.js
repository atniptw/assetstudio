import * as THREE from "https://esm.sh/three@0.160.1";
import { OrbitControls } from "https://esm.sh/three@0.160.1/examples/jsm/controls/OrbitControls.js";

(function () {
    const MOD_DB_NAME = "modviewer-mods";
    const MOD_DB_VERSION = 1;
    const MOD_STORE = "hhhFiles";

    const state = {
        renderer: null,
        scene: null,
        camera: null,
        controls: null,
        frameHandle: null,
        avatarGroup: null,
        modGroups: new Map(),
        anchorNodes: new Map(),
        avatarMeshCount: 0,
    };

    const DEFAULT_ANCHOR_TAGS = ["head", "neck", "body", "hip", "leftarm", "rightarm", "leftleg", "rightleg", "world"];

    function disposeObjectTree(obj) {
        obj.traverse((node) => {
            if (node.geometry) {
                node.geometry.dispose();
            }
            if (node.material) {
                if (Array.isArray(node.material)) {
                    node.material.forEach((m) => {
                        if (m.map) m.map.dispose();
                        if (m.normalMap) m.normalMap.dispose();
                        m.dispose();
                    });
                } else {
                    if (node.material.map) node.material.map.dispose();
                    if (node.material.normalMap) node.material.normalMap.dispose();
                    node.material.dispose();
                }
            }
        });
    }

    function init(canvasId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) {
            return;
        }

        const width = canvas.clientWidth || canvas.parentElement.clientWidth;
        const height = canvas.clientHeight || canvas.parentElement.clientHeight;

        const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
        renderer.setSize(width, height, false);
        renderer.setPixelRatio(window.devicePixelRatio || 1);

        const scene = new THREE.Scene();
        scene.background = new THREE.Color(0x0b0f16);

        const camera = new THREE.PerspectiveCamera(45, width / height, 0.1, 1000);
        camera.position.set(0, 1.6, 3.5);

        const controls = new OrbitControls(camera, renderer.domElement);
        controls.enableDamping = true;
        controls.dampingFactor = 0.08;
        controls.rotateSpeed = 0.9;
        controls.zoomSpeed = 1.0;
        controls.panSpeed = 0.8;
        controls.screenSpacePanning = true;
        controls.minDistance = 0.25;
        controls.maxDistance = 30;
        controls.target.set(0, 0.8, 0);
        controls.update();

        const ambient = new THREE.AmbientLight(0xffffff, 0.7);
        const key = new THREE.DirectionalLight(0xffffff, 0.9);
        key.position.set(2, 4, 3);

        scene.add(ambient);
        scene.add(key);

        const grid = new THREE.GridHelper(6, 20, 0x2e394d, 0x202634);
        grid.position.y = -0.8;
        scene.add(grid);

        state.renderer = renderer;
        state.scene = scene;
        state.camera = camera;
        state.controls = controls;

        const render = () => {
            if (state.controls) {
                state.controls.update();
            }
            state.renderer.render(state.scene, state.camera);
            state.frameHandle = requestAnimationFrame(render);
        };

        render();
        window.addEventListener("resize", () => resize(canvas));
    }

    function resize(canvas) {
        if (!state.renderer || !state.camera) {
            return;
        }

        const width = canvas.clientWidth || canvas.parentElement.clientWidth;
        const height = canvas.clientHeight || canvas.parentElement.clientHeight;
        state.renderer.setSize(width, height, false);
        state.camera.aspect = width / height;
        state.camera.updateProjectionMatrix();
    }

    function renderAvatar(avatarJsonStr) {
        try {
            const avatarData = typeof avatarJsonStr === 'string' 
                ? JSON.parse(avatarJsonStr) 
                : avatarJsonStr;

            if (!state.scene) {
                console.error("Scene not initialized");
                return;
            }

            clearModAssets();

            if (state.avatarGroup) {
                state.scene.remove(state.avatarGroup);
                disposeObjectTree(state.avatarGroup);
                state.avatarGroup = null;
            }

            const group = buildGroupFromAvatarData(avatarData, "Avatar");
            if (!group || group.children.length === 0) {
                console.warn("No meshes were rendered");
                state.avatarMeshCount = 0;
                return;
            }

            state.avatarMeshCount = group.children.length;

            const bbox = new THREE.Box3().setFromObject(group);
            const center = bbox.getCenter(new THREE.Vector3());
            const size = bbox.getSize(new THREE.Vector3());
            const maxDim = Math.max(size.x, size.y, size.z);
            const scale = 1.5 / (maxDim || 1);

            group.position.sub(center);
            group.scale.multiplyScalar(scale);

            const framedBox = new THREE.Box3().setFromObject(group);
            const framedCenter = framedBox.getCenter(new THREE.Vector3());
            const framedSize = framedBox.getSize(new THREE.Vector3());

            state.scene.add(group);
            state.avatarGroup = group;
            rebuildAnchorNodes(avatarData);

            if (state.camera) {
                const fov = state.camera.fov * (Math.PI / 180);
                const fitHeightDistance = framedSize.y / (2 * Math.tan(fov / 2));
                const fitWidthDistance = (framedSize.x / state.camera.aspect) / (2 * Math.tan(fov / 2));
                const distance = Math.max(fitHeightDistance, fitWidthDistance, framedSize.z) * 1.35;

                state.camera.position.set(
                    framedCenter.x,
                    framedCenter.y + framedSize.y * 0.15,
                    framedCenter.z + distance
                );
                state.camera.lookAt(framedCenter);

                if (state.controls) {
                    state.controls.target.copy(framedCenter);
                    state.controls.update();
                }
            }

            console.log(`Rendered ${group.children.length} meshes`);
        } catch (err) {
            console.error("Failed to render avatar:", err);
        }
    }

    function buildGroupFromAvatarData(avatarData, groupName) {
        const group = new THREE.Group();
        group.name = groupName;

        const materials = new Map();
        const textures = new Map();

        if (avatarData.textures && Array.isArray(avatarData.textures)) {
            avatarData.textures.forEach((texData, idx) => {
                try {
                    if (typeof texData.dataUrl !== "string" || !texData.dataUrl.startsWith("data:image/")) {
                        return;
                    }

                    const texture = new THREE.TextureLoader().load(texData.dataUrl);
                    texture.flipY = false;
                    textures.set(idx, texture);
                } catch (err) {
                    console.warn(`Failed to create texture ${idx}:`, err.message);
                }
            });
        }

        if (avatarData.materials && Array.isArray(avatarData.materials)) {
            avatarData.materials.forEach((matData, idx) => {
                const material = new THREE.MeshStandardMaterial({
                    color: new THREE.Color(
                        matData.baseColor?.[0] ?? 1,
                        matData.baseColor?.[1] ?? 1,
                        matData.baseColor?.[2] ?? 1
                    ),
                    metalness: matData.metallic ?? 0,
                    roughness: matData.roughness ?? 0.5,
                    side: THREE.DoubleSide
                });

                const albedoIndex = typeof matData.albedoTextureIndex === "number"
                    ? matData.albedoTextureIndex
                    : matData.textureIndex;
                if (typeof albedoIndex === "number" && textures.has(albedoIndex)) {
                    material.map = textures.get(albedoIndex);
                    material.map.colorSpace = THREE.SRGBColorSpace;
                    material.needsUpdate = true;
                }

                if (typeof matData.normalTextureIndex === "number" && textures.has(matData.normalTextureIndex)) {
                    material.normalMap = textures.get(matData.normalTextureIndex).clone();
                    material.normalMap.colorSpace = THREE.NoColorSpace;
                    material.normalMap.flipY = false;
                    material.needsUpdate = true;
                }

                materials.set(idx, material);
            });
        }

        if (avatarData.meshes && Array.isArray(avatarData.meshes)) {
            avatarData.meshes.forEach((meshData, idx) => {
                try {
                    const geometry = new THREE.BufferGeometry();

                    if (meshData.vertices && meshData.vertices.length > 0) {
                        const vertices = new Float32Array(meshData.vertices);
                        geometry.setAttribute("position", new THREE.BufferAttribute(vertices, 3));
                    }

                    if (meshData.normals && meshData.normals.length > 0) {
                        const normals = new Float32Array(meshData.normals);
                        geometry.setAttribute("normal", new THREE.BufferAttribute(normals, 3));
                    } else if (meshData.vertices && meshData.vertices.length > 0) {
                        geometry.computeVertexNormals();
                    }

                    if (meshData.uv && meshData.uv.length > 0) {
                        const uvs = new Float32Array(meshData.uv);
                        geometry.setAttribute("uv", new THREE.BufferAttribute(uvs, 2));
                    }

                    if (meshData.indices && meshData.indices.length > 0) {
                        const indices = new Uint32Array(meshData.indices);
                        geometry.setIndex(new THREE.BufferAttribute(indices, 1));
                    }

                    const matIdx = meshData.materialIndex ?? 0;
                    let material = materials.get(matIdx);
                    if (!material) {
                        material = new THREE.MeshStandardMaterial({
                            color: 0x888888,
                            metalness: 0,
                            roughness: 0.5,
                            side: THREE.DoubleSide
                        });
                    }

                    const mesh = new THREE.Mesh(geometry, material);
                    mesh.name = meshData.name || `Mesh_${idx}`;

                    if (Array.isArray(meshData.position) && meshData.position.length >= 3) {
                        mesh.position.set(meshData.position[0], meshData.position[1], meshData.position[2]);
                    }
                    if (Array.isArray(meshData.rotation) && meshData.rotation.length >= 4) {
                        mesh.quaternion.set(meshData.rotation[0], meshData.rotation[1], meshData.rotation[2], meshData.rotation[3]);
                    }
                    if (Array.isArray(meshData.scale) && meshData.scale.length >= 3) {
                        mesh.scale.set(meshData.scale[0], meshData.scale[1], meshData.scale[2]);
                    }

                    group.add(mesh);
                } catch (err) {
                    console.warn(`Failed to create mesh ${idx}:`, err.message);
                }
            });
        }

        return group;
    }

    function rebuildAnchorNodes(avatarData) {
        state.anchorNodes.clear();
        if (!state.avatarGroup) {
            return;
        }

        const anchors = Array.isArray(avatarData.attachmentAnchors) ? avatarData.attachmentAnchors : [];
        const anchorsByTag = new Map();
        anchors.forEach((anchor) => {
            if (anchor && typeof anchor.tag === "string") {
                anchorsByTag.set(anchor.tag.toLowerCase(), anchor);
            }
        });

        DEFAULT_ANCHOR_TAGS.forEach((tag) => {
            const anchorData = anchorsByTag.get(tag);
            const node = new THREE.Group();
            node.name = `Anchor_${tag}`;

            if (anchorData && Array.isArray(anchorData.position) && anchorData.position.length >= 3) {
                node.position.set(anchorData.position[0], anchorData.position[1], anchorData.position[2]);
            }
            if (anchorData && Array.isArray(anchorData.rotation) && anchorData.rotation.length >= 4) {
                node.quaternion.set(anchorData.rotation[0], anchorData.rotation[1], anchorData.rotation[2], anchorData.rotation[3]);
            }
            if (anchorData && Array.isArray(anchorData.scale) && anchorData.scale.length >= 3) {
                node.scale.set(anchorData.scale[0], anchorData.scale[1], anchorData.scale[2]);
            }

            state.avatarGroup.add(node);
            state.anchorNodes.set(tag, node);
        });
    }

    function addModAsset(modId, avatarJsonStr, bodyPartTag) {
        if (!state.scene || !state.avatarGroup) {
            return {
                added: false,
                reason: "scene-or-avatar-missing",
                anchorTag: null,
                anchorFound: false,
                modGroupCount: state.modGroups.size,
                meshCount: 0,
                boundsCenter: null,
                boundsSize: null,
            };
        }

        removeModAsset(modId);

        const avatarData = typeof avatarJsonStr === "string"
            ? JSON.parse(avatarJsonStr)
            : avatarJsonStr;

        const modGroup = buildGroupFromAvatarData(avatarData, `ModAsset_${modId}`);
        if (!modGroup || modGroup.children.length === 0) {
            return {
                added: false,
                reason: "no-meshes",
                anchorTag: null,
                anchorFound: false,
                modGroupCount: state.modGroups.size,
                meshCount: 0,
                boundsCenter: null,
                boundsSize: null,
            };
        }

        const box = new THREE.Box3().setFromObject(modGroup);
        const boundsCenter = box.getCenter(new THREE.Vector3());
        const boundsSize = box.getSize(new THREE.Vector3());

        const tag = typeof bodyPartTag === "string" ? bodyPartTag.toLowerCase() : "world";
        const anchor = state.anchorNodes.get(tag);
        const anchorFound = !!anchor;
        const parent = anchor || state.avatarGroup;
        parent.add(modGroup);
        state.modGroups.set(modId, modGroup);

        return {
            added: true,
            reason: "ok",
            anchorTag: tag,
            anchorFound,
            modGroupCount: state.modGroups.size,
            meshCount: modGroup.children.length,
            boundsCenter: [boundsCenter.x, boundsCenter.y, boundsCenter.z],
            boundsSize: [boundsSize.x, boundsSize.y, boundsSize.z],
        };
    }

    function removeModAsset(modId) {
        const existing = state.modGroups.get(modId);
        if (!existing) {
            return;
        }

        if (existing.parent) {
            existing.parent.remove(existing);
        }
        disposeObjectTree(existing);
        state.modGroups.delete(modId);
    }

    function clearModAssets() {
        for (const [modId] of state.modGroups) {
            removeModAsset(modId);
        }
    }

    function getRenderState() {
        let sceneMeshCount = 0;
        if (state.scene) {
            state.scene.traverse((obj) => {
                if (obj && obj.isMesh) {
                    sceneMeshCount += 1;
                }
            });
        }

        return {
            sceneMeshCount,
            modGroupCount: state.modGroups.size,
            anchorCount: state.anchorNodes.size,
            avatarMeshCount: state.avatarMeshCount,
        };
    }

    async function clearIndexedDb() {
        try {
            await clearModDb();
        } catch (err) {
            console.warn("IndexedDB clear failed", err);
        }
    }

    function openModDb() {
        return new Promise((resolve, reject) => {
            if (!window.indexedDB) {
                resolve(null);
                return;
            }

            const request = indexedDB.open(MOD_DB_NAME, MOD_DB_VERSION);
            request.onupgradeneeded = (event) => {
                const db = event.target.result;
                if (!db.objectStoreNames.contains(MOD_STORE)) {
                    const store = db.createObjectStore(MOD_STORE, { keyPath: "id" });
                    store.createIndex("bodyPartTag", "bodyPartTag", { unique: false });
                    store.createIndex("importedAtUtc", "importedAtUtc", { unique: false });
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error || new Error("Unable to open IndexedDB"));
        });
    }

    function txComplete(tx) {
        return new Promise((resolve, reject) => {
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error || new Error("IndexedDB transaction failed"));
            tx.onabort = () => reject(tx.error || new Error("IndexedDB transaction aborted"));
        });
    }

    async function storeHhhFiles(files) {
        const db = await openModDb();
        if (!db || !Array.isArray(files) || files.length === 0) {
            return;
        }

        const tx = db.transaction(MOD_STORE, "readwrite");
        const store = tx.objectStore(MOD_STORE);
        for (const file of files) {
            store.put(file);
        }

        await txComplete(tx);
        db.close();
    }

    async function getStoredHhhFiles() {
        const db = await openModDb();
        if (!db) {
            return [];
        }

        const tx = db.transaction(MOD_STORE, "readonly");
        const store = tx.objectStore(MOD_STORE);

        const result = await new Promise((resolve, reject) => {
            const request = store.getAll();
            request.onsuccess = () => resolve(request.result || []);
            request.onerror = () => reject(request.error || new Error("Failed to read stored .hhh files"));
        });

        await txComplete(tx);
        db.close();
        return result;
    }

    function clearModDb() {
        return new Promise((resolve) => {
            if (!window.indexedDB) {
                resolve();
                return;
            }

            const request = indexedDB.deleteDatabase(MOD_DB_NAME);
            request.onsuccess = () => resolve();
            request.onerror = () => resolve();
            request.onblocked = () => resolve();
        });
    }

    function openFilePicker(elementId) {
        const input = document.getElementById(elementId);
        if (!input) {
            return;
        }

        input.click();
    }

    window.modViewer = {
        init,
        renderAvatar,
        addModAsset,
        removeModAsset,
        clearModAssets,
        getRenderState,
        clearIndexedDb,
        openFilePicker,
        storeHhhFiles,
        getStoredHhhFiles,
    };
})();
