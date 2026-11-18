using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TheDelivery.FX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// POSTES DA RUA (Road): distribui postes StreetLight_01 pelas calçadas dos cinco trechos do
    /// percurso (A..E), alternando os lados da rua, cada um com luz Spot (sem sombra) e lente
    /// emissiva por cabeça, estalo ao acender e — em parte deles — zumbido. Tudo sob um GameObject
    /// "StreetLights" com a <see cref="StreetLightNetwork"/>, que acende os postes com o anoitecer.
    ///
    /// Guias medidas com o RoadSidewalkProbe (calçadas de 2,25–2,5 m). O poste fica a 0,3 m da guia,
    /// FORA das duas faixas dos pedestres (centro ±0,62 m). Cada ponto é validado: tem que ser
    /// calçada (raycast acima do asfalto — cruzamentos não têm calçada) inclusive 2 m antes e
    /// depois, e livre de obstáculos (prédio, prop, outro poste).
    /// IDEMPOTENTE: refaz o "StreetLights". Automação: <c>Temp/RoadStreetLightsSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadStreetLightsSetup
    {
        private const string ScenePath = "Assets/Scenes/Road.unity";
        private const string MarkerPath = "Temp/RoadStreetLightsSetup.pending";
        private const string PrefabPath = "Assets/_Project/Prefabs/StreetLight_01.prefab";
        private const string GlowMatPath = "Assets/_Project/Materials/StreetLampGlow.mat";
        private const string RootName = "StreetLights";
        private const float PoleScale = 1.92f;     // mesma do poste que já existia na Road (~7 m)
        private const float Spacing = 32f;          // por lado; o outro lado vem deslocado meio passo
        private const float CurbInset = 0.3f;

        // Trecho: eixo da rua (true = rua corre em z), faixa ao longo, guias (lado -1 e +1).
        private static readonly (string name, bool alongZ, float from, float to, float curbNeg, float curbPos)[] Segments =
        {
            ("A", true, 196f, 378f, 236.9f, 257.1f),
            ("B", false, -40f, 262f, 365.8f, 377.1f),
            ("C", true, 90f, 382f, -33.3f, -21.9f),
            ("D", false, -176f, -14f, 92.7f, 104.0f),
            ("E", true, 90f, 262f, -168.3f, -157.1f),
        };

        private static double nextPoll;

        static RoadStreetLightsSetup() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll)
                return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;
            try
            {
                if (Apply())
                    File.Delete(MarkerPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RoadStreetLightsSetup] Tentativa interrompida ({e.Message}); tento de novo.\n{e.StackTrace}");
            }
        }

        [MenuItem("Tools/The Delivery/Road - Postes de luz (distribuir)")]
        private static void Run() => Apply();

        public static bool Apply()
        {
            var missing = new StringBuilder();
            AssetDatabase.Refresh();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[RoadStreetLightsSetup] FALHOU | prefab {PrefabPath} não encontrado.");
                return false;
            }
            AudioClip hum = CafeteriaAudioSetup.LoadClip("RoadStreetLightHum", missing, spatial: true);
            AudioClip click = CafeteriaAudioSetup.LoadClip("RoadStreetLightOn", missing, spatial: true);
            Material glowMat = GlowMaterial();
            if (!LampHeads(prefab, out List<Vector3> heads, out float poleHeight))
            {
                Debug.LogError("[RoadStreetLightsSetup] FALHOU | não achei as cabeças do poste na malha.");
                return false;
            }

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (!scene.IsValid())
            {
                Debug.LogError("[RoadStreetLightsSetup] FALHOU | não abri a cena Road.");
                return false;
            }

            var report = new StringBuilder();
            int made = 0, rejSidewalk = 0, rejObstacle = 0;
            try
            {
                NightfallController nightfall = null;
                var existingPoles = new List<Vector3>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == RootName) { Object.DestroyImmediate(root); continue; }
                    nightfall ??= root.GetComponentInChildren<NightfallController>(true);
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name.StartsWith("StreetLight")) existingPoles.Add(t.position);
                }
                Physics.SyncTransforms();

                var lights = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(lights, scene);
                var poles = new List<StreetLightPole>();
                var rng = new System.Random(4130); // pedido nº 0413: distribuição estável entre execuções

                foreach (var (segName, alongZ, from, to, curbNeg, curbPos) in Segments)
                {
                    int segCount = 0;
                    foreach (int side in new[] { -1, 1 })
                    {
                        float curb = side < 0 ? curbNeg : curbPos;
                        float across = curb + side * CurbInset;
                        float start = from + (side < 0 ? 0f : Spacing * 0.5f);
                        for (float a = start; a <= to; a += Spacing)
                        {
                            Vector3 p = alongZ ? new Vector3(across, 0f, a) : new Vector3(a, 0f, across);
                            Vector3 along = alongZ ? Vector3.forward : Vector3.right;
                            if (!IsSidewalk(p, out float y) || !IsSidewalk(p + along * 2f, out _) || !IsSidewalk(p - along * 2f, out _))
                            { rejSidewalk++; continue; }
                            p.y = y;
                            if (Blocked(p) || existingPoles.Any(e => Vector2.Distance(new Vector2(e.x, e.z), new Vector2(p.x, p.z)) < 4f))
                            { rejObstacle++; continue; }

                            var pole = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                            pole.name = $"StreetLight_{segName}{(side < 0 ? "L" : "R")}_{segCount + 1:00}";
                            pole.transform.SetParent(lights.transform, false);
                            pole.transform.position = p;
                            // Braços (eixo Z da malha) atravessando a rua: uma cabeça sobre o asfalto.
                            pole.transform.rotation = Quaternion.Euler(0f, alongZ ? 90f : 0f, 0f);
                            pole.transform.localScale = Vector3.one * PoleScale;
                            poles.Add(BuildPole(pole, heads, poleHeight, glowMat, hum, click, rng, made));
                            existingPoles.Add(p);
                            segCount++;
                            made++;
                        }
                    }
                    report.Append($"\n  trecho {segName}: {segCount} postes");
                }

                var net = lights.AddComponent<StreetLightNetwork>();
                var nso = new SerializedObject(net);
                nso.FindProperty("nightfall").objectReferenceValue = nightfall;
                SerializedProperty arr = nso.FindProperty("poles");
                arr.arraySize = poles.Count;
                for (int i = 0; i < poles.Count; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = poles[i];
                nso.ApplyModifiedPropertiesWithoutUndo();
                report.Append($"\n  anoitecer: {(nightfall != null ? "ok" : "AUSENTE")}");

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            Debug.Log($"[RoadStreetLightsSetup] OK | {made} postes ({rejSidewalk} pontos sem calçada, {rejObstacle} obstruídos); " +
                      $"cabeças {heads.Count}, altura {poleHeight * PoleScale:0.0} m" + report +
                      (missing.Length > 0 ? "\n  Clipes inexistentes:\n" + missing : ""));
            return true;
        }

        private static StreetLightPole BuildPole(GameObject pole, List<Vector3> heads, float poleHeight, Material glowMat,
                                                 AudioClip hum, AudioClip click, System.Random rng, int index)
        {
            var lights = new List<Light>();
            var glows = new List<Renderer>();
            for (int i = 0; i < heads.Count; i++)
            {
                var lampGo = new GameObject($"Lamp_{i}");
                lampGo.transform.SetParent(pole.transform, false);
                lampGo.transform.localPosition = heads[i] + Vector3.down * 0.04f;
                lampGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // facho para baixo
                var l = lampGo.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = new Color(1f, 0.74f, 0.45f);  // vapor de sódio
                l.intensity = 16f; // a neblina da Road come muito: abaixo disso a poça de luz no chão some
                l.range = 19f;
                l.spotAngle = 128f;
                l.innerSpotAngle = 40f;
                l.shadows = LightShadows.None;
                l.enabled = false;
                lights.Add(l);

                // A lente acesa: um disco achatado e emissivo colado embaixo da cabeça.
                var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(glow.GetComponent<Collider>());
                glow.name = "Glow";
                glow.transform.SetParent(pole.transform, false);
                glow.transform.localPosition = heads[i] + Vector3.up * 0.01f;
                glow.transform.localScale = new Vector3(0.2f, 0.035f, 0.2f);
                var gr = glow.GetComponent<MeshRenderer>();
                gr.sharedMaterial = glowMat;
                gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                gr.enabled = false;
                glows.Add(gr);
            }

            var cap = pole.AddComponent<CapsuleCollider>();
            cap.direction = 1;
            cap.radius = 0.07f;
            cap.height = poleHeight;
            cap.center = new Vector3(0f, poleHeight * 0.5f, 0f);

            AudioSource MakeSource(AudioClip clip, float volume, bool loop, float min, float max, int priority)
            {
                var src = pole.AddComponent<AudioSource>();
                src.clip = clip;
                src.volume = volume;
                src.loop = loop;
                src.playOnAwake = false;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = min;
                src.maxDistance = max;
                src.priority = priority;
                src.dopplerLevel = 0f;
                return src;
            }
            // Zumbido só em 1 de cada 3 postes (as vozes de áudio são limitadas), curto e baixo.
            AudioSource humSrc = index % 3 == 0 && hum != null ? MakeSource(hum, 0.035f, true, 1.5f, 9f, 220) : null;
            AudioSource clickSrc = click != null ? MakeSource(click, 0.35f, false, 3f, 30f, 160) : null;

            var comp = pole.AddComponent<StreetLightPole>();
            var so = new SerializedObject(comp);
            SerializedProperty lp = so.FindProperty("lights");
            lp.arraySize = lights.Count;
            for (int i = 0; i < lights.Count; i++) lp.GetArrayElementAtIndex(i).objectReferenceValue = lights[i];
            SerializedProperty gp = so.FindProperty("glows");
            gp.arraySize = glows.Count;
            for (int i = 0; i < glows.Count; i++) gp.GetArrayElementAtIndex(i).objectReferenceValue = glows[i];
            so.FindProperty("hum").objectReferenceValue = humSrc;
            so.FindProperty("click").objectReferenceValue = clickSrc;
            // A rua vai pegando aos poucos: limiar entre 0,52 e 0,64 (o som "poste acendendo" é em 0,60).
            so.FindProperty("turnOnAt").floatValue = 0.52f + (float)rng.NextDouble() * 0.12f;
            so.FindProperty("igniteTime").floatValue = 0.8f + (float)rng.NextDouble() * 1.4f;
            so.FindProperty("faulty").boolValue = rng.NextDouble() < 0.12;
            so.ApplyModifiedPropertiesWithoutUndo();
            return comp;
        }

        /// <summary>
        /// Cabeças do poste, no espaço local do prefab: vértices do topo agrupados pelo lado do braço
        /// (eixo Z da malha); para cada grupo, o centro em XZ e o fundo da cúpula em Y.
        /// </summary>
        private static bool LampHeads(GameObject prefab, out List<Vector3> heads, out float height)
        {
            heads = new List<Vector3>();
            height = 0f;
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            Vector3[] v = mf.sharedMesh.vertices;
            if (v.Length == 0) return false;
            float minY = v.Min(p => p.y), maxY = v.Max(p => p.y);
            float h = maxY - minY;
            height = h;
            var top = v.Where(p => p.y > maxY - h * 0.12f).ToArray();
            foreach (int s in new[] { -1, 1 })
            {
                var g = top.Where(p => p.z * s > h * 0.08f).ToArray();
                if (g.Length == 0) continue;
                heads.Add(new Vector3(g.Average(p => p.x), g.Min(p => p.y), g.Average(p => p.z)));
            }
            return heads.Count > 0;
        }

        private static bool IsSidewalk(Vector3 p, out float y)
        {
            y = 0f;
            if (!Physics.Raycast(new Vector3(p.x, 6f, p.z), Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore))
                return false;
            y = hit.point.y;
            return hit.collider.name.Contains("Road_Surface") && hit.point.y >= 0.05f; // asfalto/cruzamento ≈ 0,02
        }

        private static bool Blocked(Vector3 p)
        {
            foreach (Collider c in Physics.OverlapCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 7.5f, 0.45f, ~0, QueryTriggerInteraction.Ignore))
            {
                string n = c.name;
                if (n.Contains("Road_Surface") || n.Contains("City_Ground") || n.Contains("StreetCollider"))
                    continue;
                Transform root = c.transform.root;
                if (root.name == "Traffic" || root.name == "Pedestrians" || root.name == RootName)
                    continue;
                return true;
            }
            return false;
        }

        private static Material GlowMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(GlowMatPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, GlowMatPath);
            }
            // HDR acima de 1: o bloom do pós pega a lente acesa.
            mat.SetColor("_BaseColor", new Color(1f, 0.74f, 0.45f) * 3.5f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
