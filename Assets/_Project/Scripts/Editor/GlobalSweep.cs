using System.Collections.Generic;
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
    /// Varredura GLOBAL estática (2026-11-26) do Prólogo ao Ato 4 (cenas Nightmare, Cafeteria, Road, Reception e
    /// Apartment, mais MainMenu). Para cada cena lista:
    /// - scripts faltando (Missing Script);
    /// - sons placeholder (clip com "Placeholder" no nome) ainda ligados, com o componente.campo;
    /// - campos de som vazios (AudioClip / AudioClip[] nulos), agrupados por componente;
    /// - materiais sem textura (cor chapada) em renderers visíveis;
    /// - malhas primitivas (Cube/Sphere/Plane/Cylinder/Capsule/Quad do Unity) em renderers visíveis.
    /// Marcador Temp/GlobalSweep.pending; relatório em Temp/GlobalSweep.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class GlobalSweep
    {
        static GlobalSweep() => EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists("Temp/GlobalSweep.pending")) return;
            File.Delete("Temp/GlobalSweep.pending"); Run();
        };

        private static readonly string[] Scenes =
        {
            "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Nightmare.unity", "Assets/Scenes/Cafeteria.unity",
            "Assets/Scenes/Road.unity", "Assets/Scenes/Reception.unity", "Assets/Scenes/Apartment.unity",
        };
        private static readonly HashSet<string> Primitives = new HashSet<string> { "Cube", "Sphere", "Plane", "Cylinder", "Capsule", "Quad" };

        private static string P(Transform t) { var s = t.name; int d = 0; while (t.parent != null && d++ < 6) { t = t.parent; s = t.name + "/" + s; } return s; }

        private static void Run()
        {
            var sb = new StringBuilder();
            var active = SceneManager.GetActiveScene().path;
            foreach (var path in Scenes)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !(scene.IsValid() && scene.isLoaded);
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    sb.AppendLine($"################ {Path.GetFileNameWithoutExtension(path)}");
                    var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();

                    int missing = 0;
                    foreach (var t in all) missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    sb.AppendLine($"Scripts faltando: {missing}");

                    var ph = new List<string>(); var empty = new Dictionary<string, List<string>>();
                    foreach (var t in all)
                        foreach (var mb in t.GetComponents<MonoBehaviour>())
                        {
                            if (mb == null) continue;
                            var so = new SerializedObject(mb); var it = so.GetIterator();
                            while (it.NextVisible(true))
                            {
                                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                                bool isClipField = it.type == "PPtr<AudioClip>" || it.type == "PPtr<$AudioClip>";
                                if (!isClipField) continue;
                                var o = it.objectReferenceValue;
                                string where = $"{mb.GetType().Name}.{it.propertyPath} @ {P(t)}";
                                if (o == null)
                                {
                                    string key = mb.GetType().Name + "." + it.propertyPath.Split('.')[0];
                                    if (!empty.TryGetValue(key, out var l)) empty[key] = l = new List<string>();
                                    l.Add(P(t));
                                }
                                else if (o.name.Contains("Placeholder")) ph.Add($"{where} = {o.name}");
                            }
                        }
                    sb.AppendLine($"Sons PLACEHOLDER ligados: {ph.Count}");
                    foreach (var s in ph) sb.AppendLine("   " + s);
                    sb.AppendLine($"Campos de som VAZIOS: {empty.Sum(kv => kv.Value.Count)} em {empty.Count} campos");
                    foreach (var kv in empty.OrderByDescending(kv => kv.Value.Count))
                        sb.AppendLine($"   {kv.Key} x{kv.Value.Count}  (ex.: {kv.Value[0]})");

                    var flat = new Dictionary<string, List<string>>(); var prim = new List<string>();
                    foreach (var r in all.Select(t => t.GetComponent<Renderer>()).Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer) && !(r is LineRenderer) && !(r is TrailRenderer)))
                    {
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) continue;
                            bool hasTex = (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) || (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null);
                            bool special = m.shader != null && (m.shader.name.Contains("Unlit") || m.shader.name.Contains("Particle") || m.shader.name.Contains("Skybox") || m.shader.name.Contains("Hidden"));
                            if (!hasTex && !special && !m.name.Contains("Glass") && !m.name.Contains("Emiss") && !m.name.Contains("Glow"))
                            {
                                if (!flat.TryGetValue(m.name, out var l)) flat[m.name] = l = new List<string>();
                                l.Add(P(r.transform));
                            }
                        }
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null && Primitives.Contains(mf.sharedMesh.name) && AssetDatabase.GetAssetPath(mf.sharedMesh).Contains("unity default resources"))
                            prim.Add($"{mf.sharedMesh.name} @ {P(r.transform)} [{(r.sharedMaterial ? r.sharedMaterial.name : "-")}]");
                    }
                    sb.AppendLine($"Materiais SEM TEXTURA (visíveis): {flat.Count}");
                    foreach (var kv in flat.OrderByDescending(kv => kv.Value.Count))
                        sb.AppendLine($"   {kv.Key} x{kv.Value.Count}  (ex.: {kv.Value[0]})");
                    sb.AppendLine($"Malhas PRIMITIVAS visíveis: {prim.Count}");
                    foreach (var s in prim.Take(40)) sb.AppendLine("   " + s);
                    if (prim.Count > 40) sb.AppendLine($"   ... e mais {prim.Count - 40}");
                }
                finally { if (opened && scene.path != active) EditorSceneManager.CloseScene(scene, true); }
            }
            File.WriteAllText("Temp/GlobalSweep.txt", sb.ToString());
            Debug.Log("[GlobalSweep] OK");
        }
    }
}
