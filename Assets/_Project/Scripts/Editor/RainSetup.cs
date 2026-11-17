using System.IO;
using System.Linq;
using TheDelivery.FX;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// CHUVA DO ATO 4 na cena Apartment, idempotente:
    /// _Environment/Weather (RainController)
    ///   ├─ RainParticles  (ParticleSystem: stretched billboard, World space; o RainController elimina
    ///   │                  as gotas dentro das colunas cobertas — uma por teto)
    ///   ├─ RainAudio      (AudioSource 2D + RainAudioSynth: chuva/trovão sintetizados)
    ///   └─ LightningLight (Light direcional azulada, desligada fora do clarão)
    /// e liga o campo <c>rain</c> do Act4Director.
    ///
    /// Automação: Temp/RainSetup.pending (conteúdo "rebuild" refaz o Weather).
    /// Vídeo: GameScreenshotTool com prep "rainvideo" (DemoFrameRecorder com áudio).
    /// </summary>
    [InitializeOnLoad]
    public static class RainSetup
    {
        private const string ApartmentPath = "Assets/Scenes/Apartment.unity";
        private const string MarkerPath = "Temp/RainSetup.pending";
        private const string WeatherName = "Weather";
        private const string ParticleMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";
        private const string Title = "Chuva do Ato 4";
        private const float IndoorMargin = 0.35f; // folga das colunas cobertas (espessura das paredes)

        private static bool interactive = true;
        private static double nextPoll;

        static RainSetup()
        {
            EditorApplication.update += AutoRun;
            EditorApplication.update += PollDebugStart;
            EditorApplication.update += PollTune;
            GameScreenshotTool.PlayReady += OnPlayReady;
        }

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
                bool rebuild = File.ReadAllText(MarkerPath).Contains("rebuild");
                if (Apply(false, rebuild))
                    File.Delete(MarkerPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RainSetup] Tentativa interrompida ({e.Message}); tento de novo.");
            }
        }

        [MenuItem("Tools/The Delivery/Chuva do Ato 4 (Apartment)")]
        private static void Run() => Apply(true, false);

        // Afinação do SOM: chuva lá FORA, abafada pelas paredes/vidro, sem ofuscar o ambiente.
        private const float TunedRainVolume = 0.3f, TunedCutoffHz = 650f, TunedDropVolume = 0.2f;

        [MenuItem("Tools/The Delivery/Chuva do Ato 4 - Afinar som (abafado, de fora)")]
        private static void MenuTune() => TuneSound();

        private static void PollTune()
        {
            const string marker = "Temp/RainTune.pending";
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(marker)) return;
            File.Delete(marker);
            try { TuneSound(); } catch (System.Exception e) { Debug.LogException(e); }
        }

        public static void TuneSound()
        {
            Scene scene = EditorSceneManager.GetSceneByPath(ApartmentPath);
            bool opened = false;
            if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(ApartmentPath, OpenSceneMode.Additive); opened = true; }
            try
            {
                RainController rc = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<RainController>(true)).FirstOrDefault(x => x != null);
                RainAudioSynth synth = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<RainAudioSynth>(true)).FirstOrDefault(x => x != null);
                if (rc == null || synth == null) { Debug.LogError("[RainSetup] Chuva não encontrada no Apartment."); return; }

                var so = new SerializedObject(rc);
                float oldVol = so.FindProperty("rainVolume").floatValue;
                so.FindProperty("rainVolume").floatValue = TunedRainVolume;
                so.ApplyModifiedPropertiesWithoutUndo();

                var ss = new SerializedObject(synth);
                float oldCut = ss.FindProperty("rainCutoffHz").floatValue, oldDrop = ss.FindProperty("dropVolume").floatValue;
                ss.FindProperty("rainCutoffHz").floatValue = TunedCutoffHz;
                ss.FindProperty("dropVolume").floatValue = TunedDropVolume;
                ss.ApplyModifiedPropertiesWithoutUndo();

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[RainSetup] Som da chuva afinado: volume {oldVol}->{TunedRainVolume}, corte {oldCut}->{TunedCutoffHz} Hz, gotas {oldDrop}->{TunedDropVolume}.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        public static bool Apply(bool interactiveMode, bool rebuild)
        {
            interactive = interactiveMode;
            Scene scene = EditorSceneManager.GetSceneByPath(ApartmentPath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ApartmentPath, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                Transform env = null;
                Act4Director director = null;
                foreach (GameObject r in scene.GetRootGameObjects())
                {
                    if (r.name == "_Environment") env = r.transform;
                    if (director == null) director = r.GetComponentInChildren<Act4Director>(true);
                }
                if (env == null || director == null)
                {
                    Fail("Não achei _Environment ou o Act4Director na cena Apartment.");
                    return false;
                }

                Transform existing = env.Find(WeatherName);
                if (existing != null && rebuild)
                {
                    Object.DestroyImmediate(existing.gameObject);
                    existing = null;
                }

                string status;
                RainController controller;
                if (existing == null)
                {
                    controller = Build(env);
                    status = "Weather criado";
                }
                else
                {
                    controller = existing.GetComponent<RainController>();
                    status = "Weather já existia";
                }

                var so = new SerializedObject(director);
                so.FindProperty("rain").objectReferenceValue = controller;
                so.ApplyModifiedPropertiesWithoutUndo();

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);

                Debug.Log($"[RainSetup] OK | {status} | Act4Director.rain ligado");
                if (interactive)
                    EditorUtility.DisplayDialog(Title, $"{status}.\nChove só no Ato 4 (o Act4Director liga em qualquer beat).\n" +
                        "Para testar a cena isolada: marque 'Rain On Start' no RainController.", "Ok");
                return true;
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static RainController Build(Transform env)
        {
            var weather = new GameObject(WeatherName);
            weather.transform.SetParent(env, false);
            var controller = weather.AddComponent<RainController>();

            // --- Partículas -----------------------------------------------------
            var pgo = new GameObject("RainParticles");
            pgo.transform.SetParent(weather.transform, false);
            var ps = pgo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5f;
            main.startLifetime = 2.6f; // do emissor (~25 m) até o chão a ~13,5 m/s
            main.startSpeed = 0f;
            main.startSize = 0.03f;
            main.startColor = new Color(0.80f, 0.85f, 0.95f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 20000;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = 0f; // o RainController controla (fade)

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(38f, 0.5f, 38f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.6f, -0.2f); // vento leve (forte faria a gota atravessar parede)
            vel.y = new ParticleSystem.MinMaxCurve(-15f, -12f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

            // Sem módulo de colisão: a física de partículas deixava gota rápida atravessar o teto.
            // O RainController elimina as gotas dentro das colunas cobertas (indoorVolumes).
            var col = ps.collision;
            col.enabled = false;

            var r = pgo.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 1f;
            r.velocityScale = 0.065f; // ~0,9 m de risco a 14 m/s
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterialPath);
            if (mat != null)
                r.sharedMaterial = mat;
            else
                Debug.LogWarning("[RainSetup] Material ParticlesUnlit do URP não encontrado; partículas sem material.");

            // --- Som ----------------------------------------------------------
            var ago = new GameObject("RainAudio");
            ago.transform.SetParent(weather.transform, false);
            var src = ago.AddComponent<AudioSource>();
            src.playOnAwake = true;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 1f;
            var synth = ago.AddComponent<RainAudioSynth>();

            // --- Relâmpago ----------------------------------------------------
            // Intensidade AUTORADA = pico do clarão (o RainController guarda e liga/desliga).
            var lights = new System.Collections.Generic.List<Light>();
            Color flashColor = new Color(0.72f, 0.80f, 1f);

            var lgo = new GameObject("LightningSky");
            lgo.transform.SetParent(weather.transform, false);
            lgo.transform.rotation = Quaternion.Euler(38f, 255f, 0f);
            var sky = lgo.AddComponent<Light>();
            sky.type = LightType.Directional;
            sky.color = flashColor;
            sky.intensity = 2.5f; // ilumina o exterior (prédios, rua) — dentro, as sombras seguram
            sky.shadows = LightShadows.Soft;
            sky.enabled = false;
            lights.Add(sky);

            // Uma luz por janela, DE FORA apontando para dentro: é o clarão entrando pela janela.
            int w = 0;
            foreach (Transform t in env.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Window" || t.GetComponent<Collider>() == null || t.parent == null)
                    continue;
                Bounds wb = t.GetComponent<Collider>().bounds;
                Bounds room = RoomBounds(t.parent);
                Vector3 outward = OutwardAxis(wb, room.center);

                var go = new GameObject($"LightningWindow_{t.parent.name}_{++w}");
                go.transform.SetParent(weather.transform, false);
                go.transform.position = wb.center + outward * 1.6f + Vector3.up * 0.9f;
                go.transform.rotation = Quaternion.LookRotation((-outward + Vector3.down * 0.45f).normalized, Vector3.up);
                var spot = go.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.color = flashColor;
                spot.spotAngle = 110f;
                spot.innerSpotAngle = 60f;
                spot.range = 9f;
                spot.intensity = 14f;
                spot.shadows = LightShadows.None;
                spot.enabled = false;
                lights.Add(spot);
            }

            // Colunas cobertas: a planta de cada teto (+ folga que cobre a espessura das paredes),
            // do piso até bem acima do prédio. O RainController elimina gota que entra nelas —
            // não chove dentro de casa, nem a chuva que "cai" no telhado aparece.
            var volumes = new System.Collections.Generic.List<Bounds>();
            foreach (MeshCollider mc in env.GetComponentsInChildren<MeshCollider>(true))
            {
                if (mc.name != "Ceiling")
                    continue;
                Bounds cb = mc.bounds;
                float bottom = cb.min.y - 4f;   // abaixo do piso do apartamento
                float top = cb.max.y + 60f;     // acima do emissor
                var v = new Bounds();
                v.SetMinMax(new Vector3(cb.min.x - IndoorMargin, bottom, cb.min.z - IndoorMargin),
                            new Vector3(cb.max.x + IndoorMargin, top, cb.max.z + IndoorMargin));
                volumes.Add(v);
            }
            int b = volumes.Count;

            var so = new SerializedObject(controller);
            SerializedProperty vp = so.FindProperty("indoorVolumes");
            vp.arraySize = volumes.Count;
            for (int i = 0; i < volumes.Count; i++)
                vp.GetArrayElementAtIndex(i).boundsValue = volumes[i];
            so.FindProperty("rainParticles").objectReferenceValue = ps;
            so.FindProperty("synth").objectReferenceValue = synth;
            so.FindProperty("clipSource").objectReferenceValue = src;
            SerializedProperty lp = so.FindProperty("lightningLights");
            lp.arraySize = lights.Count;
            for (int i = 0; i < lights.Count; i++)
                lp.GetArrayElementAtIndex(i).objectReferenceValue = lights[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[RainSetup] {w} luzes de janela, {b} colunas cobertas.");
            return controller;
        }

        private static Bounds RoomBounds(Transform room)
        {
            var rs = room.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0)
                return new Bounds(room.position, Vector3.one);
            Bounds bb = rs[0].bounds;
            foreach (Renderer r in rs) bb.Encapsulate(r.bounds);
            return bb;
        }

        /// <summary>Normal da janela apontando para FORA do cômodo (eixo mais fino do vidro).</summary>
        private static Vector3 OutwardAxis(Bounds window, Vector3 roomCenter)
        {
            Vector3 s = window.size;
            Vector3 axis = s.x <= s.z ? Vector3.right : Vector3.forward;
            float side = Vector3.Dot(window.center - roomCenter, axis);
            return side >= 0f ? axis : -axis;
        }

        // --- Vídeo de demonstração ------------------------------------------

        // --- Teste: início do Ato 4 via debug (autoStartForDebug) -------------

        /// <summary>
        /// Liga/desliga o autoStartForDebug do Act4Director na cena salva (para reproduzir o
        /// "Play direto no Ato 4"). Marcador Temp/RainDebugStart.pending: "on &lt;beat&gt;" ou "off".
        /// </summary>
        private static void PollDebugStart()
        {
            const string marker = "Temp/RainDebugStart.pending";
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(marker))
                return;
            string[] arg = File.ReadAllText(marker).Trim().Split(' ');
            File.Delete(marker);

            Scene scene = EditorSceneManager.GetSceneByPath(ApartmentPath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(ApartmentPath, OpenSceneMode.Additive); openedHere = true; }
            try
            {
                Act4Director d = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<Act4Director>(true)).FirstOrDefault(x => x != null);
                if (d == null) return;
                var so = new SerializedObject(d);
                bool on = arg[0] == "on";
                so.FindProperty("autoStartForDebug").boolValue = on;
                so.FindProperty("startBeat").intValue = on && arg.Length > 1 ? int.Parse(arg[1]) : 0;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[RainSetup] Act4Director autoStartForDebug={on} startBeat={so.FindProperty("startBeat").intValue}");
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void OnPlayReady(string prep)
        {
            if (prep == "rainduck")
            {
                // Roteiro: chuva só (0-2 s) -> tom 2D (2-4 s, deve abaixar) -> silêncio (4-7 s,
                // deve voltar) -> tom 3D a 40 m (7-9 s, inaudível: NÃO deve abaixar).
                double t0 = EditorApplication.timeSinceStartup + 1.5;
                bool startedRain = false, played2d = false, played3d = false;
                double nextLog = 0;
                var sb = new System.Text.StringBuilder();
                void Tick()
                {
                    if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                    double t = EditorApplication.timeSinceStartup - t0;
                    if (t < 0) return;
                    RainController rc = Object.FindFirstObjectByType<RainController>(FindObjectsInactive.Include);
                    if (rc == null) { EditorApplication.update -= Tick; return; }
                    if (!startedRain) { rc.gameObject.SetActive(true); rc.SetRaining(true, instant: true); startedRain = true; }
                    if (!played2d && t >= 2.0) { PlayTone(Vector3.zero, spatial: false); played2d = true; }
                    if (!played3d && t >= 7.0)
                    {
                        Camera cam = Camera.main;
                        Vector3 far = (cam != null ? cam.transform.position : Vector3.zero) + new Vector3(40f, 0f, 0f);
                        PlayTone(far, spatial: true);
                        played3d = true;
                    }
                    if (t >= nextLog) { sb.AppendLine($"{t:0.0}s={rc.DuckFactor:0.00} [{rc.DuckDebug}]"); nextLog = t + 0.5; }
                    if (t >= 9.5)
                    {
                        EditorApplication.update -= Tick;
                        Debug.Log("[RainSetup] DUCK " + sb);
                    }
                }
                EditorApplication.update += Tick;
                return;
            }

            if (prep == "raincheck")
            {
                double checkAt = EditorApplication.timeSinceStartup + 3.0;
                void Check()
                {
                    if (!EditorApplication.isPlaying) { EditorApplication.update -= Check; return; }
                    if (EditorApplication.timeSinceStartup < checkAt) return;
                    EditorApplication.update -= Check;
                    RainController rc = Object.FindFirstObjectByType<RainController>(FindObjectsInactive.Include);
                    ParticleSystem ps = rc != null ? rc.GetComponentInChildren<ParticleSystem>(true) : null;
                    var act4 = Object.FindFirstObjectByType<Act4Director>(FindObjectsInactive.Include);
                    Debug.Log($"[RainSetup] CHECK beat={act4?.CurrentBeat} raining={rc?.IsRaining} " +
                              $"ativo={rc?.gameObject.activeInHierarchy} gotas vivas={ps?.particleCount} " +
                              $"taxa={ps?.emission.rateOverTime.constant} tocando={ps?.isPlaying}");
                }
                EditorApplication.update += Check;
                return;
            }

            // "rainvideo" = janela da sala (de perto); "rainvideo_bedroom" = dentro do quarto,
            // olhando a janela de longe (mostra o cômodo e o relâmpago iluminando-o).
            if (prep != "rainvideo" && prep != "rainvideo_bedroom")
                return;
            bool bedroom = prep == "rainvideo_bedroom";

            double at = EditorApplication.timeSinceStartup + 1.5;
            void Step()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Step; return; }
                if (EditorApplication.timeSinceStartup < at) return;
                EditorApplication.update -= Step;

                RainController rain = Object.FindFirstObjectByType<RainController>(FindObjectsInactive.Include);
                PlayerController pc = Object.FindFirstObjectByType<PlayerController>();
                if (rain == null || pc == null)
                {
                    Debug.LogWarning("[RainSetup] Vídeo: faltou RainController ou PlayerController.");
                    return;
                }

                pc.CanMove = false;
                var cc = pc.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                if (bedroom)
                    // Fundo do quarto, olhando a janela (+Z) a ~3 m.
                    pc.transform.SetPositionAndRotation(new Vector3(-1.5f, 5.26f, 8.75f), Quaternion.Euler(0f, 0f, 0f));
                else
                    // Na janela da sala, olhando para fora (-X).
                    pc.transform.SetPositionAndRotation(new Vector3(-1.25f, 5.26f, 2.15f), Quaternion.Euler(0f, -90f, 0f));
                if (cc != null) cc.enabled = true;
                Transform holder = pc.transform.Find("CameraHolder");
                if (holder != null) holder.localRotation = Quaternion.Euler(bedroom ? -2f : 4f, 0f, 0f);

                rain.gameObject.SetActive(true);
                rain.SetRaining(true, instant: true);

                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                rec.Begin(duration: 14f, fps: 24, recordAudio: true);
                // Roteiro: relâmpago com luz acesa; depois "cai a energia" e outro relâmpago no escuro.
                rec.At(2.5f, () => rain.FlashNow());
                rec.At(6.5f, () =>
                {
                    foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                        if (l.type != LightType.Directional && !l.transform.IsChildOf(rain.transform)) l.enabled = false;
                });
                rec.At(8.5f, () => rain.FlashNow());
                Debug.Log("[RainSetup] Gravação da chuva iniciada.");
            }
            EditorApplication.update += Step;
        }

        /// <summary>Toca um tom de teste de 2 s (440 Hz) — 2D ou 3D na posição dada.</summary>
        private static void PlayTone(Vector3 position, bool spatial)
        {
            const int rate = 44100;
            var data = new float[rate * 2];
            for (int i = 0; i < data.Length; i++) data[i] = 0.4f * Mathf.Sin(2f * Mathf.PI * 440f * i / rate);
            AudioClip clip = AudioClip.Create("TestTone", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            var go = new GameObject(spatial ? "TestTone3D" : "TestTone2D");
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = spatial ? 1f : 0f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 1f;
            src.maxDistance = 30f;
            if (spatial)
            {
                src.clip = clip;
                src.Play();
            }
            else
            {
                // 2D via PlayOneShot numa fonte SEM clip — é assim que os diretores tocam efeitos.
                src.PlayOneShot(clip);
            }
            Object.Destroy(go, 2.2f);
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[RainSetup] {message}");
            if (interactive)
                EditorUtility.DisplayDialog(Title, message, "Ok");
        }
    }
}
