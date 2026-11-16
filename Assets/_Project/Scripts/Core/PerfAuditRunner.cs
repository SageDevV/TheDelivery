#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace TheDelivery.Core
{
    /// <summary>
    /// SÓ EDITOR. Mede o jogo rodando com <see cref="ProfilerRecorder"/>: tempo de CPU
    /// (main/render), contadores de render (draw calls, SetPass, triângulos, sombras), GC e
    /// memória, e marcadores do PlayerLoop (scripts, física, partículas, animação, render).
    /// Cada janela de medição é rotulada; o relatório sai em <see cref="ReportPath"/>.
    /// Quem roteiriza os cenários (o que ligar/desligar entre janelas) é o PerfAudit (Editor).
    /// </summary>
    public sealed class PerfAuditRunner : MonoBehaviour
    {
        public const string ReportPath = "Temp/PerfAudit/runtime.txt";
        public const string DoneMarker = "Temp/PerfAudit/runtime.done";

        private struct Counter
        {
            public string Label;
            public ProfilerRecorder Rec;
            public double Scale; // multiplica a média (ex.: ns -> ms)
            public string Unit;
        }

        private readonly List<Counter> counters = new List<Counter>();
        private readonly StringBuilder report = new StringBuilder();
        private readonly List<(string label, Func<IEnumerator> step)> steps = new List<(string, Func<IEnumerator>)>();

        public void AddStep(string label, Func<IEnumerator> step) => steps.Add((label, step));

        public void Run() => StartCoroutine(RunAll());

        private void Add(ProfilerCategory cat, string name, string label, double scale, string unit)
        {
            var rec = ProfilerRecorder.StartNew(cat, name, 600);
            counters.Add(new Counter { Label = label, Rec = rec, Scale = scale, Unit = unit });
        }

        private void StartRecorders()
        {
            const double ns2ms = 1e-6, b2mb = 1.0 / (1024 * 1024);
            Add(ProfilerCategory.Internal, "Main Thread", "CPU main thread", ns2ms, "ms");
            Add(ProfilerCategory.Internal, "CPU Main Thread Frame Time", "CPU main (frame timing)", ns2ms, "ms");
            Add(ProfilerCategory.Internal, "CPU Render Thread Frame Time", "CPU render thread", ns2ms, "ms");
            Add(ProfilerCategory.Internal, "GPU Frame Time", "GPU", ns2ms, "ms");

            Add(ProfilerCategory.Scripts, "Update.ScriptRunBehaviourUpdate", "  Scripts Update", ns2ms, "ms");
            Add(ProfilerCategory.Scripts, "PreLateUpdate.ScriptRunBehaviourLateUpdate", "  Scripts LateUpdate", ns2ms, "ms");
            Add(ProfilerCategory.Scripts, "Update.ScriptRunDelayedDynamicFrameRate", "  Coroutines", ns2ms, "ms");
            Add(ProfilerCategory.Physics, "FixedUpdate.PhysicsFixedUpdate", "  Física", ns2ms, "ms");
            Add(ProfilerCategory.Particles, "PreLateUpdate.ParticleSystemBeginUpdateAll", "  Partículas (begin)", ns2ms, "ms");
            Add(ProfilerCategory.Particles, "PostLateUpdate.ParticleSystemEndUpdateAll", "  Partículas (end)", ns2ms, "ms");
            Add(ProfilerCategory.Animation, "PreLateUpdate.DirectorUpdateAnimationBegin", "  Animação (begin)", ns2ms, "ms");
            Add(ProfilerCategory.Animation, "PreLateUpdate.DirectorUpdateAnimationEnd", "  Animação (end)", ns2ms, "ms");
            Add(ProfilerCategory.Ai, "PreUpdate.AIUpdatePostScript", "  NavMesh", ns2ms, "ms");
            Add(ProfilerCategory.Render, "PostLateUpdate.FinishFrameRendering", "  Render (main)", ns2ms, "ms");
            Add(ProfilerCategory.Render, "Inl_UniversalRenderPipeline.RenderSingleCameraInternal: Main Camera", "    URP câmera", ns2ms, "ms");
            Add(ProfilerCategory.Render, "Shadows.RenderShadowMap", "    Sombras", ns2ms, "ms");
            Add(ProfilerCategory.Render, "Inl_MainLightShadow", "    Sombra luz principal", ns2ms, "ms");
            Add(ProfilerCategory.Render, "Inl_AdditionalLightsShadow", "    Sombras luzes adicionais", ns2ms, "ms");
            Add(ProfilerCategory.Render, "Inl_DrawOpaqueObjects", "    Opacos", ns2ms, "ms");
            Add(ProfilerCategory.Render, "Inl_DrawTransparentObjects", "    Transparentes", ns2ms, "ms");
            Add(ProfilerCategory.Render, "Inl_UberPostProcess", "    Pós-processamento", ns2ms, "ms");

            Add(ProfilerCategory.Render, "SetPass Calls Count", "SetPass calls", 1, "");
            Add(ProfilerCategory.Render, "Draw Calls Count", "Draw calls", 1, "");
            Add(ProfilerCategory.Render, "Batches Count", "Batches", 1, "");
            Add(ProfilerCategory.Render, "Triangles Count", "Triângulos", 1e-3, "k");
            Add(ProfilerCategory.Render, "Shadow Casters Count", "Shadow casters", 1, "");
            Add(ProfilerCategory.Render, "Visible Skinned Meshes Count", "Skinned visíveis", 1, "");

            Add(ProfilerCategory.Memory, "GC Allocated In Frame", "GC alocado/quadro", 1.0 / 1024, "KB");
            Add(ProfilerCategory.Memory, "Total Used Memory", "Memória total usada", b2mb, "MB");
            Add(ProfilerCategory.Memory, "GC Used Memory", "Heap gerenciado usado", b2mb, "MB");
            Add(ProfilerCategory.Memory, "Gfx Used Memory", "Memória gráfica", b2mb, "MB");
            Add(ProfilerCategory.Memory, "Texture Memory", "Texturas", b2mb, "MB");
            Add(ProfilerCategory.Memory, "Mesh Memory", "Malhas", b2mb, "MB");
            Add(ProfilerCategory.Memory, "Audio Used Memory", "Áudio", b2mb, "MB");
        }

        private IEnumerator RunAll()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            StartRecorders();
            report.AppendLine($"Unity {Application.unityVersion} | {SystemInfo.processorType} | {SystemInfo.graphicsDeviceName} | " +
                              $"Game view {Screen.width}x{Screen.height} | vSync {QualitySettings.vSyncCount} | targetFPS {Application.targetFrameRate}");
            report.AppendLine("Contadores inválidos (não disponíveis nesta versão/plataforma) aparecem como n/d.");
            report.AppendLine();

            foreach (var (label, step) in steps)
            {
                IEnumerator e = null;
                try { e = step(); }
                catch (Exception ex) { report.AppendLine($"[{label}] falhou ao preparar: {ex.Message}"); }
                if (e != null)
                    yield return e;
            }

            foreach (var c in counters)
                c.Rec.Dispose();

            File.WriteAllText(ReportPath, report.ToString());
            File.WriteAllText(DoneMarker, "ok");
            Debug.Log($"[PerfAuditRunner] Relatório salvo em {ReportPath}");
        }

        /// <summary>Espera <paramref name="settle"/> s, mede <paramref name="seconds"/> s e escreve a janela.</summary>
        public IEnumerator Measure(string label, float settle = 1.5f, float seconds = 4f)
        {
            yield return new WaitForSecondsRealtime(settle);

            int n = counters.Count;
            var sums = new double[n];
            var maxs = new double[n];
            var valid = new bool[n];
            int frames = 0;
            float t0 = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                yield return null;
                frames++;
                for (int i = 0; i < n; i++)
                {
                    ProfilerRecorder r = counters[i].Rec;
                    if (!r.Valid || r.Count == 0) continue;
                    valid[i] = true;
                    double v = r.LastValue;
                    sums[i] += v;
                    if (v > maxs[i]) maxs[i] = v;
                }
            }

            float elapsed = Time.realtimeSinceStartup - t0;
            report.AppendLine($"=== {label}  ({frames} quadros, {frames / elapsed:0.0} fps no Editor)");
            for (int i = 0; i < n; i++)
            {
                Counter c = counters[i];
                if (!valid[i]) { report.AppendLine($"  {c.Label,-32} n/d"); continue; }
                double avg = sums[i] / Mathf.Max(1, frames) * c.Scale;
                double max = maxs[i] * c.Scale;
                report.AppendLine($"  {c.Label,-32} média {avg,9:0.###} {c.Unit,-2}  pico {max,9:0.###} {c.Unit}");
            }
            report.AppendLine();
        }

        public void Note(string text) => report.AppendLine(text);
    }
}
#endif
