using System.Collections;
using System.Reflection;
using TheDelivery.AI;
using TheDelivery.Core;
using TheDelivery.FX;
using TheDelivery.Narrative;
using TheDelivery.Player;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Evidência da ChaseMusic (2026-11-25). Play pela Boot com o marcador "120 Assets/Scenes/Boot.unity chasemusictest".
    /// Ato 4, Beat 7, gravado. Sequência:
    /// 1. a Clear aparece a 3 m do killer, ele a vê e a música entra;
    /// 2. ela some da vista (teleporte para outro cômodo): a música segura e some;
    /// 3. de novo: ele a vê e ela se esconde no box, e a música some rápido.
    /// O som definitivo ainda não existe; só NESTE teste o loop é o NightmareChaseLoop, provisório, posto em
    /// tempo de execução (não fica salvo na cena). Um painel na tela mostra o estado e o volume.
    /// </summary>
    [InitializeOnLoad]
    public static class ChaseMusicTest
    {
        static ChaseMusicTest() => GameScreenshotTool.PlayReady += p =>
        {
            if (p != "chasemusictest") return;
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true;
            rec.StartCoroutine(Run(rec));
        };

        private static IEnumerator Run(DemoFrameRecorder rec)
        {
            yield return new WaitForSeconds(2.5f);
            GameManager.Instance.GoToAct(GameAct.Act4, GameScene.Apartment);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Apartment" && Object.FindFirstObjectByType<Act4Director>() != null);
            var d = Object.FindFirstObjectByType<Act4Director>();
            rec.Begin(75f, 24, true); // ANTES do Beat 7: o microfone do esconderijo ligado antes do gravador deixa o áudio mudo
            yield return null;
            d.AdvanceToBeat(Act4Beat.FinalHiding);
            AntagonistAI ai = null;
            float w = Time.time;
            while (ai == null && Time.time - w < 30f)
            {
                foreach (var a in Object.FindObjectsByType<AntagonistAI>(FindObjectsSortMode.None)) if (a.isActiveAndEnabled && (ai == null || a.name == "AntagonistFSM")) ai = a;
                yield return null;
            }
            yield return new WaitForSeconds(1.5f);
            var music = Object.FindFirstObjectByType<ChaseMusic>();
            Debug.Log($"[ChaseMusicTest] killer={(ai != null)} música={(music != null)}");
            foreach (var a in Object.FindObjectsByType<AntagonistAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Debug.Log($"[ChaseMusicTest] AI {a.name} (pai {(a.transform.parent ? a.transform.parent.name : "-")}) ativo={a.gameObject.activeInHierarchy} enabled={a.enabled} estado={a.CurrentState} pos={a.transform.position} escolhido={(a == ai)}");
            if (ai == null || music == null) { EditorApplication.isPlaying = false; yield break; }

            // som provisório SÓ para o teste
            var tmp = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/SoundEffects/NightmareChaseLoop.mp3");
            foreach (var m in Object.FindObjectsByType<ChaseMusic>(FindObjectsSortMode.None)) typeof(ChaseMusic).GetField("chaseLoop", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(m, tmp);
            Debug.Log($"[ChaseMusicTest] provisório={(tmp != null ? tmp.name : "NULO")} ChaseMusic na cena={Object.FindObjectsByType<ChaseMusic>(FindObjectsSortMode.None).Length}");
            AudioSource loop = music.GetComponents<AudioSource>()[0];
            ChaseMusic.verboseLog = true;

            // painel na tela
            var cam = Camera.main;
            var canvasGo = new GameObject("ChaseTestHUD", typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 0.4f; canvas.sortingOrder = 999;
            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(canvasGo.transform, false);
            var txt = txtGo.GetComponent<TextMeshProUGUI>();
            var rt = txt.rectTransform; rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(30f, -30f); rt.sizeDelta = new Vector2(900f, 200f);
            txt.fontSize = 30; txt.color = Color.yellow; txt.raycastTarget = false;
            string fase = "";
            var hud = canvasGo.AddComponent<HudTick>();
            hud.tick = () =>
            {
                if (cam != Camera.main && Camera.main != null) { cam = Camera.main; canvas.worldCamera = cam; }
                txt.text = $"TESTE da música de perseguição (som provisório)\n{fase}\nkiller: {ai.CurrentState}   |   escondida: {(PlayerHiding.Instance != null && PlayerHiding.Instance.IsHidden ? "sim" : "não")}\nvolume da música: {loop.volume:0.00}";
            };

            var pc = Object.FindFirstObjectByType<PlayerController>();
            var cc = pc.GetComponent<CharacterController>();
            void PutInFront(float dist)
            {
                // na linha do olhar dele (o cone de visão é de 60 graus), olhando para ele
                Vector3 fwd = ai.transform.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 pos = ai.transform.position + fwd * dist;
                if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
                cc.enabled = false; pc.transform.position = pos; pc.transform.rotation = Quaternion.LookRotation(-fwd); cc.enabled = true;
                pc.SyncCameraState(0f, pc.EyeHeight);
            }
            void MoveTo(Vector3 p) { cc.enabled = false; pc.transform.position = p; cc.enabled = true; }
            bool Chasing() => ai.CurrentState == AntagonistAI.AIState.Chase || ai.CurrentState == AntagonistAI.AIState.Attack;
            var box = GameObject.Find("HideSpot_ShowerBox");
            var far = GameObject.Find("_Environment/Kitchen/LightSwitch");

            // 1. ele a vê
            fase = "1) a Clear aparece na frente do killer";
            yield return new WaitForSeconds(1f);
            float t0 = Time.time, lastPut = -1f;
            while (!Chasing() && Time.time - t0 < 12f) { if (Time.time - lastPut > 0.5f) { PutInFront(3.5f); lastPut = Time.time; } yield return null; }
            fase = "1) ELE A VIU: a música entra seca";
            yield return new WaitForSeconds(0.15f);

            // 2. some da vista sem se esconder: segura 2,5 s e some
            if (far != null) MoveTo(far.transform.position + far.transform.forward * 0.8f - Vector3.up * 1.2f);
            fase = "2) ela sumiu da vista: a música segura ~2,5 s e some";
            yield return new WaitForSeconds(9f);

            // 3. de novo, e ela se esconde no box: some rápido
            fase = "3) de novo na frente dele...";
            t0 = Time.time; lastPut = -1f;
            while (!Chasing() && Time.time - t0 < 12f) { if (Time.time - lastPut > 0.5f) { PutInFront(3.5f); lastPut = Time.time; } yield return null; }
            fase = "3) ELE A VIU de novo";
            yield return new WaitForSeconds(0.15f);
            if (box != null)
            {
                MoveTo(box.transform.position);
                PlayerHiding.Instance.EnterHideSpot(box.GetComponent<HideSpot>());
            }
            fase = "3) escondida no box: a música some rápido (0,8 s)";
            yield return new WaitForSeconds(6f);
            fase = "FIM";
            yield return new WaitForSeconds(0.5f);
            rec.FinishEarly();
        }

        private sealed class HudTick : MonoBehaviour
        {
            public System.Action tick;
            private void LateUpdate() => tick?.Invoke();
        }
    }
}
