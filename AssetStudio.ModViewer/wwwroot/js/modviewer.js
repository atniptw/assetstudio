(function () {
    const state = {
        renderer: null,
        scene: null,
        camera: null,
        frameHandle: null,
    };

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

        const render = () => {
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

            const oldAvatars = state.scene.children.filter(obj => obj.name === 'Avatar');
            oldAvatars.forEach((obj) => {
                state.scene.remove(obj);
                disposeObjectTree(obj);
            });

            // Create materials map
            const materials = new Map();
            const textures = new Map();

            if (avatarData.textures && Array.isArray(avatarData.textures)) {
                avatarData.textures.forEach((texData, idx) => {
                    try {
                        if (typeof texData.dataUrl !== 'string' || !texData.dataUrl.startsWith('data:image/')) {
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

                    const albedoIndex = typeof matData.albedoTextureIndex === 'number'
                        ? matData.albedoTextureIndex
                        : matData.textureIndex;
                    if (typeof albedoIndex === 'number' && textures.has(albedoIndex)) {
                        material.map = textures.get(albedoIndex);
                        material.map.colorSpace = THREE.SRGBColorSpace;
                        material.needsUpdate = true;
                    }

                    if (typeof matData.normalTextureIndex === 'number' && textures.has(matData.normalTextureIndex)) {
                        material.normalMap = textures.get(matData.normalTextureIndex).clone();
                        material.normalMap.colorSpace = THREE.NoColorSpace;
                        material.normalMap.flipY = false;
                        material.needsUpdate = true;
                    }

                    materials.set(idx, material);
                });
            }

            // Create meshes
            let meshCount = 0;
            if (avatarData.meshes && Array.isArray(avatarData.meshes)) {
                const group = new THREE.Group();
                group.name = "Avatar";

                avatarData.meshes.forEach((meshData, idx) => {
                    try {
                        const geometry = new THREE.BufferGeometry();

                        // Add vertices
                        if (meshData.vertices && meshData.vertices.length > 0) {
                            const vertices = new Float32Array(meshData.vertices);
                            geometry.setAttribute('position', new THREE.BufferAttribute(vertices, 3));
                        }

                        // Add normals or compute them
                        if (meshData.normals && meshData.normals.length > 0) {
                            const normals = new Float32Array(meshData.normals);
                            geometry.setAttribute('normal', new THREE.BufferAttribute(normals, 3));
                        } else if (meshData.vertices && meshData.vertices.length > 0) {
                            geometry.computeVertexNormals();
                        }

                        // Add UV coordinates
                        if (meshData.uv && meshData.uv.length > 0) {
                            const uvs = new Float32Array(meshData.uv);
                            geometry.setAttribute('uv', new THREE.BufferAttribute(uvs, 2));
                        }

                        // Add indices
                        if (meshData.indices && meshData.indices.length > 0) {
                            const indices = new Uint32Array(meshData.indices);
                            geometry.setIndex(new THREE.BufferAttribute(indices, 1));
                        }

                        // Get material for this mesh
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
                            mesh.quaternion.set(
                                meshData.rotation[0],
                                meshData.rotation[1],
                                meshData.rotation[2],
                                meshData.rotation[3]
                            );
                        }
                        if (Array.isArray(meshData.scale) && meshData.scale.length >= 3) {
                            mesh.scale.set(meshData.scale[0], meshData.scale[1], meshData.scale[2]);
                        }

                        group.add(mesh);

                        meshCount++;
                    } catch (err) {
                        console.warn(`Failed to create mesh ${idx}:`, err.message);
                    }
                });

                // Center and scale avatar
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

                // Add to scene
                state.scene.add(group);

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
                }

                console.log(`Rendered ${meshCount} meshes`);
            }

            if (meshCount === 0) {
                console.warn("No meshes were rendered");
            }
        } catch (err) {
            console.error("Failed to render avatar:", err);
        }
    }

    async function clearIndexedDb() {
        try {
            if (!window.indexedDB) {
                return;
            }

            if (typeof indexedDB.databases !== "function") {
                return;
            }

            const databases = await indexedDB.databases();
            const deletions = databases
                .filter((database) => database && typeof database.name === "string")
                .map((database) => new Promise((resolve) => {
                    const request = indexedDB.deleteDatabase(database.name);
                    request.onsuccess = () => resolve();
                    request.onerror = () => resolve();
                    request.onblocked = () => resolve();
                }));

            await Promise.all(deletions);
        } catch (err) {
            console.warn("IndexedDB clear failed", err);
        }
    }

    window.modViewer = {
        init,
        renderAvatar,
        clearIndexedDb,
    };
})();
