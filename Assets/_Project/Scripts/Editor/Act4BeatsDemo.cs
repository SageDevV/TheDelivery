using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using TheDelivery.AI;
using TheDelivery.Core;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// "act4beatsvideo" (2026-11-25): o Ato 4 inteiro jogado por roteiro, para ouvir as pendências resolvidas.
    /// Play pela Boot; marcador "420 Assets/Scenes/Boot.unity act4beatsvideo". Sequência:
    /// - acorda com os passos na cozinha e levanta (Espaço simulado);
    /// - investiga o corredor e a cozinha; o vulto passa com a nota grave; o prato quebra;
    /// - ela se esconde no guarda-roupa e respira baixinho;
    /// - pega o celular morto (toques na tela) e anda até o fixo, onde ouve o cantarolar;
    /// - a ligação: discagem, atendente abafada, a linha cai, o apagão;
    /// - se esconde no box; os passos vão embora; sirene e viaturas; sai do box e leva a pancada.
    /// Tempos e eventos vão para Temp/DemoFrames/log.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class Act4BeatsDemo
    {
        static Act4BeatsDemo() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "act4beatsvideo" && prep != "act4fixvideo" && prep != "act4shadowvideo" && prep != "act4chasevideo" && prep != "act4sirenvideo") return;
            sirenRun = prep == "act4sirenvideo";
            chaseRun = prep == "act4chasevideo";
            shadowRun = prep == "act4shadowvideo";
            shortRun = prep == "act4fixvideo";
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true;
            rec.StartCoroutine(Script(rec));
        }

        private static bool sirenRun; // act4sirenvideo: do box até a sala, com o giroflex batendo nas janelas
        private static bool chaseRun; // act4chasevideo: o killer a vê, persegue (música de perseguição) e ela se esconde
        private static bool shadowRun; // act4shadowvideo: para depois do vulto (o susto)
        private static bool shortRun; // act4fixvideo: para depois do celular (as correções de 25/11)
        private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static T F<T>(object o, string name) => (T)o.GetType().GetField(name, BF)?.GetValue(o);
        private static void SetF(object o, string name, object v) => o.GetType().GetField(name, BF)?.SetValue(o, v);

        private static IEnumerator Script(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            float t0 = 0f;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log("[Act4BeatsDemo] " + s); }
            void Save() { Directory.CreateDirectory(DemoFrameRecorder.OutputDir); File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString()); }

            yield return new WaitForSeconds(2.5f);
            GameManager.Instance.GoToAct(GameAct.Act4, GameScene.Apartment);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Apartment" && Object.FindFirstObjectByType<Act4Director>() != null);
            var d = Object.FindFirstObjectByType<Act4Director>();
            SetF(d, "deadPhoneDelay", 10f); // só a demo: encurta a caçada antes do lembrete do celular
            rec.Begin(420f, 24, true);
            t0 = Time.time;
            Ev("Apartment carregado — Beat " + d.CurrentBeat);

            var pc = Object.FindFirstObjectByType<PlayerController>();
            var cc = pc.GetComponent<CharacterController>();
            void Teleport(Transform t, float back = 0f)
            {
                if (t == null) { Ev("(ponto ausente)"); return; }
                Vector3 p = t.position - t.forward * back;
                if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) p = hit.position;
                cc.enabled = false; pc.transform.position = p; cc.enabled = true;
            }
            void Face(Vector3 target)
            {
                Vector3 dir = target - pc.transform.position; dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f) pc.transform.rotation = Quaternion.LookRotation(dir);
                pc.SyncCameraState(5f, pc.EyeHeight);
            }
            IEnumerator WaitThought(float max = 20f)
            {
                yield return null;
                float s = Time.time;
                while (ThoughtSystem.Instance != null && ThoughtSystem.Instance.IsShowing && Time.time - s < max) yield return null;
            }
            IEnumerator WaitBeat(Act4Beat b, float max)
            {
                float s = Time.time;
                while (d.CurrentBeat != b && Time.time - s < max) yield return null;
                Ev(d.CurrentBeat == b ? "Beat " + b : $"TIMEOUT esperando {b} (está em {d.CurrentBeat})");
            }
            HideSpot Spot(string name)
            {
                foreach (var h in Object.FindObjectsByType<HideSpot>(FindObjectsSortMode.None)) if (h.name == name) return h;
                return null;
            }

            if (sirenRun)
            {
                // atalho: direto ao Beat 7, ela no box; o killer "vai embora", sirene, viaturas estacionam
                d.AdvanceToBeat(Act4Beat.FinalHiding);
                // como no jogo: a energia caiu na ligação (o atalho pula a ligação, então apaga aqui)
                foreach (var sw in Object.FindObjectsByType<TheDelivery.Interaction.LightSwitch>(FindObjectsSortMode.None))
                    if (sw.transform.parent == null || sw.transform.parent.name != "BuildingCorridor") { sw.SetOnSilent(false); sw.Powered = false; }
                yield return new WaitForSeconds(1f);
                var boxS = Spot("HideSpot_ShowerBox");
                if (boxS != null) { Teleport(boxS.transform, 0.9f); yield return new WaitForSeconds(0.5f); PlayerHiding.Instance.EnterHideSpot(boxS); Ev("No box"); }
                yield return WaitBeat(Act4Beat.Death, 120f);
                Light anyParked = null;
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.transform.parent != null && l.transform.parent.name.StartsWith("ParkedSiren_Window_Bathroom")) anyParked = l;
                float ws = Time.time;
                while ((anyParked == null || !anyParked.enabled) && Time.time - ws < 40f) yield return null;
                Ev("Viaturas estacionadas: giroflex na janela do banheiro");
                yield return new WaitForSeconds(6f);
                if (PlayerHiding.Instance.IsHidden) PlayerHiding.Instance.ExitHideSpot();
                yield return new WaitForSeconds(1.5f);
                var bathWin = GameObject.Find("_Environment/Bathroom/Window");
                if (bathWin != null) Face(bathWin.transform.position);
                Ev("Fora do box, olhando a janela do banheiro");
                yield return new WaitForSeconds(4f);
                var livingWin = GameObject.Find("_Environment/LivingRoom/Window");
                var lz = F<Transform>(d, "landlineApproachZone");
                if (lz != null) Teleport(lz);
                if (livingWin != null) Face(livingWin.transform.position);
                Ev("Na sala, olhando a janela");
                yield return new WaitForSeconds(5f);
                var bedLight = GameObject.Find("_Environment/Bedroom/Light_Window");
                var bedP = F<Transform>(d, "bedPosition");
                if (bedP != null) Teleport(bedP);
                if (bedLight != null) Face(bedLight.transform.position);
                Ev("No quarto, olhando a janela");
                yield return new WaitForSeconds(5f);
                Ev("FIM (act4sirenvideo)"); Save(); rec.FinishEarly(); yield break;
            }

            // 1. Despertar: passos na cozinha; levanta com Espaço
            Ev("Despertar: passos abafados na cozinha");
            var prompt = F<GameObject>(d, "standUpPrompt");
            float w = Time.time;
            while ((prompt == null || !prompt.activeInHierarchy) && Time.time - w < 25f) yield return null;
            yield return new WaitForSeconds(1f);
            var kb = Keyboard.current;
            if (kb != null)
            {
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space)); yield return null; yield return null;
                InputSystem.QueueStateEvent(kb, new KeyboardState()); Ev("Espaço: levantar");
            }
            yield return WaitBeat(Act4Beat.Investigation, 15f);

            // 2. Investigação
            yield return new WaitForSeconds(1.5f);
            Teleport(F<Transform>(d, "hallwayZone")); Ev("Corredor (só a luz da sala acesa)");
            var livingLight = GameObject.Find("_Environment/LivingRoom/Light");
            if (livingLight != null) Face(livingLight.transform.position - Vector3.up * 1.2f);
            yield return WaitThought();
            yield return new WaitForSeconds(1f);
            Teleport(F<Transform>(d, "kitchenZone")); Ev("Cozinha");
            yield return WaitBeat(Act4Beat.Discovery, 20f);

            // 3. Descoberta: o vulto com a nota grave; depois a zona -> o prato quebrando
            var shadow = F<GameObject>(d, "shadowFigure");
            w = Time.time;
            yield return new WaitForSeconds(1f);
            while (shadow != null && shadow.activeInHierarchy && Time.time - w < 25f) yield return null;
            Ev("O vulto passou (nota grave)");
            yield return new WaitForSeconds(1f);
            Teleport(F<Transform>(d, "discoveryTriggerZone")); Ev("Zona da descoberta: o prato quebra, o killer acorda longe");
            yield return new WaitForSeconds(2.5f);
            if (shadowRun) { Ev("FIM (act4shadowvideo)"); Save(); rec.FinishEarly(); yield break; }
            if (chaseRun)
            {
                // som PROVISÓRIO só nesta gravação (o definitivo vem do ElevenLabs depois da renovação)
                var music = Object.FindFirstObjectByType<TheDelivery.FX.ChaseMusic>();
                var tmpLoop = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/SoundEffects/NightmareChaseLoop.mp3");
                if (music != null && typeof(TheDelivery.FX.ChaseMusic).GetField("chaseLoop", BF).GetValue(music) == null)
                    typeof(TheDelivery.FX.ChaseMusic).GetField("chaseLoop", BF).SetValue(music, tmpLoop); // só se ainda não houver o definitivo
                AntagonistAI killer = null;
                float wk = Time.time;
                while (killer == null && Time.time - wk < 20f)
                {
                    foreach (var a in Object.FindObjectsByType<AntagonistAI>(FindObjectsSortMode.None)) if (a.isActiveAndEnabled) killer = a;
                    yield return null;
                }
                Ev("Perseguição: a Clear aparece na frente do killer");
                float tc = Time.time, lastPut = -1f;
                bool Chasing() => killer != null && (killer.CurrentState == AntagonistAI.AIState.Chase || killer.CurrentState == AntagonistAI.AIState.Attack);
                while (killer != null && !Chasing() && Time.time - tc < 15f)
                {
                    if (Time.time - lastPut > 0.5f)
                    {
                        Vector3 fwd = killer.transform.forward; fwd.y = 0f; fwd.Normalize();
                        Vector3 p = killer.transform.position + fwd * 3.5f;
                        if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hh, 2f, UnityEngine.AI.NavMesh.AllAreas)) p = hh.position;
                        cc.enabled = false; pc.transform.position = p; pc.transform.rotation = Quaternion.LookRotation(-fwd); cc.enabled = true;
                        pc.SyncCameraState(0f, pc.EyeHeight);
                        lastPut = Time.time;
                    }
                    yield return null;
                }
                Ev(Chasing() ? "ELE A VIU: a música de perseguição entra" : "(ele não a viu em 15 s)");
                yield return new WaitForSeconds(0.35f);
                killer.SuspendForDirector(); // só na demo: ele para (não a alcança), e para a música é como perdê-la de vista
                // some da vista dele (o ponto da casa mais LONGE dele): a música segura ~2,5 s e some em ~2,5 s
                Transform farthest = null; float best = -1f;
                foreach (var n in new[] { "bedPosition", "kitchenZone", "hallwayZone", "ambushZone", "landlineApproachZone" })
                {
                    var tz = F<Transform>(d, n);
                    if (tz == null) continue;
                    float dd = Vector3.Distance(tz.position, killer.transform.position);
                    if (dd > best) { best = dd; farthest = tz; }
                }
                Teleport(farthest);
                Ev("Ela sumiu da vista dele: a música segura e some");
                yield return new WaitForSeconds(8f);
                Ev("FIM (act4chasevideo)"); Save(); rec.FinishEarly(); yield break;
            }

            // 4. Esconde-se no guarda-roupa (respiração), até o lembrete do celular
            var wardrobe = Spot("HideSpot_Wardrobe") ?? Spot("HideSpot_Bed");
            if (wardrobe != null) { PlayerHiding.Instance.EnterHideSpot(wardrobe); Ev("Escondida: " + wardrobe.name + " (respiração)"); }
            yield return WaitBeat(Act4Beat.DeadPhone, 90f);
            yield return WaitThought();
            if (PlayerHiding.Instance.IsHidden) PlayerHiding.Instance.ExitHideSpot();
            yield return new WaitForSeconds(1.5f);
            var phone = F<GameObject>(d, "phoneObject");
            if (phone != null) { Teleport(phone.transform, 0.8f); Face(phone.transform.position); }
            w = Time.time;
            while (!F<bool>(d, "phoneArmed") && Time.time - w < 15f) yield return null;
            d.OnPhonePickedUp(); Ev("Celular morto: toques na tela");
            yield return WaitBeat(Act4Beat.RunToLandline, 40f);
            if (shortRun)
            {
                yield return new WaitForSeconds(3f);
                Ev("FIM (act4fixvideo)"); Save(); rec.FinishEarly(); yield break;
            }

            // 5. O fixo: ao chegar perto, o cantarolar ao longe
            w = Time.time;
            while (!F<bool>(d, "landlineArmed") && Time.time - w < 20f) yield return null;
            var zone = F<Transform>(d, "landlineApproachZone");
            Teleport(zone); Ev("Perto do fixo: o killer cantarola ao longe");
            var landline = F<GameObject>(d, "landlineObject");
            if (landline != null) Face(landline.transform.position);
            yield return new WaitForSeconds(8f);
            d.OnLandlinePickedUp(); Ev("A ligação: discagem, atendente, linha cai, apagão");
            yield return WaitBeat(Act4Beat.FinalHiding, 120f);

            // 6. O box
            yield return WaitThought();
            var box = F<HideSpot>(d, "showerBoxSpot") ?? Spot("HideSpot_ShowerBox");
            if (box != null) { Teleport(box.transform, 0.9f); yield return new WaitForSeconds(1f); PlayerHiding.Instance.EnterHideSpot(box); Ev("No box"); }
            yield return WaitBeat(Act4Beat.Death, 120f);

            // 7. Passos indo embora, sirene e viaturas; "Ele foi embora?" -> sai -> a pancada
            Ev("Passos indo embora; sirene e viaturas");
            w = Time.time;
            while (Time.time - w < 40f)
            {
                var th = ThoughtSystem.Instance;
                if (th != null && th.IsShowing && Time.time - w > 6f) break;
                yield return null;
            }
            yield return WaitThought();
            yield return new WaitForSeconds(1f);
            if (PlayerHiding.Instance.IsHidden) PlayerHiding.Instance.ExitHideSpot();
            yield return new WaitForSeconds(2f);
            Teleport(F<Transform>(d, "ambushZone")); Ev("Emboscada no corredor: a pancada");
            yield return new WaitForSeconds(9f);
            Ev("FIM");
            Save();
            rec.FinishEarly();
        }
    }
}
