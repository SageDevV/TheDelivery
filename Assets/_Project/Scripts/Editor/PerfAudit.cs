using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TheDelivery.AI;
using TheDelivery.Core;
using TheDelivery.FX;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// AUDITORIA DE PERFORMANCE DO APARTMENT (Atos 3 e 4). Duas partes:
    /// <list type="number">
    /// <item><b>Inventário estático</b> (Temp/PerfAudit.pending): luzes/sombras, malhas e
    /// triângulos, texturas (memória e import), colisores, áudio, animators, partículas, URP e
    /// pós-processamento → Temp/PerfAudit/static.txt.</item>
    /// <item><b>Runtime</b> (GameScreenshotTool com prep "perfaudit"): roda cenários do Ato 3
    /// e do Ato 4 e faz A/B no Beat 7 (sem chuva, sem sombras, sem pós, sem killer) →
    /// Temp/PerfAudit/runtime.txt.</item>
    /// </list>
    /// Números do Editor têm overhead do próprio Editor: valem para COMPARAR, não como fps final.
    /// </summary>
    [InitializeOnLoad]
    public static class PerfAudit
    {
        private const string ScenePath = "Assets/Scenes/Apartment.unity";
        private const string MarkerPath = "Temp/PerfAudit.pending";
        private const string StaticReport = "Temp/PerfAudit/static.txt";
        private static double nextPoll;

        static PerfAudit()
        {
            EditorApplication.update += Poll;
            GameScreenshotTool.PlayReady += OnPlayReady;
        }

        [MenuItem("Tools/The Delivery/Auditoria de Performance - Inventário (Apartment)")]
        private static void Menu() => RunStatic();

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(MarkerPath)) return;
            File.Delete(MarkerPath);
            try { RunStatic(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        // =================================================================
        // 1. INVENTÁRIO ESTÁTICO
        // =================================================================

        private static void RunStatic()
        {
            Scene scene = EditorSceneManager.GetSceneByPath(ScenePath);
            bool opened = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            var sb = new StringBuilder();
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                T[] All<T>() where T : Component => roots.SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();

                // --- Render pipeline -------------------------------------------
                sb.AppendLine("##### PIPELINE / QUALIDADE");
                sb.AppendLine($"Qualidade ativa: {QualitySettings.names[QualitySettings.GetQualityLevel()]}  vSync={QualitySettings.vSyncCount}");
                if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                {
                    sb.AppendLine($"URP asset: {urp.name}");
                    sb.AppendLine($"  renderScale={urp.renderScale} HDR={urp.supportsHDR} MSAA={urp.msaaSampleCount}");
                    sb.AppendLine($"  shadowDistance={urp.shadowDistance} cascades={urp.shadowCascadeCount} mainShadowRes={urp.mainLightShadowmapResolution}");
                    sb.AppendLine($"  additionalLights max={urp.maxAdditionalLightsCount} shadowsAdicionais={urp.supportsAdditionalLightShadows} addShadowRes={urp.additionalLightsShadowmapResolution}");
                    sb.AppendLine($"  softShadows={urp.supportsSoftShadows} SRPBatcher={urp.useSRPBatcher}");
                    var so = new SerializedObject(urp);
                    var list = so.FindProperty("m_RendererDataList");
                    for (int i = 0; list != null && i < list.arraySize; i++)
                    {
                        var data = list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                        if (data == null) continue;
                        string rpath = data is UniversalRendererData urd ? $" renderingMode={urd.renderingMode} depthPriming={urd.depthPrimingMode}" : "";
                        sb.AppendLine($"  renderer[{i}] {data.name}{rpath}");
                        foreach (var f in data.rendererFeatures)
                            if (f != null) sb.AppendLine($"    feature: {f.name} ({f.GetType().Name}) ativo={f.isActive}");
                    }
                }
                sb.AppendLine($"Occlusion culling bakeado: {(StaticOcclusionCulling.umbraDataSize > 0 ? $"SIM ({StaticOcclusionCulling.umbraDataSize / 1024} KB)" : "NÃO")}");
                sb.AppendLine($"Lightmaps: {LightmapSettings.lightmaps.Length}  (modo GI: {Lightmapping.lightingDataAsset?.name ?? "sem LightingData"})");
                sb.AppendLine();

                // --- Volumes ---------------------------------------------------
                sb.AppendLine("##### PÓS-PROCESSAMENTO (Volumes)");
                foreach (Volume v in All<Volume>())
                {
                    var p = v.sharedProfile;
                    string comps = p == null ? "-" : string.Join(", ", p.components.Where(c => c.active).Select(c => c.GetType().Name));
                    sb.AppendLine($"  {Path(v.transform)} global={v.isGlobal} peso={v.weight} → {comps}");
                }
                sb.AppendLine();

                // --- Câmeras ---------------------------------------------------
                sb.AppendLine("##### CÂMERAS");
                foreach (Camera c in All<Camera>())
                {
                    var add = c.GetComponent<UniversalAdditionalCameraData>();
                    sb.AppendLine($"  {Path(c.transform)} far={c.farClipPlane} occlusionCulling={c.useOcclusionCulling} " +
                                  $"postFX={(add != null && add.renderPostProcessing)} AA={(add != null ? add.antialiasing.ToString() : "-")} shadows={(add != null && add.renderShadows)}");
                }
                sb.AppendLine();

                // --- Luzes -----------------------------------------------------
                Light[] lights = All<Light>();
                sb.AppendLine($"##### LUZES ({lights.Length})");
                foreach (var g in lights.GroupBy(l => $"{l.type}/{l.lightmapBakeType}/sombra={l.shadows}"))
                    sb.AppendLine($"  {g.Key,-40} x{g.Count()}  (ativas {g.Count(l => l.isActiveAndEnabled)})");
                sb.AppendLine("  Com sombra (realtime/mixed):");
                foreach (Light l in lights.Where(l => l.shadows != LightShadows.None && l.lightmapBakeType != LightmapBakeType.Baked))
                    sb.AppendLine($"    {Path(l.transform)}  {l.type} range={l.range:0.#} res={l.shadowResolution} ativa={l.isActiveAndEnabled}");
                sb.AppendLine();

                // --- Malhas ----------------------------------------------------
                MeshRenderer[] mrs = All<MeshRenderer>();
                SkinnedMeshRenderer[] smrs = All<SkinnedMeshRenderer>();
                var meshes = new Dictionary<Mesh, int>();
                long tris = 0; int shadowCasters = 0, staticBatch = 0, noLod = 0;
                foreach (MeshRenderer r in mrs)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    Mesh m = mf.sharedMesh;
                    meshes[m] = meshes.TryGetValue(m, out int k) ? k + 1 : 1;
                    tris += Tris(m);
                    if (r.shadowCastingMode != ShadowCastingMode.Off) shadowCasters++;
                    if (GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic)) staticBatch++;
                }
                foreach (SkinnedMeshRenderer s in smrs)
                    if (s.sharedMesh != null) { meshes[s.sharedMesh] = 1; tris += Tris(s.sharedMesh); }
                int lodGroups = All<LODGroup>().Length;
                long meshMem = meshes.Keys.Sum(m => Profiler.GetRuntimeMemorySizeLong(m));
                int readable = meshes.Keys.Count(m => m.isReadable);
                sb.AppendLine($"##### MALHAS");
                sb.AppendLine($"  MeshRenderers={mrs.Length} (projetam sombra={shadowCasters}, BatchingStatic={staticBatch})  Skinned={smrs.Length}  LODGroups={lodGroups}");
                sb.AppendLine($"  Malhas únicas={meshes.Count}  triângulos na cena (soma das instâncias)={tris / 1000}k  memória das malhas≈{meshMem / (1024 * 1024)} MB  Read/Write ligado={readable}");
                sb.AppendLine("  Top 12 malhas por triângulos x instâncias:");
                foreach (var kv in meshes.OrderByDescending(kv => Tris(kv.Key) * kv.Value).Take(45))
                    sb.AppendLine($"    {kv.Key.name,-40} {Tris(kv.Key) / 1000.0,8:0.0}k tris x{kv.Value}  {Profiler.GetRuntimeMemorySizeLong(kv.Key) / 1024} KB  rw={kv.Key.isReadable}  {AssetDatabase.GetAssetPath(kv.Key)}");
                sb.AppendLine("  Por raiz de ambiente (renderers / tris):");
                foreach (GameObject root in roots)
                    foreach (Transform child in root.transform)
                    {
                        var rs = child.GetComponentsInChildren<MeshRenderer>(true);
                        if (rs.Length < 5) continue;
                        long t = rs.Select(r => r.GetComponent<MeshFilter>()?.sharedMesh).Where(m => m != null).Sum(Tris);
                        sb.AppendLine($"    {root.name}/{child.name,-28} {rs.Length,5} renderers  {t / 1000,6}k tris");
                    }
                sb.AppendLine();

                // --- Materiais e texturas ---------------------------------------
                var mats = new HashSet<Material>();
                foreach (Renderer r in All<Renderer>())
                    foreach (Material m in r.sharedMaterials)
                        if (m != null) mats.Add(m);
                var texs = new HashSet<Texture>();
                foreach (Material m in mats)
                    foreach (int id in m.GetTexturePropertyNameIDs())
                    {
                        Texture t = m.GetTexture(id);
                        if (t != null) texs.Add(t);
                    }
                long texMem = texs.Sum(TexMem);
                sb.AppendLine($"##### MATERIAIS / TEXTURAS");
                sb.AppendLine($"  Materiais únicos={mats.Count}  shaders={string.Join(", ", mats.GroupBy(m => m.shader.name).OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key} x{g.Count()}"))}");
                sb.AppendLine($"  Instancing ligado em {mats.Count(m => m.enableInstancing)} materiais");
                sb.AppendLine($"  Texturas únicas={texs.Count}  memória estimada={texMem / (1024 * 1024)} MB");
                var tex2d = texs.OfType<Texture2D>().ToList();
                sb.AppendLine($"  Por resolução: " + string.Join("  ", tex2d.GroupBy(t => Mathf.Max(t.width, t.height)).OrderByDescending(g => g.Key).Select(g => $"{g.Key}px x{g.Count()}")));
                int uncompressed = 0, readTex = 0, noMip = 0;
                foreach (Texture2D t in tex2d)
                {
                    var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
                    if (imp == null) continue;
                    if (imp.textureCompression == TextureImporterCompression.Uncompressed) uncompressed++;
                    if (imp.isReadable) readTex++;
                    if (!imp.mipmapEnabled) noMip++;
                }
                sb.AppendLine($"  Sem compressão={uncompressed}  Read/Write={readTex}  sem mipmap={noMip}");
                sb.AppendLine("  Top 20 texturas por memória:");
                foreach (Texture t in texs.OrderByDescending(TexMem).Take(20))
                {
                    string path = AssetDatabase.GetAssetPath(t);
                    var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                    string impInfo = imp == null ? "" : $"max={imp.maxTextureSize} comp={imp.textureCompression} rw={imp.isReadable} tipo={imp.textureType}";
                    string fmt = t is Texture2D t2 ? t2.format.ToString() : t.GetType().Name;
                    sb.AppendLine($"    {TexMem(t) / (1024.0 * 1024):0.0} MB  {t.width}x{t.height} {fmt,-12} {impInfo}  {path}");
                }
                sb.AppendLine();

                // --- Física ----------------------------------------------------
                MeshCollider[] mcs = All<MeshCollider>();
                sb.AppendLine($"##### FÍSICA");
                sb.AppendLine($"  Colliders={All<Collider>().Length}  MeshColliders={mcs.Length} (não-convexos={mcs.Count(m => !m.convex)})  Rigidbodies={All<Rigidbody>().Length}");
                sb.AppendLine("  MeshColliders mais pesados:");
                foreach (MeshCollider mc in mcs.Where(m => m.sharedMesh != null).OrderByDescending(m => Tris(m.sharedMesh)).Take(8))
                    sb.AppendLine($"    {Path(mc.transform),-60} {Tris(mc.sharedMesh) / 1000.0:0.0}k tris");
                sb.AppendLine();

                // --- Animação / IA / partículas ----------------------------------
                sb.AppendLine("##### ANIMAÇÃO / IA / PARTÍCULAS");
                foreach (Animator a in All<Animator>())
                    sb.AppendLine($"  Animator {Path(a.transform)} culling={a.cullingMode} ativo={a.isActiveAndEnabled}");
                foreach (UnityEngine.AI.NavMeshAgent ag in All<UnityEngine.AI.NavMeshAgent>())
                    sb.AppendLine($"  NavMeshAgent {Path(ag.transform)} avoidance={ag.obstacleAvoidanceType}");
                foreach (ParticleSystem ps in All<ParticleSystem>())
                    sb.AppendLine($"  Partículas {Path(ps.transform)} max={ps.main.maxParticles} colisão={ps.collision.enabled}");
                sb.AppendLine();

                // --- Áudio -----------------------------------------------------
                AudioSource[] srcs = All<AudioSource>();
                sb.AppendLine($"##### ÁUDIO ({srcs.Length} AudioSources)");
                var clips = new HashSet<AudioClip>(srcs.Select(s => s.clip).Where(c => c != null));
                foreach (MonoBehaviour mb in All<MonoBehaviour>())
                {
                    if (mb == null) continue;
                    foreach (FieldInfo f in mb.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (f.FieldType == typeof(AudioClip) && f.GetValue(mb) is AudioClip c && c != null) clips.Add(c);
                        else if (f.FieldType == typeof(AudioClip[]) && f.GetValue(mb) is AudioClip[] arr) foreach (var c2 in arr) if (c2 != null) clips.Add(c2);
                    }
                }
                long audioMem = 0;
                foreach (AudioClip c in clips.OrderByDescending(c => c.length))
                {
                    var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(c)) as AudioImporter;
                    var s = imp?.defaultSampleSettings;
                    long mem = Profiler.GetRuntimeMemorySizeLong(c);
                    audioMem += mem;
                    sb.AppendLine($"  {c.length,6:0.0}s  {mem / 1024,6} KB  {(s.HasValue ? $"{s.Value.loadType}/{s.Value.compressionFormat}" : "?"),-30} {c.name}");
                }
                sb.AppendLine($"  Total ≈ {audioMem / (1024 * 1024)} MB em {clips.Count} clipes");
            }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StaticReport));
            File.WriteAllText(StaticReport, sb.ToString());
            Debug.Log($"[PerfAudit] Inventário salvo em {StaticReport}");
        }

        private static long Tris(Mesh m)
        {
            long n = 0;
            for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3;
            return n;
        }

        private static MethodInfo storageSize;
        private static long TexMem(Texture t)
        {
            if (storageSize == null)
                storageSize = typeof(Editor).Assembly.GetType("UnityEditor.TextureUtil")?
                    .GetMethod("GetStorageMemorySizeLong", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            try
            {
                if (storageSize != null) return (long)storageSize.Invoke(null, new object[] { t });
            }
            catch { }
            return Profiler.GetRuntimeMemorySizeLong(t);
        }

        private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;

        // =================================================================
        // 2. RUNTIME
        // =================================================================

        private static void OnPlayReady(string prep)
        {
            // "view:x,y,z,yaw,pitch" — põe o player nessa pose para a foto (antes/depois).
            if (prep.StartsWith("view:"))
            {
                float[] v = prep.Substring(5).Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                double when = EditorApplication.timeSinceStartup + 1.5;
                void Place()
                {
                    if (!EditorApplication.isPlaying) { EditorApplication.update -= Place; return; }
                    if (EditorApplication.timeSinceStartup < when) return;
                    EditorApplication.update -= Place;
                    var pc = Object.FindFirstObjectByType<PlayerController>();
                    if (pc == null) return;
                    pc.CanMove = false;
                    var cc = pc.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    pc.transform.SetPositionAndRotation(new Vector3(v[0], v[1], v[2]), Quaternion.Euler(0f, v[3], 0f));
                    if (cc != null) cc.enabled = true;
                    Transform holder = pc.transform.Find("CameraHolder");
                    if (holder != null) holder.localRotation = Quaternion.Euler(v[4], 0f, 0f);
                }
                EditorApplication.update += Place;
                return;
            }

            if (prep != "perfaudit") return;
            double at = EditorApplication.timeSinceStartup + 2.0;
            void Step()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Step; return; }
                if (EditorApplication.timeSinceStartup < at) return;
                EditorApplication.update -= Step;
                BuildScenarios();
            }
            EditorApplication.update += Step;
        }

        private static void BuildScenarios()
        {
            var act3 = Object.FindFirstObjectByType<Act3Director>(FindObjectsInactive.Include);
            var act4 = Object.FindFirstObjectByType<Act4Director>(FindObjectsInactive.Include);
            var runner = new GameObject("PerfAuditRunner").AddComponent<PerfAuditRunner>();

            // Ato 3: a escalada (fenômenos).
            runner.AddStep("Ato 3", () => Seq(
                Do(() => { if (act3 != null) act3.AdvanceToBeat(Act3Beat.Phenomena); }),
                runner.Measure("ATO 3 — Phenomena (luzes acesas, fenômenos)", settle: 3f)));

            // Ato 4: investigação (chuva já cai).
            runner.AddStep("Ato 4 Investigation", () => Seq(
                Do(() =>
                {
                    if (act3 != null) { act3.StopAllCoroutines(); act3.enabled = false; }
                    act4?.AdvanceToBeat(Act4Beat.Investigation);
                }),
                runner.Measure("ATO 4 — Investigation (chuva, killer ainda fora)", settle: 3f)));

            // Ato 4: clímax — FSM caçando + chuva + escondida no box (microfone, vinheta, barra).
            runner.AddStep("Ato 4 FinalHiding", () => Seq(
                Do(() =>
                {
                    act4?.AdvanceToBeat(Act4Beat.FinalHiding);
                }),
                Wait(7f), // folga do Beat 7 (graceBeforeHunt) até o killer voltar
                Do(() =>
                {
                    HideSpot box = Object.FindObjectsByType<HideSpot>(FindObjectsInactive.Exclude).FirstOrDefault(h => h.name.Contains("Shower"))
                                   ?? Object.FindFirstObjectByType<HideSpot>();
                    if (box != null && PlayerHiding.Instance != null) PlayerHiding.Instance.EnterHideSpot(box);
                    // Sem barulho real atrapalhando a medição: o sensor fica armado (mic ligado) mas mascarado.
                    MicrophoneNoiseSensor.MaskFor(600f);
                }),
                runner.Measure("ATO 4 — Beat 7 BASE (killer + chuva + escondida c/ microfone)", settle: 3f),

                // A/B 1: sem chuva.
                Do(() => SetRain(false)),
                runner.Measure("  A/B: SEM chuva (partículas + culling + som)"),
                Do(() => SetRain(true)),

                // A/B 2: sem sombras em tempo real.
                Do(() => SetShadows(false)),
                runner.Measure("  A/B: SEM sombras realtime"),
                Do(() => SetShadows(true)),

                // A/B 3: sem pós-processamento.
                Do(() => SetVolumes(false)),
                runner.Measure("  A/B: SEM pós-processamento"),
                Do(() => SetVolumes(true)),

                // A/B 4: sem o killer (FSM + animator + navmesh).
                Do(() => SetKiller(false)),
                runner.Measure("  A/B: SEM killer"),
                Do(() => SetKiller(true)),

                // A/B 5: sem o exterior (cidade vista pelas janelas).
                Do(() => SetExterior(false)),
                runner.Measure("  A/B: SEM Exterior (cidade)"),
                Do(() => SetExterior(true))));

            runner.Run();
            Debug.Log("[PerfAudit] Cenários de runtime iniciados.");
        }

        // --- Interruptores do A/B -------------------------------------------

        private static readonly Dictionary<Light, LightShadows> savedShadows = new Dictionary<Light, LightShadows>();

        private static void SetRain(bool on)
        {
            var rain = Object.FindFirstObjectByType<RainController>(FindObjectsInactive.Include);
            if (rain == null) return;
            rain.gameObject.SetActive(on);
            if (on) rain.SetRaining(true, instant: true);
        }

        private static void SetShadows(bool on)
        {
            if (!on)
            {
                savedShadows.Clear();
                foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
                {
                    savedShadows[l] = l.shadows;
                    l.shadows = LightShadows.None;
                }
            }
            else
            {
                foreach (var kv in savedShadows)
                    if (kv.Key != null) kv.Key.shadows = kv.Value;
            }
        }

        private static void SetVolumes(bool on)
        {
            foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
                v.enabled = on;
        }

        private static void SetKiller(bool on)
        {
            var ai = Object.FindFirstObjectByType<AntagonistAI>(FindObjectsInactive.Include);
            if (ai != null) ai.gameObject.SetActive(on);
            if (on && ai != null) ai.ResumeFromDirector();
        }

        private static void SetExterior(bool on)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Transform ext = root.transform.Find("Exterior");
                if (ext != null) ext.gameObject.SetActive(on);
            }
        }

        // --- Mini DSL de passos ---------------------------------------------

        private static IEnumerator Seq(params IEnumerator[] parts)
        {
            foreach (IEnumerator p in parts)
                yield return p;
        }

        private static IEnumerator Do(System.Action a)
        {
            try { a(); }
            catch (System.Exception e) { Debug.LogException(e); }
            yield break;
        }

        private static IEnumerator Wait(float s)
        {
            yield return new WaitForSecondsRealtime(s);
        }
    }
}
