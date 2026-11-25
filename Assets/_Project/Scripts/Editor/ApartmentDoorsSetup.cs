using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheDelivery.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Portas novas do prédio (2026-11-24, <c>Blender/build_door.py</c>): folha almofadada de madeira com ferragens e
    /// número (302 da Clear, 303 do vizinho com corrente, 301 do outro vizinho) + guarnição (batente e alisares) em cada vão.
    /// <para>Troca só o VISUAL e a colisão do <c>DoorMesh</c> de cada porta (o componente Door, os sons e o pivô da dobradiça
    /// ficam como estão), remove as maçanetas primitivas e cria a guarnição fixa no vão.</para>
    /// A orientação é calculada da porta antiga: eixo fino = espessura, lado da largura = para onde a folha antiga ia a partir
    /// do pivô, lado do corredor = o do centro do BuildingCorridor (o número e o olho mágico ficam para lá).
    /// Menu "Tools/The Delivery/Apartment - Portas novas"; marcador <c>Temp/ApartmentDoorsSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class ApartmentDoorsSetup
    {
        private const string MarkerPath = "Temp/ApartmentDoorsSetup.pending";
        private const string ScenePath = "Assets/Scenes/Apartment.unity";
        private const string ModelDir = "Assets/_Project/Models/Doors";
        private const string ModelPath = ModelDir + "/Doors.fbx";
        private const float JambGap = 0.015f;     // batente (1,2 cm) + folga (3 mm) entre o vão e a folha
        private const float FloorGap = 0.012f;
        private static double nextPoll;

        // porta da cena -> (folha, guarnição, folha com o corredor em +Z local?, altura da folha, largura do vão)
        private static readonly Dictionary<string, (string leaf, string frame, bool corridorPlusZ, float h, float wo)> Map =
            new Dictionary<string, (string, string, bool, float, float)>
            {
                { "ApartmentDoor", ("Door_302", "Frame_Apt", false, 2.06f, 0.935f) },
                { "NeighborDoor_01", ("Door_303", "Frame_Neighbor", true, 2.03f, 0.99f) },
                { "NeighborDoor_02", ("Door_301", "Frame_Neighbor", false, 2.03f, 0.99f) },
            };

        static ApartmentDoorsSetup() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;
            File.Delete(MarkerPath);
            Run();
        }

        [MenuItem("Tools/The Delivery/Apartment - Portas novas")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            if (AssetImporter.GetAtPath(ModelPath) is ModelImporter mi)
            {
                bool dirty = false;
                if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
                if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; dirty = true; }
                if (!Mathf.Approximately(mi.globalScale, 1f)) { mi.globalScale = 1f; dirty = true; }
                if (dirty) mi.SaveAndReimport();
            }
            else { Debug.LogError("[ApartmentDoorsSetup] Doors.fbx não encontrado em " + ModelPath); return; }
            Material mat = RoadCarModelsSetup.BuildMaterial("Doors", ModelDir);
            var meshes = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Mesh>().ToDictionary(m => m.name, m => m);
            Mesh M(string n) => meshes.TryGetValue(n, out var m) ? m : meshes.FirstOrDefault(kv => kv.Key.StartsWith(n)).Value;

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var notes = new List<string>();
            try
            {
                var all = new List<Transform>();
                foreach (var r in scene.GetRootGameObjects()) all.AddRange(r.GetComponentsInChildren<Transform>(true));
                Transform corridor = all.FirstOrDefault(t => t.name == "BuildingCorridor");
                Vector3 corridorCenter = corridor != null ? RendererBounds(corridor).center : Vector3.zero;

                foreach (var kv in Map)
                {
                    Transform door = all.FirstOrDefault(t => t != null && t.name == kv.Key);   // (as maçanetas destruídas viram nulas na lista)
                    if (door == null) { notes.Add(kv.Key + ": não encontrada"); continue; }
                    Transform meshT = door.Find("DoorMesh");
                    if (meshT == null) { notes.Add(kv.Key + ": sem DoorMesh"); continue; }
                    var cfg = kv.Value;

                    // medidas tiradas da folha antiga (só na 1ª vez; depois ficam guardadas no nome da guarnição)
                    Transform frameT = door.parent.Find("DoorFrame_" + kv.Key);
                    Bounds old;
                    if (frameT == null)
                    {
                        var rends = meshT.GetComponentsInChildren<Renderer>(true);
                        old = rends[0].bounds; foreach (var r in rends) old.Encapsulate(r.bounds);
                    }
                    else
                    {
                        var info = frameT.GetComponent<DoorOpeningInfo>();
                        old = new Bounds(info.center, info.size);
                    }
                    bool thinX = old.size.x < old.size.z;
                    Vector3 piv = door.position;
                    Vector3 w = thinX ? new Vector3(0, 0, Mathf.Sign(old.center.z - piv.z)) : new Vector3(Mathf.Sign(old.center.x - piv.x), 0, 0);
                    Vector3 c = thinX ? new Vector3(Mathf.Sign(corridorCenter.x - old.center.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(corridorCenter.z - old.center.z));
                    float yaw = Mathf.Atan2(-w.z, w.x) * Mathf.Rad2Deg;
                    Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                    Vector3 corridorLocal = cfg.corridorPlusZ ? Vector3.forward : Vector3.back;
                    bool faceOk = Vector3.Dot(rot * corridorLocal, c) > 0.5f;

                    float floorY = old.min.y;
                    Vector3 openingCenter = new Vector3(old.center.x, floorY, old.center.z);

                    // --- folha
                    // As portas dos vizinhos eram ProBuilder: o ProBuilderMesh refaz a PRÓPRIA malha por cima da nossa.
                    // Sem ele, a folha nova fica (o MeshFilter/MeshRenderer continuam).
                    foreach (var comp in meshT.GetComponents<Component>())
                        if (comp != null && comp.GetType().Name == "ProBuilderMesh") { Object.DestroyImmediate(comp, true); notes.Add(kv.Key + ": ProBuilder removido"); }
                    Mesh leaf = M(cfg.leaf);
                    var mf = meshT.GetComponent<MeshFilter>(); if (mf == null) mf = meshT.gameObject.AddComponent<MeshFilter>();
                    var mr = meshT.GetComponent<MeshRenderer>(); if (mr == null) mr = meshT.gameObject.AddComponent<MeshRenderer>();
                    mf.sharedMesh = leaf;
                    mr.sharedMaterials = new[] { mat };
                    meshT.localScale = Vector3.one;
                    meshT.rotation = rot;
                    Vector3 hingeEdge = openingCenter - w * (cfg.wo / 2f - JambGap);
                    meshT.position = new Vector3(hingeEdge.x, floorY + FloorGap + cfg.h / 2f, hingeEdge.z);
                    if (thinX) meshT.position = new Vector3(old.center.x, meshT.position.y, meshT.position.z);
                    else meshT.position = new Vector3(meshT.position.x, meshT.position.y, old.center.z);
                    foreach (var col in meshT.GetComponents<Collider>()) Object.DestroyImmediate(col);
                    var box = meshT.gameObject.AddComponent<BoxCollider>();
                    box.center = leaf.bounds.center; box.size = leaf.bounds.size;
                    // tira as maçanetas primitivas (esfera/cilindro) — a folha nova já tem as ferragens
                    int removed = 0;
                    for (int i = door.childCount - 1; i >= 0; i--)
                    {
                        var ch = door.GetChild(i);
                        if (ch.name.StartsWith("DoorHandle")) { Object.DestroyImmediate(ch.gameObject); removed++; }
                    }

                    // --- guarnição (fixa no vão, fora do pivô que gira)
                    if (frameT == null)
                    {
                        frameT = new GameObject("DoorFrame_" + kv.Key).transform;
                        frameT.SetParent(door.parent, true);
                        var info = frameT.gameObject.AddComponent<DoorOpeningInfo>();
                        info.center = old.center; info.size = old.size;
                    }
                    frameT.position = openingCenter;
                    frameT.rotation = rot;
                    frameT.localScale = Vector3.one;
                    var fmf = frameT.GetComponent<MeshFilter>(); if (fmf == null) fmf = frameT.gameObject.AddComponent<MeshFilter>();
                    var fmr = frameT.GetComponent<MeshRenderer>(); if (fmr == null) fmr = frameT.gameObject.AddComponent<MeshRenderer>();
                    fmf.sharedMesh = M(cfg.frame); fmr.sharedMaterials = new[] { mat };
                    GameObjectUtility.SetStaticEditorFlags(frameT.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic);

                    EditorUtility.SetDirty(meshT.gameObject); EditorUtility.SetDirty(frameT.gameObject);
                    notes.Add($"{kv.Key}: {cfg.leaf} yaw {yaw:0} corredor {(faceOk ? "ok" : "AO CONTRÁRIO")}, {removed} maçaneta(s) antiga(s) fora");
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[ApartmentDoorsSetup] OK | " + string.Join(" | ", notes));
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static Bounds RendererBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
