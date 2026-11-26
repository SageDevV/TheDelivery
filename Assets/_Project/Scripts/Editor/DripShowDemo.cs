using System.Collections;
using TheDelivery.Core;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>"dripshow" (2026-11-26): as goteiras do navio na luz do celular — uma na cela (respingo no chão) e duas na
    /// área inundada (anel na água).</summary>
    [InitializeOnLoad]
    public static class DripShowDemo
    {
        static DripShowDemo() => GameScreenshotTool.PlayReady += p =>
        {
            if (p != "dripshow") return;
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true;
            rec.StartCoroutine(Run(rec));
        };

        private static IEnumerator Run(DemoFrameRecorder rec)
        {
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.ActEscape, GameScene.Escape);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Escape" && Object.FindAnyObjectByType<Act5Director>() != null);
            var d = Object.FindAnyObjectByType<Act5Director>();
            while (d.CurrentBeat != Act5Beat.CellPuzzle) yield return null;
            var pc = Object.FindAnyObjectByType<PlayerController>();
            var cc = pc.GetComponent<CharacterController>();
            void Place(Vector3 pos, Vector3 look, float pitch)
            {
                cc.enabled = false;
                Vector3 dir = look - pos; dir.y = 0f;
                pc.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
                cc.enabled = true;
                pc.SyncCameraState(pitch, pc.EyeHeight);
            }
            // (sem travar o movimento: com CanMove false a inclinação da câmera não é aplicada)
            rec.Begin(24f, 24, true);
            Place(new Vector3(-25.1f, 0f, -15.4f), new Vector3(-23.8f, 0f, -14.2f), 12f);      // goteira da cela, a 1,8 m
            for (int k = 0; k < 4; k++)
            {
                yield return new WaitForSeconds(2f);
                int alive = 0, sys = 0, playing = 0;
                foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    if (ps.name.StartsWith("Drip_")) { sys++; alive += ps.particleCount; if (ps.isPlaying) playing++; }
                Debug.Log($"[DripShow] goteiras={sys} tocando={playing} gotas no ar={alive}");
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.enabled && l.type != LightType.Point) Debug.Log($"[DripShow] luz {l.name} {l.type} int={l.intensity} range={l.range} ang={l.spotAngle} shadows={l.shadows} pos={l.transform.position} fwd={l.transform.forward} cull={l.cullingMask}");
            }
            Place(new Vector3(13.6f, -0.9f, 10.0f), new Vector3(15f, 0f, 12f), 17f);             // área inundada
            for (int k = 0; k < 7; k++)
            {
                yield return new WaitForSeconds(1f);
                var wr = Object.FindAnyObjectByType<TheDelivery.Act5.WaterRipples>();
                Debug.Log($"[DripShow] WaterRipples={(wr != null)} shader={(wr != null ? wr.GetComponent<Renderer>().sharedMaterial.shader.name : "-")} t={Time.timeSinceLevelLoad:0.0}");
                int sp = 0, rp = 0;
                foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                { if (ps.name == "Splash") sp += ps.particleCount; if (ps.name == "Ripple") rp += ps.particleCount; }
                Debug.Log($"[DripShow] respingos={sp} aneis={rp}");
                var buf = new ParticleSystem.Particle[8];
                foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    if (ps.name == "Ripple" && ps.particleCount > 0)
                    {
                        int n = ps.GetParticles(buf);
                        for (int j = 0; j < n; j++) Debug.Log($"[DripShow] anel em {buf[j].position} tam={buf[j].GetCurrentSize(ps):0.00} cor={buf[j].GetCurrentColor(ps)} cam={Camera.main.transform.position} rot={ps.GetComponent<ParticleSystemRenderer>().renderMode}");
                    }
            }
            Place(new Vector3(11.2f, -0.9f, 6.1f), new Vector3(10f, 0f, 4f), 17f);
            yield return new WaitForSeconds(7f);
            rec.FinishEarly();
        }
    }
}
