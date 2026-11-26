using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Pendências de som da varredura global (2026-11-26), só com reuso de arquivos que já estão no projeto:
    /// - Menu: o apagão = Act4PowerOut; a presença no escuro = o cantarolar do killer; o drone = Act3OminousDrone.
    /// - Ato 1: levantar das cadeiras = CafeteriaChairDrag; ligar/desligar os fliperamas = estalo elétrico (FlickerBuzz_02).
    /// - Ato 2: tira o ElevatorSound_Placeholder (reserva que ninguém ouvia; os sons novos do elevador já tocam).
    /// - Ato 3: sentar/levantar do sofá = a roupa da Clear.
    /// Marcador Temp/SweepAudioFixes.pending; relatório em Temp/SweepAudioFixes.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class SweepAudioFixes
    {
        static SweepAudioFixes() => EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists("Temp/SweepAudioFixes.pending")) return;
            File.Delete("Temp/SweepAudioFixes.pending"); Run();
        };

        private static StringBuilder log;
        private static AudioClip Clip(string n)
        {
            foreach (var ext in new[] { ".wav", ".mp3", ".ogg" })
            {
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/SoundEffects/" + n + ext);
                if (c != null) return c;
            }
            log.AppendLine("  FALTA arquivo " + n); return null;
        }
        private static void Set(Component c, string field, Object v, bool onlyIfEmpty = false)
        {
            var so = new SerializedObject(c); var p = so.FindProperty(field);
            if (p == null) { log.AppendLine($"  CAMPO NÃO EXISTE {c.GetType().Name}.{field}"); return; }
            if (onlyIfEmpty && p.objectReferenceValue != null) return;
            p.objectReferenceValue = v; so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"  {c.name} {c.GetType().Name}.{field} = {(v ? v.name : "vazio")}");
        }

        private static void InScene(string path, System.Action<System.Collections.Generic.List<Transform>> act)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !(scene.IsValid() && scene.isLoaded);
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                log.AppendLine("== " + Path.GetFileNameWithoutExtension(path));
                act(scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList());
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        private static System.Collections.Generic.IEnumerable<Component> Comps(System.Collections.Generic.List<Transform> all, string type) =>
            all.Select(t => t.GetComponent(type)).Where(c => c != null);

        /// <summary>
        /// Materiais sem textura da varredura (2026-11-26):
        /// - batente da porta da cafeteria (SimpleDoor_02): recebe as texturas da folha (SimpleDoor), do mesmo atlas;
        /// - carro em frente ao prédio (Tocus): vidro escuro e opaco (regra dos carros: ninguém dentro), cromo polido e
        ///   plástico fosco escuro. Esses não pedem textura, só os valores certos.
        /// </summary>
        private static void Materials()
        {
            log.AppendLine("== Materiais");
            var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/SimpleDoor.mat");
            var frame = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/SimpleDoor_02.mat");
            if (src != null && frame != null)
            {
                foreach (var p in new[] { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" })
                    if (src.HasProperty(p) && frame.HasProperty(p)) frame.SetTexture(p, src.GetTexture(p));
                foreach (var k in new[] { "_NORMALMAP", "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP" })
                    if (src.IsKeywordEnabled(k)) frame.EnableKeyword(k); else frame.DisableKeyword(k);
                frame.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                frame.SetFloat("_Metallic", src.GetFloat("_Metallic"));
                frame.SetFloat("_Smoothness", src.GetFloat("_Smoothness"));
                EditorUtility.SetDirty(frame);
                log.AppendLine("  SimpleDoor_02 (batente da cafeteria) = texturas da SimpleDoor");
            }
            void Tune(string name, Color c, float metal, float smooth)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Imports/Car_Low/Tocus/Materials/{name}.mat");
                if (m == null) { log.AppendLine("  FALTA " + name); return; }
                m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
                EditorUtility.SetDirty(m);
                log.AppendLine($"  {name}: cor {c} metálico {metal} liso {smooth}");
            }
            Tune("Tocus_Windows", new Color(0.035f, 0.04f, 0.045f), 0.15f, 0.92f);  // vidro fumê opaco, reflexo forte
            Tune("Tocus_Chrome", new Color(0.78f, 0.78f, 0.8f), 1f, 0.82f);
            Tune("Tocus_Plastics", new Color(0.07f, 0.07f, 0.075f), 0f, 0.3f);
        }

        public static void Run()
        {
            log = new StringBuilder();
            InScene("Assets/Scenes/MainMenu.unity", all =>
            {
                foreach (var c in Comps(all, "MainMenuAtmosphere"))
                {
                    Set(c, "blackoutClick", Clip("Act4PowerOut"));
                    Set(c, "darkPresence", Clip("KillerHumming"));
                    Set(c, "ambientDrone", Clip("Act3OminousDrone"), onlyIfEmpty: true);
                }
            });
            InScene("Assets/Scenes/Nightmare.unity", all =>
            {
                // 2026-11-26: o usuário escolheu a variante C para TODOS os sustos (JumpscareHit.wav)
                foreach (var c in Comps(all, "NightmareDirector")) Set(c, "attackJumpscareSound", Clip("JumpscareHit"));
            });
            InScene("Assets/Scenes/Cafeteria.unity", all =>
            {
                foreach (var c in Comps(all, "SittableChair")) Set(c, "standSound", Clip("CafeteriaChairDrag"), onlyIfEmpty: true);
                foreach (var c in Comps(all, "TVScreen")) Set(c, "toggleClip", Clip(c.name.Contains("Laptop") ? "LightSwitchClick_01" : "FlickerBuzz_02"));
            });
            InScene("Assets/Scenes/Reception.unity", all =>
            {
                foreach (var c in Comps(all, "Act2Director"))
                {
                    var so = new SerializedObject(c);
                    bool novos = so.FindProperty("elevatorRideSound").objectReferenceValue != null && so.FindProperty("elevatorDingSound").objectReferenceValue != null;
                    if (novos) Set(c, "elevatorSound", null); else log.AppendLine("  elevador: sons novos ausentes, mantém a reserva");
                }
            });
            InScene("Assets/Scenes/Apartment.unity", all =>
            {
                foreach (var c in Comps(all, "SittableChair"))
                {
                    Set(c, "sitSound", Clip("ClearClothRustle_01"), onlyIfEmpty: true);
                    Set(c, "standSound", Clip("ClearClothRustle_03"), onlyIfEmpty: true);
                }
            });
            Materials();
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/SweepAudioFixes.txt", log.ToString());
            Debug.Log("[SweepAudioFixes] OK\n" + log);
        }
    }
}
