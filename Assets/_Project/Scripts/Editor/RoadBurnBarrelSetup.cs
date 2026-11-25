using System.Collections.Generic;
using System.IO;
using System.Text;
using TheDelivery.FX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// BARRIS DE FOGO nos becos da Road.
    /// <list type="number">
    /// <item>Modelos: <c>Blender/build_burnbarrel.py</c> → Models/BurnBarrel/BurnBarrel_A|B|C (tambor de
    /// 200 L com furos, cinza, brasas emissivas e tábuas carbonizadas; PBR assado).</item>
    /// <item>Fogo ANIMADO por partículas: chamas (flipbook 8×8 de línguas de fogo GERADO aqui, com
    /// turbulência em movimento), núcleo amarelo, faíscas esticadas pela velocidade e fumaça escura
    /// macia; luz pontual tremulando e o fogo do ElevenLabs em loop (<see cref="BurnBarrelFire"/>).</item>
    /// <item>Prefabs Prefabs/BurnBarrel_A|B|C e a raiz "BurnBarrels" na cena, com os barris dentro dos
    /// becos medidos pelo <see cref="RoadAlleyProbe"/> (encostados numa parede, a 2–6 m da calçada).</item>
    /// </list>
    /// IDEMPOTENTE. Automação: <c>Temp/RoadBurnBarrelSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadBurnBarrelSetup
    {
        private const string MarkerPath = "Temp/RoadBurnBarrelSetup.pending";
        private const string ScenePath = "Assets/Scenes/Road.unity";
        private const string ModelRoot = "Assets/_Project/Models/BurnBarrel";
        private const string PrefabDir = "Assets/_Project/Prefabs";
        private const string FxTexDir = "Assets/_Project/Textures/FX";
        private const string FxMatDir = "Assets/_Project/Materials/FX";
        private const string RootName = "BurnBarrels";
        internal static readonly string[] Variants = { "BurnBarrel_A", "BurnBarrel_B", "BurnBarrel_C" };
        private const float FireY = 0.80f; // entre a cama de brasas (0,62 m) e a boca (0,88 m)

        /// <summary>Becos (do RoadAlleyProbe): boca no meio da calçada, direção para dentro, e a que
        /// profundidades vão barris.</summary>
        private static readonly (string name, Vector3 mouth, Vector3 inward, float[] depths)[] Alleys =
        {
            ("B x145", new Vector3(145.0f, 0f, 364.8f), Vector3.back, new[] { 3.2f, 7.5f }),
            ("D x-89", new Vector3(-88.8f, 0f, 91.7f), Vector3.back, new[] { 3.6f }),
            ("D x-16", new Vector3(-16.3f, 0f, 91.7f), Vector3.back, new[] { 3.0f }),
            ("A z201", new Vector3(258.1f, 0f, 200.8f), Vector3.right, new[] { 3.4f, 6.5f }),
        };

        private static double nextPoll;
        static RoadBurnBarrelSetup() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;
            File.Delete(MarkerPath);
            Apply();
        }

        [MenuItem("Tools/The Delivery/Road - Barris de fogo nos becos")]
        public static void Apply()
        {
            AssetDatabase.Refresh();
            var report = new StringBuilder("[RoadBurnBarrelSetup] OK");
            Directory.CreateDirectory(FxTexDir);
            Directory.CreateDirectory(FxMatDir);
            var flameTex = SaveTexture($"{FxTexDir}/FireFlipbook.png", BuildFlameSheet());
            var smokeTex = SaveTexture($"{FxTexDir}/FireSmoke.png", BuildPuff(256, 7));
            var sparkTex = SaveTexture($"{FxTexDir}/FireSpark.png", BuildDot(64));
            Material flameMat = ParticleMaterial("FireFlame", flameTex, additive: true, new Color(1.7f, 1.7f, 1.7f, 1f));
            Material sparkMat = ParticleMaterial("FireSpark", sparkTex, additive: true, new Color(2.5f, 2.5f, 2.5f, 1f));
            Material smokeMat = ParticleMaterial("FireSmoke", smokeTex, additive: false, Color.white);

            var prefabs = new List<GameObject>();
            foreach (string v in Variants)
            {
                var p = BuildPrefab(v, flameMat, sparkMat, smokeMat, out string info);
                report.Append($"\n  {v}: {info}");
                if (p != null) prefabs.Add(p);
            }
            AssetDatabase.SaveAssets();
            if (prefabs.Count == 0) { Debug.LogError(report + "\n  nenhum modelo — rode o build_burnbarrel.py"); return; }

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            int placed = 0;
            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (root.name == RootName) Object.DestroyImmediate(root);
                Physics.SyncTransforms();
                var group = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(group, scene);
                var rng = new System.Random(4130);
                foreach (var (name, mouth, inward, depths) in Alleys)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, inward);
                    int k = 0;
                    foreach (float depth in depths)
                    {
                        Vector3 c = mouth + inward * depth + Vector3.up * 1.0f;
                        // paredes do beco dos dois lados; encosta na mais próxima (alterna se empatar)
                        float dl = Physics.Raycast(c, side, out RaycastHit hl, 12f, ~0, QueryTriggerInteraction.Ignore) ? hl.distance : 12f;
                        float dr = Physics.Raycast(c, -side, out RaycastHit hr, 12f, ~0, QueryTriggerInteraction.Ignore) ? hr.distance : 12f;
                        bool left = Mathf.Abs(dl - dr) < 0.5f ? (k % 2 == 0) : dl < dr;
                        float wall = left ? dl : dr;
                        Vector3 p = c + (left ? side : -side) * Mathf.Max(0f, wall - 0.75f);
                        // não cair em cima de outra coisa (lixo, caçamba): anda mais para dentro
                        for (int tries = 0; tries < 6; tries++)
                        {
                            var hits = Physics.OverlapCapsule(p + Vector3.up * 0.15f, p + Vector3.up * 0.8f, 0.33f, ~0, QueryTriggerInteraction.Ignore);
                            if (hits.Length == 0) break;
                            p += inward * 0.8f;
                        }
                        if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit g, 8f, ~0, QueryTriggerInteraction.Ignore))
                        { report.Append($"\n  {name} d={depth}: SEM CHÃO"); continue; }
                        p.y = g.point.y;
                        var prefab = prefabs[placed % prefabs.Count];
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                        go.name = $"{prefab.name}_{++placed:00}";
                        go.transform.SetParent(group.transform, false);
                        go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                        Physics.SyncTransforms();
                        report.Append($"\n  {go.name}: beco {name}, {depth:0.0} m para dentro, ({p.x:0.0},{p.y:0.00},{p.z:0.0}), parede a {wall:0.0} m (largura {dl + dr:0.0})");
                        k++;
                    }
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            Debug.Log(report + $"\n  {placed} barris na cena");
            foreach (string v in Variants)
            {
                PrefabPreviewRender.Render($"{PrefabDir}/{v}.prefab");
                PrefabPreviewRender.Render($"{PrefabDir}/{v}.prefab", front: true);
            }
        }

        // ------------------------------------------------------------------------------ prefab
        private static GameObject BuildPrefab(string name, Material flameMat, Material sparkMat, Material smokeMat, out string info)
        {
            string dir = $"{ModelRoot}/{name}";
            string modelPath = $"{dir}/{name}.fbx";
            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter imp) { info = "FBX ausente"; return null; }
            if (imp.materialImportMode != ModelImporterMaterialImportMode.None || imp.importAnimation || imp.animationType != ModelImporterAnimationType.None)
            {
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.SaveAndReimport();
            }
            Material mat = RoadCarModelsSetup.BuildMaterial(name, dir);
            mat.SetColor("_EmissionColor", new Color(1f, 1f, 1f) * 2.2f); // brasas mais vivas
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var root = new GameObject(name);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.transform.SetParent(root.transform, false);
                var b = new Bounds(root.transform.position, Vector3.zero);
                int tris = 0;
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    r.sharedMaterials = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)));
                    b.Encapsulate(r.bounds);
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                }
                var cap = root.AddComponent<CapsuleCollider>();
                cap.radius = 0.30f;
                cap.height = 0.90f;
                cap.center = new Vector3(0f, 0.45f, 0f);

                var fire = new GameObject("Fire");
                fire.transform.SetParent(root.transform, false);
                fire.transform.localPosition = new Vector3(0f, FireY, 0f);
                Flames(fire.transform, "Flames", flameMat, core: false);
                Flames(fire.transform, "FlameCore", flameMat, core: true);
                Sparks(fire.transform, sparkMat);
                Smoke(fire.transform, smokeMat);

                var lightGo = new GameObject("FireLight");
                lightGo.transform.SetParent(fire.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.55f, 0.2f);
                light.range = 7.5f;
                light.intensity = 2.4f;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;

                var src = fire.AddComponent<AudioSource>();
                src.playOnAwake = false;
                var bf = fire.AddComponent<BurnBarrelFire>();
                var so = new SerializedObject(bf);
                so.FindProperty("fireLight").objectReferenceValue = light;
                so.FindProperty("crackleClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/SoundEffects/BurnBarrelFire.wav");
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
                info = $"prefab ok | {b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00} m | {tris} tris";
                return AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{name}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ParticleSystem NewSystem(Transform parent, string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static AnimationCurve Curve(params float[] tv)
        {
            var c = new AnimationCurve();
            for (int i = 0; i < tv.Length; i += 2) c.AddKey(tv[i], tv[i + 1]);
            return c;
        }

        private static Gradient Grad((Color c, float t)[] cols, (float a, float t)[] alphas)
        {
            var g = new Gradient();
            g.SetKeys(System.Array.ConvertAll(cols, x => new GradientColorKey(x.c, x.t)),
                      System.Array.ConvertAll(alphas, x => new GradientAlphaKey(x.a, x.t)));
            return g;
        }

        private static void Flames(Transform parent, string name, Material mat, bool core)
        {
            var ps = NewSystem(parent, name, mat);
            var main = ps.main;
            main.loop = true;
            main.duration = 2f;
            main.prewarm = true;
            main.startLifetime = core ? new ParticleSystem.MinMaxCurve(0.35f, 0.55f) : new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            main.startSpeed = core ? new ParticleSystem.MinMaxCurve(0.25f, 0.5f) : new ParticleSystem.MinMaxCurve(0.4f, 0.75f);
            main.startSize = core ? new ParticleSystem.MinMaxCurve(0.28f, 0.40f) : new ParticleSystem.MinMaxCurve(0.40f, 0.62f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            main.startColor = Color.white;
            main.gravityModifier = core ? -0.05f : -0.12f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var em = ps.emission;
            em.rateOverTime = core ? 22f : 30f;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = core ? 4f : 9f;
            sh.radius = core ? 0.12f : 0.20f;
            sh.rotation = new Vector3(-90f, 0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = core
                ? Grad(new[] { (new Color(1f, 0.95f, 0.75f), 0f), (new Color(1f, 0.78f, 0.35f), 0.5f), (new Color(1f, 0.45f, 0.1f), 1f) },
                       new[] { (0f, 0f), (1f, 0.12f), (0.85f, 0.6f), (0f, 1f) })
                : Grad(new[] { (new Color(1f, 0.82f, 0.45f), 0f), (new Color(1f, 0.52f, 0.14f), 0.35f), (new Color(0.85f, 0.22f, 0.04f), 0.75f), (new Color(0.35f, 0.06f, 0.02f), 1f) },
                       new[] { (0f, 0f), (0.95f, 0.1f), (0.6f, 0.45f), (0f, 0.85f) });

            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, core ? Curve(0f, 0.7f, 0.4f, 1f, 1f, 0.5f) : Curve(0f, 0.55f, 0.3f, 1f, 1f, 0.35f));

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

            var nz = ps.noise;
            nz.enabled = true;
            nz.strength = core ? 0.15f : 0.3f;
            nz.frequency = 1.6f;
            nz.scrollSpeed = 1.2f;
            nz.octaveCount = 2;
            nz.damping = true;

            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = 8;
            tsa.numTilesY = 8;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0f, 1f, 1f));
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, core ? 0f : 0.15f);
            tsa.cycleCount = 1;

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            r.sortingFudge = core ? -2f : 0f;
            r.maxParticleSize = 2f;
        }

        private static void Sparks(Transform parent, Material mat)
        {
            var ps = NewSystem(parent, "Sparks", mat);
            var main = ps.main;
            main.loop = true;
            main.duration = 3f;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f), new Color(1f, 0.5f, 0.12f));
            main.gravityModifier = -0.04f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            var em = ps.emission;
            em.rateOverTime = 4f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0.6f, 3, 7, 1, 1.4f) { probability = 0.6f } });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 18f;
            sh.radius = 0.15f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Grad(new[] { (Color.white, 0f), (new Color(1f, 0.6f, 0.2f), 0.4f), (new Color(0.8f, 0.15f, 0.03f), 1f) },
                             new[] { (1f, 0f), (1f, 0.6f), (0f, 1f) });
            var nz = ps.noise;
            nz.enabled = true;
            nz.strength = 0.9f;
            nz.frequency = 2.2f;
            nz.scrollSpeed = 0.8f;
            var lv = ps.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.limit = 1.6f;
            lv.dampen = 0.08f;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 1.5f;
        }

        private static void Smoke(Transform parent, Material mat)
        {
            var ps = NewSystem(parent, "Smoke", mat);
            ps.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.10f, 0.09f, 0.085f), new Color(0.2f, 0.18f, 0.17f));
            main.gravityModifier = -0.03f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 50;
            var em = ps.emission;
            em.rateOverTime = 5f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 10f;
            sh.radius = 0.15f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Grad(new[] { (Color.white, 0f), (new Color(0.8f, 0.8f, 0.8f), 1f) },
                             new[] { (0f, 0f), (0.55f, 0.15f), (0.28f, 0.6f), (0f, 1f) });
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0.6f, 1f, 3.2f));
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
            var nz = ps.noise;
            nz.enabled = true;
            nz.strength = 0.25f;
            nz.frequency = 0.6f;
            nz.scrollSpeed = 0.3f;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortingFudge = 10f; // atrás das chamas
        }

        // ------------------------------------------------------------------------------ materiais
        private static Material ParticleMaterial(string name, Texture2D tex, bool additive, Color tint)
        {
            string path = $"{FxMatDir}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = (int)RenderQueue.Transparent;
            const float far = 0.25f;
            m.SetFloat("_SoftParticlesEnabled", 1f);
            m.SetFloat("_SoftParticlesNearFadeDistance", 0f);
            m.SetFloat("_SoftParticlesFarFadeDistance", far);
            m.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / far, 0f, 0f));
            m.EnableKeyword("_SOFTPARTICLES_ON");
            m.SetFloat("_CameraFadingEnabled", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------------------ texturas
        private static Texture2D SaveTexture(string path, Texture2D t)
        {
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private static float Fbm(float x, float y, int oct)
        {
            float s = 0f, a = 0.5f, f = 1f;
            for (int i = 0; i < oct; i++) { s += a * Mathf.PerlinNoise(x * f + 31.7f * i, y * f + 17.3f * i); a *= 0.5f; f *= 2.03f; }
            return s / (1f - Mathf.Pow(0.5f, oct));
        }

        /// <summary>64 quadros (8×8, 128 px) de UMA língua de fogo evoluindo no tempo: nasce larga e
        /// baixa, sobe, afina e se rasga em pontas, com a turbulência subindo. O quadro 0 é o canto
        /// de cima à esquerda (ordem do Texture Sheet Animation).</summary>
        private static Texture2D BuildFlameSheet()
        {
            const int cell = 128, tiles = 8, size = cell * tiles;
            var px = new Color[size * size];
            for (int f = 0; f < 64; f++)
            {
                float life = f / 63f;
                float tt = life * 2.2f;
                int ox = (f % tiles) * cell, oy = (tiles - 1 - f / tiles) * cell;
                float height = Mathf.Lerp(0.5f, 0.86f, Smooth(0f, 0.45f, life)); // a chama "estica"
                for (int y = 0; y < cell; y++)
                {
                    float v = y / (float)(cell - 1);
                    float vv = v / height;
                    for (int x = 0; x < cell; x++)
                    {
                        float u = x / (float)(cell - 1) * 2f - 1f;
                        float sway = (Fbm(vv * 2.2f - tt * 2.6f, 3.1f, 2) - 0.5f) * 0.45f * vv;
                        float nx = u - sway;
                        float w = 0.58f * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Clamp01(vv), 1.25f))) * (0.75f + 0.25f * (1f - vv));
                        w *= Mathf.Lerp(1f, 0.72f, life);
                        float body = 1f - Smooth(w * 0.35f, w + 0.0001f, Mathf.Abs(nx));
                        float turb = Fbm(nx * 3.2f + 5f, vv * 3.6f - tt * 4.5f, 4);
                        float i = body * (0.45f + 0.85f * turb) * Mathf.Pow(Mathf.Clamp01(1f - vv), 0.45f);
                        i *= Smooth(0f, 0.10f, v);
                        i = Mathf.Clamp01((i - 0.18f) / 0.62f);
                        // janela de borda: nada encosta no limite do quadro (senão sai chama com corte reto)
                        i *= (1f - Smooth(0.72f, 0.95f, Mathf.Abs(u))) * (1f - Smooth(0.80f, 0.97f, v));
                        // calor: núcleo branco-amarelado, borda alaranjada (o gradiente da partícula tinge por cima)
                        var c = new Color(1f, Mathf.Lerp(0.55f, 1f, i), Mathf.Lerp(0.25f, 0.9f, i * i), i);
                        px[(oy + y) * size + ox + x] = c;
                    }
                }
            }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true);
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static Texture2D BuildPuff(int size, int seed)
        {
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)(size - 1) * 2f - 1f, v = y / (float)(size - 1) * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float n = Fbm(u * 2.4f + seed, v * 2.4f - seed, 5);
                    float a = Mathf.Clamp01(1f - Smooth(0.25f, 1f, r + (n - 0.5f) * 0.45f)) * (0.55f + 0.45f * n);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true);
            t.SetPixels(px); t.Apply();
            return t;
        }

        private static Texture2D BuildDot(int size)
        {
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)(size - 1) * 2f - 1f, v = y / (float)(size - 1) * 2f - 1f;
                    float r2 = u * u + v * v;
                    float a = Mathf.Exp(-r2 * 9f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true);
            t.SetPixels(px); t.Apply();
            return t;
        }
    }
}
