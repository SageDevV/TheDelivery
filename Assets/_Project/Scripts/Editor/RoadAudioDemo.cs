using System.IO;
using System.Text;
using TheDelivery.FX;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// ROTEIRO DE VÍDEO "roadvideo" — o INTERLÚDIO inteiro, andado: Play isolado na Road
    /// (marcador <c>Temp/GameScreenshot.pending</c> = "900 Assets/Scenes/Road.unity roadvideo"),
    /// 4 s de aquecimento do gravador (cortados na montagem), e a Clear ANDA pela rua com um gamepad
    /// virtual seguindo a NavMesh: até o gatilho do assovio, até o portão do encontro (o diretor
    /// assume, ela vira, vai até o estranho, a conversa), e depois até o prédio — porta e corte para
    /// a Recepção. Trilha em Temp/DemoFrames/log.txt, com o progresso do anoitecer em cada marco.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadAudioDemo
    {
        private const float Duration = 300f;
        private static bool noSkips;

        static RoadAudioDemo() => GameScreenshotTool.PlayReady += OnPlayReady;

        /// <summary>
        /// "carshow": vitrine dos carros do tráfego (os 4 do Meshy + os BBW30). A Clear fica parada na
        /// calçada leste do trecho A olhando a rua; os 8 carros passam diante dela, um a cada 6 s,
        /// alternando as mãos (os novos primeiro). Os da volta são adiantados na pista até 55 m antes
        /// dela (a origem deles fica a ~900 m). 4 s de aquecimento.
        /// </summary>
        private static void CarShow()
        {
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var player = Object.FindAnyObjectByType<PlayerController>();
                var director = Object.FindAnyObjectByType<JourneyDirector>();
                var cars = Object.FindObjectsByType<StreetTrafficCar>(FindObjectsInactive.Exclude);
                if (player == null || cars.Length == 0 || director == null || director.CurrentBeat == JourneyBeat.None)
                    return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(62f, 24, true);
                rec.StartCoroutine(CarShowScript(player, cars));
            }
            EditorApplication.update += Tick;
        }

        private static readonly string[] CarShowOrder =
            { "Car_Ida_2", "Car_Volta_6", "Car_Ida_4", "Car_Volta_7", "Car_Ida_1", "Car_Volta_5", "Car_Ida_3", "Car_Volta_8" };

        private static System.Collections.IEnumerator CarShowScript(PlayerController player, StreetTrafficCar[] cars)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string text) { log.AppendLine($"{Time.time - t0:0.0}  {text}"); Debug.Log($"[RoadAudioDemo] {Time.time - t0:0.0}s {text}"); }
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var fWait = typeof(StreetTrafficCar).GetField("waitTimer", F);
            var fS = typeof(StreetTrafficCar).GetField("s", F);
            var fLane = typeof(StreetTrafficCar).GetField("lane", F);

            // Calçada leste do trecho A, 53 m depois da origem da ida (fora do spawnClearance), olhando a rua.
            Vector3 stand = new Vector3(256.6f, 0.5f, 305f);
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(stand, Quaternion.Euler(0f, 262f, 0f));
            if (cc != null) cc.enabled = true;

            // Entardecer adiantado (0,62): escuro o bastante para os faróis contarem, antes do toque de
            // recolher dos carros (0,7). O anoitecer fica parado aí durante a vitrine.
            var nightfall = Object.FindAnyObjectByType<NightfallController>();
            if (nightfall != null)
            {
                nightfall.Stop();
                typeof(NightfallController).GetMethod("Apply", F)?.Invoke(nightfall, new object[] { 0.62f });
                Ev($"anoitecer em {nightfall.Progress:0.00}");
            }

            // deixa os Start() dos carros rodarem (montam a pista e entram em espera)
            while (System.Array.Exists(cars, c => fLane.GetValue(c) == null)) yield return null;
            yield return null;
            var pending = new System.Collections.Generic.List<StreetTrafficCar>();
            for (int i = 0; i < CarShowOrder.Length; i++)
                foreach (var c in cars)
                    if (c.name == CarShowOrder[i])
                    {
                        fWait.SetValue(c, 2f + 6f * i);
                        pending.Add(c);
                        Ev($"{c.name} ({PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject)?.name}) larga em {2f + 6f * i:0}s");
                    }

            float logAt = 0f;
            while (Time.time - t0 < 60f)
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var c = pending[i];
                    if (!c.IsDriving) continue;
                    pending.RemoveAt(i);
                    var lane = (StreetRoute.LanePath)fLane.GetValue(c);
                    // ponto da pista mais perto da Clear; recua 55 m
                    float best = 0f, bestD = float.MaxValue;
                    for (float d = 0f; d < lane.Length; d += 1f)
                    {
                        lane.Evaluate(d, out Vector3 p, out _);
                        float dd = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(stand.x, stand.z));
                        if (dd < bestD) { bestD = dd; best = d; }
                    }
                    float s0 = Mathf.Max(0f, best - 55f);
                    if ((float)fS.GetValue(c) < s0) fS.SetValue(c, s0);
                    Ev($"{c.name} largou; s={s0:0} (Clear em {best:0} m da origem)");
                }
                // Olha para o carro que vem chegando (os faróis de frente); sem ninguém, olha a rua.
                StreetTrafficCar incoming = null; float bestDist = 75f;
                foreach (var c in cars)
                {
                    if (!c.IsDriving) continue;
                    Vector3 to = player.transform.position - c.transform.position; to.y = 0f;
                    float dist = to.magnitude;
                    if (dist < bestDist && Vector3.Dot(c.transform.forward, to) > 0f) { bestDist = dist; incoming = c; }
                }
                Quaternion want = Quaternion.Euler(0f, 262f, 0f);
                if (incoming != null)
                {
                    Vector3 look = incoming.transform.position - player.transform.position; look.y = 0f;
                    want = Quaternion.LookRotation(look);
                }
                player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, want, 90f * Time.deltaTime);

                if (Time.time - t0 >= logAt)
                {
                    logAt += 3f;
                    var sb = new StringBuilder();
                    foreach (var c in cars)
                        if (c.IsDriving) sb.Append($"{c.name}:({c.transform.position.x:0},{c.transform.position.z:0}) ");
                    Ev(sb.ToString());
                }
                yield return null;
            }
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "trafficvideo": teste do tráfego. A Clear é posta NA PISTA do primeiro carro da ida, de
        /// frente para ele: o carro freia, para antes dela e buzina; depois de alguns segundos ela
        /// sai da frente (anda para a calçada) e ele segue. Registra cada carro a cada 2 s.
        /// </summary>
        private static void TrafficVideo()
        {
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var player = Object.FindAnyObjectByType<PlayerController>();
                var director = Object.FindAnyObjectByType<JourneyDirector>();
                if (player == null || director == null || director.CurrentBeat == JourneyBeat.None)
                    return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(44f, 24, true);
                rec.StartCoroutine(TrafficScript(player));
            }
            EditorApplication.update += Tick;
        }

        private static System.Collections.IEnumerator TrafficScript(PlayerController player)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            void Ev(string text) { log.AppendLine($"{Time.time - t0:0.0}  {text}"); Debug.Log($"[RoadAudioDemo] {Time.time - t0:0.0}s {text}"); }
            var cars = Object.FindObjectsByType<StreetTrafficCar>(FindObjectsInactive.Exclude);
            Gamepad pad = InputSystem.AddDevice<Gamepad>("RoadTrafficGamepad");

            // Na pista da ida (x ≈ 249,2), 45 m à frente da origem dos carros, olhando para eles (sul).
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(249.2f, 0.5f, 300f), Quaternion.Euler(0f, 180f, 0f));
            if (cc != null) cc.enabled = true;
            Ev("Clear na pista da ida, de frente para o trânsito (aquecimento 4 s)");

            float stepAsideAt = 22f, logAt = 0f;
            bool aside = false;
            while (Time.time - t0 < 42f)
            {
                if (Time.time - t0 >= logAt)
                {
                    logAt += 2f;
                    var sb = new StringBuilder();
                    foreach (var c in cars)
                        sb.Append($"{c.name}:{(c.IsDriving ? "anda" : "espera")}{(c.IsYieldingToPlayer ? "/PARADO-P-ELA" : "")} buz={c.HornsPlayed} pos=({c.transform.position.x:0},{c.transform.position.z:0}) | ");
                    Ev(sb.ToString());
                }
                if (!aside && Time.time - t0 >= stepAsideAt)
                {
                    aside = true;
                    Ev("ela sai da frente (anda para a calçada, oeste)");
                }
                if (aside && player.transform.position.x > 239f)
                {
                    player.transform.rotation = Quaternion.Euler(0f, 270f, 0f);
                    InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0f, 1f) });
                }
                else
                {
                    if (aside) player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, Quaternion.Euler(0f, 120f, 0f), 120f * Time.deltaTime); // vira para a rua (leste-sul): vê o carro passar de lado
                    InputSystem.QueueStateEvent(pad, new GamepadState());
                }
                yield return null;
            }
            InputSystem.RemoveDevice(pad);
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "npcvideo": teste do pedestre NPC1. A Clear fica parada na rua junto à calçada leste e
        /// acompanha o NPC com o olhar enquanto ele desce a calçada e passa ao lado dela (40 s).
        /// </summary>
        private static void NpcVideo()
        {
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var player = Object.FindAnyObjectByType<PlayerController>();
                var npc = GameObject.Find("NPC1");
                if (player == null || npc == null)
                    return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(40f, 24, true);
                rec.StartCoroutine(NpcScript(player, npc.transform));
            }
            EditorApplication.update += Tick;
        }

        private static System.Collections.IEnumerator NpcScript(PlayerController player, Transform npc)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(252.5f, 0.5f, 326f), Quaternion.identity);
            if (cc != null) cc.enabled = true;
            float t0 = Time.time;
            var cam = Camera.main;
            while (Time.time - t0 < 39f)
            {
                // Corpo e câmera seguem o NPC (só no plano; a câmera olha um pouco para baixo, para o corpo dele).
                Vector3 to = npc.position - player.transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.5f && npc.gameObject.activeInHierarchy)
                    player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, Quaternion.LookRotation(to.normalized), 90f * Time.deltaTime);
                if (cam != null)
                    cam.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
                yield return null;
            }
        }

        /// <summary>
        /// "npcprobe": diagnóstico do NPC1 — a Clear para a 3 m de frente para ele (que fica parado,
        /// sem o Walker) e o log registra o Animator (clipe, tempo) e se os ossos se mexem. 12 s.
        /// </summary>
        private static void NpcProbe()
        {
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var player = Object.FindAnyObjectByType<PlayerController>();
                var npc = GameObject.Find("NPC1");
                if (player == null || npc == null)
                    return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(12f, 24, false);
                rec.StartCoroutine(NpcProbeScript(player, npc));
            }
            EditorApplication.update += Tick;
        }

        private static System.Collections.IEnumerator NpcProbeScript(PlayerController player, GameObject npc)
        {
            var sb = new StringBuilder();
            var walker = npc.GetComponent<TheDelivery.AI.AmbientWalker>();
            if (walker != null) walker.enabled = false;
            foreach (var r in npc.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            Vector3 p = npc.transform.position;
            npc.transform.rotation = Quaternion.Euler(0f, 270f, 0f); // de frente para a câmera (oeste)
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(p + new Vector3(-3f, 0.5f, 0f), Quaternion.Euler(0f, 90f, 0f));
            if (cc != null) cc.enabled = true;
            if (Camera.main != null) Camera.main.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);

            var anim = npc.GetComponentInChildren<Animator>();
            sb.AppendLine($"Animator: {(anim != null ? "sim" : "NÃO")} enabled={anim?.enabled} controller={anim?.runtimeAnimatorController?.name} avatar={anim?.avatar?.name} valid={anim?.avatar?.isValid} human={anim?.isHuman} culling={anim?.cullingMode} speed={anim?.speed}");
            var smr = npc.GetComponentInChildren<SkinnedMeshRenderer>();
            sb.AppendLine($"SkinnedMesh: {(smr != null ? smr.name : "NÃO")} root={smr?.rootBone?.name} bones={smr?.bones?.Length} mesh={smr?.sharedMesh?.name} v={smr?.sharedMesh?.vertexCount} uv={(smr?.sharedMesh != null ? smr.sharedMesh.uv.Length : 0)} mats={string.Join(",", System.Array.ConvertAll(smr != null ? smr.sharedMaterials : new Material[0], m => m == null ? "null" : m.name + "/" + m.shader.name + " tex=" + (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap").name : "-")))}");
            foreach (var r in npc.GetComponentsInChildren<Renderer>(true))
                sb.AppendLine($"  renderer {r.name} ({r.GetType().Name}) enabled={r.enabled}");
            Transform bone = smr != null && smr.bones != null && smr.bones.Length > 5 ? smr.bones[5] : null;
            Quaternion last = bone != null ? bone.localRotation : Quaternion.identity;
            float t0 = Time.time, next = 0f;
            while (Time.time - t0 < 11f)
            {
                if (Time.time - t0 >= next && anim != null)
                {
                    next += 1f;
                    var info = anim.GetCurrentAnimatorStateInfo(0);
                    var clips = anim.GetCurrentAnimatorClipInfo(0);
                    float delta = bone != null ? Quaternion.Angle(last, bone.localRotation) : -1f;
                    if (bone != null) last = bone.localRotation;
                    sb.AppendLine($"t={Time.time - t0:0.0} estado={info.shortNameHash} norm={info.normalizedTime:0.00} clipes={clips.Length} {(clips.Length > 0 ? clips[0].clip.name : "")} osso={bone?.name} giro={delta:0.0}°");
                }
                yield return null;
            }
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "npcprobe.txt"), sb.ToString());
        }

        /// <summary>
        /// "pedvideo": a Clear ANDA (sem correr) do começo pelo primeiro trecho e pela rua longa até a
        /// noite fechar — passa pelos pedestres e mostra que eles somem com o anoitecer. 4 s de
        /// aquecimento; registra a cada 5 s quantos pedestres seguem na rua.
        /// </summary>
        private static void PedVideo()
        {
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var director = Object.FindAnyObjectByType<JourneyDirector>();
                var player = Object.FindAnyObjectByType<PlayerController>();
                if (director == null || player == null || director.CurrentBeat == JourneyBeat.None)
                    return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(132f, 24, true);
                rec.StartCoroutine(PedScript(player, rec));
            }
            EditorApplication.update += Tick;
        }

        private static System.Collections.IEnumerator PedScript(PlayerController player, DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            var scape = Object.FindAnyObjectByType<NightfallSoundscape>();
            var peds = GameObject.Find("Pedestrians");
            void Ev(string text) { log.AppendLine($"{Time.time - t0:0.0}  {text} [anoitecer {(scape != null ? scape.CurrentProgress : 0f):0.00}]"); }
            rec.StartCoroutine(PedWatch(peds, Ev, t0));
            Gamepad pad = InputSystem.AddDevice<Gamepad>("PedDemoGamepad");
            yield return new WaitForSeconds(4f);
            Ev("CORTE_INICIO — caminhada (andando)");
            var t2 = GameObject.Find("Trigger_Thought_ActJourney-2");
            var t3 = GameObject.Find("Trigger_Thought_ActJourney-3");
            if (t2 != null) { yield return WalkTo(pad, player, t2.transform.position, 1.2f, 60f, false, () => false); Ev("primeira curva"); }
            if (t3 != null) yield return WalkTo(pad, player, t3.transform.position, 1.2f, 125f - (Time.time - t0), false, () => Time.time - t0 > 128f);
            Queue(pad, Vector2.zero);
            InputSystem.RemoveDevice(pad);
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        private static System.Collections.IEnumerator PedWatch(GameObject peds, System.Action<string> ev, float t0)
        {
            if (peds == null) yield break;
            float next = 0f;
            while (true)
            {
                if (Time.time - t0 >= next)
                {
                    next += 5f;
                    int active = 0, visible = 0;
                    foreach (Transform t in peds.transform)
                    {
                        if (!t.name.StartsWith("NPC") || t.name.EndsWith("_End")) continue;
                        if (t.gameObject.activeInHierarchy) active++;
                        var r = t.GetComponentInChildren<Renderer>();
                        if (t.gameObject.activeInHierarchy && r != null && r.enabled) visible++;
                    }
                    ev($"PEDESTRES na rua: {active} (visíveis agora: {visible})");
                }
                yield return null;
            }
        }

        /// <summary>
        /// "pedprobe": diagnóstico dos pedestres. A Clear fica parada no trecho B (rua longa), olhando
        /// para o oeste, e a cada 1 s o log registra cada NPC: posição, renderer ligado, tamanho do
        /// bounds e se está no campo de visão da câmera. Grava 24 s de quadros.
        /// </summary>
        private static void PedProbe()
        {
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var player = Object.FindAnyObjectByType<PlayerController>();
                var peds = GameObject.Find("Pedestrians");
                if (player == null || peds == null)
                    return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(24f, 24, false);
                rec.StartCoroutine(PedProbeScript(player, peds));
            }
            EditorApplication.update += Tick;
        }

        private static System.Collections.IEnumerator PedProbeScript(PlayerController player, GameObject peds)
        {
            var sb = new StringBuilder();
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(200f, 0.5f, 372f), Quaternion.Euler(0f, 270f, 0f));
            if (cc != null) cc.enabled = true;
            float t0 = Time.time, next = 0f;
            while (Time.time - t0 < 23f)
            {
                if (Time.time - t0 >= next)
                {
                    next += 1f;
                    var cam = Camera.main;
                    Plane[] planes = cam != null ? GeometryUtility.CalculateFrustumPlanes(cam) : null;
                    sb.AppendLine($"t={Time.time - t0:0.0} player={player.transform.position} cam={(cam != null ? cam.transform.position + " fwd " + cam.transform.forward : "-")}");
                    foreach (Transform t in peds.transform)
                    {
                        if (!t.name.StartsWith("NPC") || t.name.EndsWith("_End")) continue;
                        var r = t.GetComponentInChildren<Renderer>(true);
                        string info = r == null ? "sem renderer" :
                            $"ren={r.enabled} b.center={r.bounds.center} b.size={r.bounds.size} naTela={(planes != null && GeometryUtility.TestPlanesAABB(planes, r.bounds))}";
                        sb.AppendLine($"   {t.name} ativo={t.gameObject.activeInHierarchy} pos={t.position} scale={t.lossyScale} {info}");
                    }
                }
                yield return null;
            }
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "pedprobe.txt"), sb.ToString());
        }

        /// <summary>
        /// "pedshow": vitrine dos 6 pedestres. Para cada um, o NPC é adiantado para um ponto do próprio
        /// trajeto e a Clear fica parada na beira da pista, ~12 m à frente dele, acompanhando-o com o
        /// olhar enquanto ele passa ao lado dela (9 s por NPC). Depois ela fica no trecho B olhando a
        /// calçada até a noite fechar — a rua esvazia (toque de recolher). 4 s de aquecimento.
        /// </summary>
        private static void PedShow()
        {
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var player = Object.FindAnyObjectByType<PlayerController>();
                var peds = GameObject.Find("Pedestrians");
                if (player == null || peds == null)
                    return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(132f, 24, true);
                rec.StartCoroutine(PedShowScript(player, peds, rec));
            }
            EditorApplication.update += Tick;
        }

        // (NPC, ponto do trajeto onde ele é adiantado, lado da pista em relação à calçada dele)
        private static readonly (string npc, Vector3 at, Vector3 toRoad)[] PedShots =
        {
            ("NPC7", new Vector3(235.13f, 0f, 330f), Vector3.right), // o mendigo, calçada oeste do trecho A
            ("NPC6", new Vector3(256.8f, 0f, 310f), Vector3.left),
            ("NPC1", new Vector3(256.8f, 0f, 340f), Vector3.left),
            ("NPC2", new Vector3(203f, 0f, 378.8f), Vector3.back),
            ("NPC5", new Vector3(150f, 0f, 365.0f), Vector3.forward),
            ("NPC3", new Vector3(120f, 0f, 365.0f), Vector3.forward),
            ("NPC4", new Vector3(92f, 0f, 378.8f), Vector3.back),
        };

        // (NPC A, NPC B, ponto de encontro, lado da pista, deslocamento da câmera ao longo da calçada)
        private static readonly (string a, string b, Vector3 meet, Vector3 toRoad, Vector3 along)[] PedCrossings =
        {
            ("NPC1", "NPC6", new Vector3(258.5f, 0f, 335f), Vector3.left, Vector3.back),
            ("NPC3", "NPC5", new Vector3(140f, 0f, 364.5f), Vector3.forward, Vector3.left),
        };

        /// <summary>
        /// Põe o NPC na própria reta (faixa), a <paramref name="seconds"/> de caminhada ANTES do ponto
        /// <paramref name="meet"/> projetado nela, e garante que ele esteja andando e visível (se estava
        /// esperando para renascer, o AmbientWalker o prenderia no fim do trajeto).
        /// </summary>
        internal static bool PutOnLine(GameObject peds, Transform npc, Vector3 meet, float seconds, out Vector3 meetOnLine)
        {
            meetOnLine = Vector3.zero;
            Transform end = npc != null ? peds.transform.Find(npc.name + "_End") : null;
            var walker = npc != null ? npc.GetComponent<TheDelivery.AI.AmbientWalker>() : null;
            if (npc == null || end == null || walker == null || !npc.gameObject.activeInHierarchy)
                return false;
            Vector3 dir = end.position - npc.position; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) // parado no fim: a reta vem da origem guardada no walker
            {
                var originField = typeof(TheDelivery.AI.AmbientWalker).GetField("origin",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                dir = end.position - (Vector3)originField.GetValue(walker); dir.y = 0f;
            }
            dir.Normalize();
            meetOnLine = end.position + Vector3.Project(new Vector3(meet.x, end.position.y, meet.z) - end.position, dir);
            meetOnLine.y = npc.position.y;
            float speed = new SerializedObject(walker).FindProperty("walkSpeed").floatValue;
            typeof(TheDelivery.AI.AmbientWalker).GetMethod("StartWalking",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(walker, null);
            npc.position = meetOnLine - dir * (speed * seconds);
            return true;
        }

        private static System.Collections.IEnumerator PedShowScript(PlayerController player, GameObject peds, DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            var scape = Object.FindAnyObjectByType<NightfallSoundscape>();
            void Ev(string text) { log.AppendLine($"{Time.time - t0:0.0}  {text} [anoitecer {(scape != null ? scape.CurrentProgress : 0f):0.00}]"); }
            rec.StartCoroutine(PedWatch(peds, Ev, t0));
            rec.StartCoroutine(PairWatch(peds, Ev));
            var cc = player.GetComponent<CharacterController>();
            var cam = Camera.main;

            void Teleport(Vector3 pos, Quaternion rot)
            {
                if (cc != null) cc.enabled = false;
                player.transform.SetPositionAndRotation(pos, rot);
                if (cc != null) cc.enabled = true;
            }

            // Parada inicial, fora do caminho de todos, durante o aquecimento.
            Teleport(new Vector3(247f, 0.5f, 270f), Quaternion.identity);
            yield return new WaitForSeconds(4f);
            Ev("CORTE_INICIO");

            // CRUZAMENTOS: os dois pares cujos trajetos se sobrepõem na mesma calçada, em sentidos opostos,
            // são postos para se encontrar 5 s depois do corte bem na frente da câmera (fixa, na guia):
            // cada um na sua faixa, passam lado a lado sem se tocar.
            foreach (var (nameA, nameB, meet, toRoad, along) in PedCrossings)
            {
                Transform a = peds.transform.Find(nameA), b = peds.transform.Find(nameB);
                if (!PutOnLine(peds, a, meet, 5f, out Vector3 meetA) || !PutOnLine(peds, b, meet, 5f, out _))
                {
                    Ev($"cruzamento {nameA}x{nameB}: indisponível, pulado");
                    continue;
                }
                Vector3 stand = meetA + toRoad * 2.4f + along * 4f;
                stand.y = 0.5f;
                Vector3 look = meetA - stand; look.y = 0f;
                Teleport(stand, Quaternion.LookRotation(look.normalized));
                Ev($"cruzamento {nameA} x {nameB} (faixas opostas)");
                float s0 = Time.time;
                while (Time.time - s0 < 10f)
                {
                    if (cam != null) cam.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
                    yield return null;
                }
            }

            foreach (var (name, at, toRoad) in PedShots)
            {
                Transform npc = peds.transform.Find(name);
                var walker = npc != null ? npc.GetComponent<TheDelivery.AI.AmbientWalker>() : null;
                Transform end = peds.transform.Find(name + "_End");
                if (npc == null || walker == null || end == null || !npc.gameObject.activeInHierarchy)
                {
                    Ev($"{name}: indisponível, pulado");
                    continue;
                }
                // O ponto da tomada é projetado na RETA do próprio NPC: ele continua na faixa dele.
                PutOnLine(peds, npc, at, 0f, out Vector3 npcPos);
                Vector3 dir = end.position - npcPos; dir.y = 0f; dir.Normalize();
                // Distância pela velocidade do NPC: ele passa pela Clear ~6,5 s depois do corte (sobram
                // ~2,5 s para ela acompanhá-lo indo embora). Na guia, fora da faixa dos carros.
                float speed = new SerializedObject(walker).FindProperty("walkSpeed").floatValue;
                float ahead = Mathf.Max(speed * 6.5f, 4f); // o mendigo (0,5 m/s) não fica colado na câmera
                float shotLength = Mathf.Max(9f, ahead / speed + 2.5f);
                Vector3 stand = npcPos + dir * ahead + toRoad * 1.8f;
                stand.y = 0.5f;
                Vector3 look = npcPos - stand; look.y = 0f;
                Teleport(stand, Quaternion.LookRotation(look.normalized));
                Ev($"{name} vindo pela calçada");
                float s0 = Time.time;
                while (Time.time - s0 < shotLength)
                {
                    Vector3 to = npc.position - player.transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 0.3f)
                        player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation,
                            Quaternion.LookRotation(to.normalized), 120f * Time.deltaTime);
                    if (cam != null) cam.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
                    yield return null;
                }
            }

            // A rua esvaziando: parada no trecho B, olhando a calçada para o oeste, até a noite fechar.
            Teleport(new Vector3(232f, 0.5f, 377.0f), Quaternion.Euler(0f, 266f, 0f)); // na guia norte, fora da faixa dos carros
            Ev("trecho B — esperando a noite");
            while (Time.time - t0 < 126f && (scape == null || scape.CurrentProgress < 0.995f))
            {
                if (cam != null) cam.transform.localRotation = Quaternion.Euler(3f, 0f, 0f);
                yield return null;
            }
            Ev("CORTE_FIM");
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// Esbarrões: toda frame mede a menor distância horizontal entre dois pedestres VISÍVEIS e, a
        /// cada 5 s, registra o mínimo da janela (e qual par) — evidência de que ninguém se esbarra.
        /// </summary>
        private static System.Collections.IEnumerator PairWatch(GameObject peds, System.Action<string> ev)
        {
            if (peds == null) yield break;
            var npcs = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in peds.transform)
                if (t.name.StartsWith("NPC") && !t.name.EndsWith("_End")) npcs.Add(t);
            float windowEnd = Time.time + 5f, best = float.MaxValue, overall = float.MaxValue;
            string pair = "-";
            while (true)
            {
                for (int i = 0; i < npcs.Count; i++)
                for (int j = i + 1; j < npcs.Count; j++)
                {
                    Transform a = npcs[i], b = npcs[j];
                    if (!a.gameObject.activeInHierarchy || !b.gameObject.activeInHierarchy) continue;
                    var ra = a.GetComponentInChildren<Renderer>(); var rb = b.GetComponentInChildren<Renderer>();
                    if (ra == null || rb == null || !ra.enabled || !rb.enabled) continue;
                    Vector3 d = a.position - b.position; d.y = 0f;
                    if (d.magnitude < best) { best = d.magnitude; pair = $"{a.name}x{b.name}"; }
                }
                if (Time.time >= windowEnd)
                {
                    if (best < float.MaxValue)
                    {
                        overall = Mathf.Min(overall, best);
                        ev($"menor distância entre pedestres: {best:0.00} m ({pair}) | mínimo geral {overall:0.00} m");
                    }
                    best = float.MaxValue; pair = "-"; windowEnd = Time.time + 5f;
                }
                yield return null;
            }
        }

        private static void OnPlayReady(string prep)
        {
            if (prep == "pedshow")
            {
                PedShow();
                return;
            }
            if (prep == "pedprobe")
            {
                PedProbe();
                return;
            }
            if (prep == "pedvideo")
            {
                PedVideo();
                return;
            }
            if (prep == "npcprobe")
            {
                NpcProbe();
                return;
            }
            if (prep == "npcvideo")
            {
                NpcVideo();
                return;
            }
            if (prep == "carshow")
            {
                CarShow();
                return;
            }
            if (prep == "trafficvideo")
            {
                TrafficVideo();
                return;
            }
            // "roadvideofull": o mesmo percurso SEM os saltos de corte — a jornada inteira, para validar.
            noSkips = prep == "roadvideofull";
            if (prep != "roadvideo" && prep != "roadvideofull")
                return;
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                var director = Object.FindAnyObjectByType<JourneyDirector>();
                var player = Object.FindAnyObjectByType<PlayerController>();
                if (director == null || player == null || director.CurrentBeat == JourneyBeat.None)
                    return;
                EditorApplication.update -= Tick;
                var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(noSkips ? 420f : Duration, 24, true);
                rec.StartCoroutine(Script(director, player));
            }
            EditorApplication.update += Tick;
        }

        /// <summary>Registra cada evento do tráfego: largada, sumiço, passagem com som, parada por ela, buzinada.</summary>
        private static System.Collections.IEnumerator WatchCars(System.Action<string> ev)
        {
            var cars = Object.FindObjectsByType<StreetTrafficCar>(FindObjectsInactive.Exclude);
            var driving = new bool[cars.Length];
            var yielding = new bool[cars.Length];
            var horns = new int[cars.Length];
            var passes = new int[cars.Length];
            while (true)
            {
                for (int i = 0; i < cars.Length; i++)
                {
                    var c = cars[i];
                    if (c == null) continue;
                    if (c.IsDriving != driving[i]) { driving[i] = c.IsDriving; ev($"TRÁFEGO {c.name} {(c.IsDriving ? "larga" : "some")}"); }
                    if (c.IsYieldingToPlayer != yielding[i]) { yielding[i] = c.IsYieldingToPlayer; ev($"TRÁFEGO {c.name} {(c.IsYieldingToPlayer ? "PARA por causa dela" : "segue")}"); }
                    if (c.HornsPlayed != horns[i]) { horns[i] = c.HornsPlayed; ev($"TRÁFEGO {c.name} BUZINA ({c.HornsPlayed})"); }
                    if (c.PassesPlayed != passes[i]) { passes[i] = c.PassesPlayed; ev($"TRÁFEGO {c.name} passa (som)"); }
                }
                yield return null;
            }
        }

        private static System.Collections.IEnumerator Script(JourneyDirector director, PlayerController player)
        {
            var log = new StringBuilder();
            float t0 = Time.time;
            var scape = Object.FindAnyObjectByType<NightfallSoundscape>();
            void Ev(string text)
            {
                string p = scape != null ? $" [anoitecer {scape.CurrentProgress:0.00}]" : "";
                log.AppendLine($"{Time.time - t0:0.0}  {text}{p}");
                Debug.Log($"[RoadAudioDemo] {Time.time - t0:0.0}s {text}{p}");
            }
            void Save()
            {
                Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
            }
            player.StartCoroutine(WatchCars(Ev));

            Gamepad pad = InputSystem.AddDevice<Gamepad>("RoadDemoGamepad");
            var trigger = GetPrivate<Transform>(director, "leaningTriggerPoint");
            var notice = GetPrivate<Transform>(director, "leaningNoticePoint");
            var destination = GetPrivate<Transform>(director, "destinationPoint");

            Ev("gravando; aquecimento de 4 s (cortado na montagem)");
            yield return new WaitForSeconds(4f);
            Ev("CORTE_INICIO — caminhada");

            // A ROTA: a NavMesh da Road não cobre a rua inteira, mas os gatilhos de pensamento
            // ("esquerda", "direita") ficam exatamente nas curvas — são os pontos de passagem. Corre
            // nos trechos longos (a rua tem ~750 m) e anda no último trecho antes do estranho.
            Transform Named(string n) { var go = GameObject.Find(n); return go != null ? go.transform : null; }
            (Transform point, bool sprint, string label)[] route =
            {
                (Named("Trigger_Thought_ActJourney-2"), true, "primeira curva (pensamento 2)"),
                (Named("Trigger_Thought_ActJourney-3"), true, "segunda curva (pensamento 3)"),
                (Named("Trigger_Thought_ActJourney-4"), true, "terceira curva (pensamento 4: 'muito silêncio')"),
                (trigger, false, "gatilho do assovio"),
                (notice, false, "portão do encontro"),
            };
            foreach (var (point, sprint, label) in route)
            {
                if (point == null || director.CurrentBeat != JourneyBeat.Walk)
                    continue;
                if (!noSkips && label.StartsWith("terceira"))
                {
                    // O TRECHO MORTO: ~260 m de rua já de noite, sem som novo. Anda 20 s nele e
                    // depois CORTA (salto) para 25 m antes da curva — só para o vídeo caber.
                    yield return WalkTo(pad, player, point.position, 1.2f, 20f, sprint, () => director.CurrentBeat != JourneyBeat.Walk);
                    Vector3 dir = point.position - player.transform.position; dir.y = 0f;
                    var cc = player.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    player.transform.position = point.position - dir.normalized * 25f + Vector3.up * 0.5f;
                    if (cc != null) cc.enabled = true;
                    Ev("CORTE_SALTO — pula o trecho noturno sem som novo");
                }
                yield return WalkTo(pad, player, point.position, point == notice ? 0.15f : 1.2f, 140f, sprint, () => director.CurrentBeat != JourneyBeat.Walk);
                Ev(label);
            }
            Queue(pad, Vector2.zero);
            Ev($"encontro: beat {director.CurrentBeat}");

            // 2. O encontro roda sozinho; espera o controle voltar.
            yield return new WaitUntil(() => director.CurrentBeat == JourneyBeat.Walk || Time.time - t0 > (noSkips ? 420f : Duration) - 40f);
            Ev("controle de volta (fim do encontro)");
            yield return new WaitForSeconds(1.5f);

            // 3. Última curva e o prédio.
            // O encontro termina com ela DENTRO do recuo do estranho: primeiro volta ao meio da rua
            // (o portão do encontro), senão a linha reta até a curva bate na parede do recuo.
            if (notice != null)
                yield return WalkTo(pad, player, notice.position, 1.2f, 20f, false, () => director.CurrentBeat == JourneyBeat.Arrival);
            var t5 = Named("Trigger_Thought_ActJourney-5");
            if (t5 != null)
            {
                // Anda 8 s (a tensão descendo, o assovio que não volta) e corta para 12 m da curva.
                yield return WalkTo(pad, player, t5.position, 1.2f, 8f, false, () => director.CurrentBeat == JourneyBeat.Arrival);
                Vector3 dir = t5.position - player.transform.position; dir.y = 0f;
                if (!noSkips && dir.magnitude > 14f)
                {
                    var cc = player.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    player.transform.position = t5.position - dir.normalized * 12f + Vector3.up * 0.5f;
                    if (cc != null) cc.enabled = true;
                    Ev("CORTE_SALTO — até perto da última curva");
                }
                yield return WalkTo(pad, player, t5.position, 1.2f, 60f, true, () => director.CurrentBeat == JourneyBeat.Arrival);
                Ev("última curva (pensamento 5: 'não tem ninguém na rua')");
            }
            if (destination != null)
                yield return WalkTo(pad, player, destination.position, 0.6f, 60f, false, () => director.CurrentBeat == JourneyBeat.Arrival);
            Queue(pad, Vector2.zero);
            Ev($"chegada: beat {director.CurrentBeat}");
            InputSystem.RemoveDevice(pad);

            yield return new WaitUntil(() => SceneManager.GetActiveScene().name != "Road" || Time.time - t0 > (noSkips ? 420f : Duration) - 6f);
            Ev($"cena ativa: {SceneManager.GetActiveScene().name}");
            yield return new WaitForSeconds(4f);
            Ev("CORTE_FIM");
            Save();
        }

        /// <summary>Anda (ou corre) em linha reta até o alvo, virando o corpo e empurrando o stick.</summary>
        private static System.Collections.IEnumerator WalkTo(Gamepad pad, PlayerController player, Vector3 target,
                                                               float stopDistance, float timeout, bool sprint, System.Func<bool> abort)
        {
            float start = Time.time;
            float nextLog = Time.time;
            // Desvio: parada (< 0,3 m em 1,5 s) = obstáculo na frente. Anda de lado por 1,2 s,
            // alternando o lado a cada tentativa, e volta a mirar no alvo.
            Vector3 lastPos = player.transform.position;
            float lastMoveCheck = Time.time;
            float sidestepUntil = -1f;
            float side = 1f;
            while (Time.time - start < timeout && !abort())
            {
                Vector3 to = target - player.transform.position;
                to.y = 0f;
                if (Time.time >= nextLog)
                {
                    nextLog = Time.time + 10f;
                    Debug.Log($"[RoadAudioDemo]   pos {player.transform.position} -> {target} dist {to.magnitude:0.0}");
                }
                if (to.magnitude <= stopDistance)
                    break;
                if (Time.time - lastMoveCheck >= 1.5f)
                {
                    if (Vector3.Distance(player.transform.position, lastPos) < 0.3f && Time.time > sidestepUntil)
                    {
                        sidestepUntil = Time.time + 1.2f;
                        side = -side;
                        Debug.Log($"[RoadAudioDemo]   travada em {player.transform.position}: desviando para {(side > 0 ? "a direita" : "a esquerda")}");
                    }
                    lastPos = player.transform.position;
                    lastMoveCheck = Time.time;
                }
                Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
                player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, want, 200f * Time.deltaTime);
                var state = new GamepadState { leftStick = Time.time < sidestepUntil ? new Vector2(side, 0.2f) : new Vector2(0f, 1f) };
                if (sprint)
                    state = state.WithButton(GamepadButton.LeftStick);
                if (pad != null && pad.added)
                    InputSystem.QueueStateEvent(pad, state);
                yield return null;
            }
            Queue(pad, Vector2.zero);
        }

        /// <summary>Anda pela NavMesh até o alvo, de quina em quina, virando o corpo e empurrando o stick.</summary>
        private static System.Collections.IEnumerator WalkPath(Gamepad pad, PlayerController player, Vector3 target,
                                                                 float stopDistance, float timeout, System.Func<bool> abort)
        {
            float start = Time.time;
            var path = new NavMeshPath();
            Vector3[] corners = { target };
            if (NavMesh.SamplePosition(player.transform.position, out var a, 3f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(target, out var b, 3f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.corners.Length > 1)
                corners = path.corners;

            int i = corners.Length > 1 ? 1 : 0;
            Debug.Log($"[RoadAudioDemo] caminho: {corners.Length} quinas, status {path.status}, de {player.transform.position} até {target}");
            float nextLog = Time.time;
            while (Time.time - start < timeout && !abort())
            {
                if (Time.time >= nextLog)
                {
                    nextLog = Time.time + 3f;
                    Debug.Log($"[RoadAudioDemo]   pos {player.transform.position} quina {i}/{corners.Length - 1} {corners[i]} " +
                              $"dist {Vector3.Distance(player.transform.position, corners[i]):0.0} podeMover={player.CanMove}");
                }
                Vector3 to = corners[i] - player.transform.position;
                to.y = 0f;
                float reach = i == corners.Length - 1 ? stopDistance : 1.2f;
                if (to.magnitude <= reach)
                {
                    if (i == corners.Length - 1)
                        break;
                    i++;
                    continue;
                }
                Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
                player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, want, 200f * Time.deltaTime);
                Queue(pad, new Vector2(0f, 1f));
                yield return null;
            }
            Queue(pad, Vector2.zero);
        }

        private static void Queue(Gamepad pad, Vector2 move)
        {
            if (pad == null || !pad.added)
                return;
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = move });
        }

        private static T GetPrivate<T>(object owner, string field) where T : class =>
            owner.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                 ?.GetValue(owner) as T;
    }
}
