(function () {
    const state = {
        renderer: null,
        scene: null,
        camera: null,
        frameHandle: null,
    };

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

        const placeholder = new THREE.Mesh(
            new THREE.CapsuleGeometry(0.35, 1.0, 6, 12),
            new THREE.MeshStandardMaterial({ color: 0x5b8cff, metalness: 0.1, roughness: 0.6 })
        );
        placeholder.position.y = 0.4;
        scene.add(placeholder);

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

            // Remove placeholder geometry
            const placeholders = state.scene.children.filter(obj => obj.geometry?.type === 'CapsuleGeometry');
            placeholders.forEach(obj => {
                state.scene.remove(obj);
                if (obj.geometry) obj.geometry.dispose();
                if (obj.material) obj.material.dispose();
            });

            // Create materials map
            const materials = new Map();
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

                // Add to scene
                state.scene.add(group);

                console.log(`Rendered ${meshCount} meshes`);
            }

            if (meshCount === 0) {
                console.warn("No meshes were rendered");
            }
        } catch (err) {
            console.error("Failed to render avatar:", err);
        }
    }

    window.modViewer = {
        init,
        renderAvatar,
    };
})();
