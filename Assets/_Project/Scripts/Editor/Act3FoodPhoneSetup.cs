using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheDelivery.Items;
using TheDelivery.Narrative;
using TheDelivery.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Ato 3 (2026-11-24): o hambúrguer e o pedido no celular.
    /// <list type="bullet">
    /// <item><b>Food.prefab</b> (o lanche na mão, GUID mantido): o hambúrguer do <c>Blender/build_burger.py</c> com 4
    /// estágios (inteiro, 1ª, 2ª e 3ª mordida) + <see cref="FoodBiteStages"/>; o Act3Director avança a cada mordida.</item>
    /// <item><b>BurgerOnWrapper.prefab</b>: o lanche desembrulhado sobre o papel, na bancada (o
    /// <c>unwrappedFoodVisual</c> da cena; substitui o cubo provisório no mesmo lugar).</item>
    /// <item><b>OrderPhoneOverlay</b>: overlay só do Ato 3 com a <see cref="PhoneOrderCutscene"/> (tela inicial →
    /// app The Delivery → lanche → pagar). O Ato 4 continua com o DeadPhoneOverlay (celular apagado).</item>
    /// </list>
    /// Menu "Tools/The Delivery/Ato 3 - Hambúrguer e celular"; marcador <c>Temp/Act3FoodPhoneSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class Act3FoodPhoneSetup
    {
        private const string MarkerPath = "Temp/Act3FoodPhoneSetup.pending";
        private const string ScenePath = "Assets/Scenes/Apartment.unity";
        private const string ModelDir = "Assets/_Project/Models/Burger";
        private const string ModelPath = ModelDir + "/Burger.fbx";
        private const string FoodPrefab = "Assets/_Project/Prefabs/Food.prefab";
        private const string WrapperPrefab = "Assets/_Project/Prefabs/BurgerOnWrapper.prefab";
        private const string PhotoDir = "Assets/_Project/UI/PhoneOrder";
        private const string SfxDir = "Assets/_Project/SoundEffects/";
        private static readonly string[] Stages = { "Burger_Full", "Burger_Bite1", "Burger_Bite2", "Burger_Bite3" };
        private static double nextPoll;

        static Act3FoodPhoneSetup() => EditorApplication.update += AutoRun;

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

        [MenuItem("Tools/The Delivery/Ato 3 - Hambúrguer e celular")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            var notes = new List<string>();

            // ---------- modelo + material
            if (AssetImporter.GetAtPath(ModelPath) is ModelImporter mi)
            {
                bool dirty = false;
                if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
                if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; dirty = true; }
                if (!Mathf.Approximately(mi.globalScale, 1f)) { mi.globalScale = 1f; dirty = true; }
                if (mi.isReadable) { mi.isReadable = false; dirty = true; }
                if (dirty) mi.SaveAndReimport();
            }
            else { Debug.LogError("[Act3FoodPhoneSetup] Burger.fbx não encontrado em " + ModelPath); return; }
            Material mat = RoadCarModelsSetup.BuildMaterial("Burger", ModelDir);
            var meshes = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Mesh>().ToDictionary(m => m.name, m => m);
            Mesh M(string n) => meshes.TryGetValue(n, out var m) ? m : meshes.FirstOrDefault(kv => kv.Key.StartsWith(n)).Value;

            // lado mordido (no espaço do Unity): onde o Bite1 "encolheu" em relação ao inteiro
            Bounds full = M("Burger_Full").bounds, b1 = M("Burger_Bite1").bounds;
            Vector3 bitten = Vector3.zero;
            if (full.max.x - b1.max.x > 0.004f) bitten = Vector3.right;
            else if (b1.min.x - full.min.x > 0.004f) bitten = Vector3.left;
            else if (full.max.z - b1.max.z > 0.004f) bitten = Vector3.forward;
            else if (b1.min.z - full.min.z > 0.004f) bitten = Vector3.back;
            // vira o lado mordido para a câmera (-Z do handAnchor) e um pouco para a esquerda, para a mordida aparecer
            Quaternion faceCam = bitten == Vector3.zero ? Quaternion.identity
                : Quaternion.Euler(0f, 25f, 0f) * Quaternion.FromToRotation(bitten, Vector3.back);
            notes.Add($"lado mordido {bitten}, altura {full.size.y:0.000} m");

            // ---------- Food.prefab (na mão)
            var root = PrefabUtility.LoadPrefabContents(FoodPrefab);
            foreach (var c in root.GetComponents<Component>())
                if (c is MeshFilter || c is MeshRenderer || c is Collider) Object.DestroyImmediate(c);
            for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            root.transform.localScale = Vector3.one;
            var holder = new GameObject("Burger").transform;
            holder.SetParent(root.transform, false);
            holder.localRotation = faceCam;
            // "Encostar no limite da câmera" (2026-11-24): a frente do lanche fica ~1 cm além do plano de corte (0,3 m) e ele
            // é AMPLIADO 2,6x — na tela equivale a um lanche de verdade segurado a ~18 cm do rosto (frente a ~12 cm).
            // Câmera ~0,35 m à frente da origem do CameraHolder; HandAnchor em (0,18; -0,25; 0,80). Centro do lanche a
            // 0,47 m da câmera, ~8 cm à direita e ~7 cm abaixo do olhar.
            const float HeldScale = 2.6f;
            holder.localScale = Vector3.one * HeldScale;
            holder.localPosition = new Vector3(-0.095f, 0.14f - full.center.y * HeldScale, 0.018f);
            var stageObjs = new List<GameObject>();
            foreach (var s in Stages)
            {
                var go = MeshObject(s, holder, M(s), mat);
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // na mão: sem sombra (ampliado, a sombra denunciaria)
                go.SetActive(s == Stages[0]);
                stageObjs.Add(go);
            }
            var stagesComp = root.GetComponent<FoodBiteStages>(); if (stagesComp == null) stagesComp = root.AddComponent<FoodBiteStages>();
            var sso = new SerializedObject(stagesComp);
            var arr = sso.FindProperty("stages"); arr.arraySize = stageObjs.Count;
            for (int i = 0; i < stageObjs.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = stageObjs[i];
            sso.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, FoodPrefab);
            PrefabUtility.UnloadPrefabContents(root);
            notes.Add("Food.prefab = hambúrguer com 4 estágios");

            // ---------- BurgerOnWrapper.prefab (bancada)
            var w = new GameObject("BurgerOnWrapper");
            MeshObject("Wrapper", w.transform, M("Wrapper"), mat);
            var onPaper = MeshObject("Burger", w.transform, M("Burger_Full"), mat);
            onPaper.transform.localPosition = new Vector3(0f, 0.0012f, 0f);
            onPaper.transform.localRotation = Quaternion.Euler(0f, 140f, 0f);
            PrefabUtility.SaveAsPrefabAsset(w, WrapperPrefab);
            Object.DestroyImmediate(w);
            var wrapperAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WrapperPrefab);

            // ---------- fotos do cardápio como sprites
            var photos = new List<Sprite>();
            foreach (var n in new[] { "menu_burger_hero", "menu_burger_front", "menu_burger_top" })
            {
                string p = $"{PhotoDir}/{n}.png";
                if (AssetImporter.GetAtPath(p) is TextureImporter ti)
                {
                    if (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled || !ti.alphaIsTransparency)
                    {
                        ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
                        ti.mipmapEnabled = false; ti.alphaIsTransparency = true; ti.SaveAndReimport();
                    }
                    var sp = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                    if (sp != null) photos.Add(sp);
                }
            }

            // ---------- cena
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Act3Director a3 = null;
                foreach (var r in scene.GetRootGameObjects()) { a3 = r.GetComponentInChildren<Act3Director>(true); if (a3 != null) break; }
                if (a3 == null) { Debug.LogError("[Act3FoodPhoneSetup] Act3Director não encontrado."); return; }
                var so = new SerializedObject(a3);

                // bancada: troca o cubo pelo lanche no papel, no mesmo ponto (apoiado na mesma altura)
                var oldVis = so.FindProperty("unwrappedFoodVisual").objectReferenceValue as GameObject;
                if (oldVis != null && PrefabUtility.GetCorrespondingObjectFromSource(oldVis) != wrapperAsset)
                {
                    bool wasActive = oldVis.activeSelf;
                    oldVis.SetActive(true);
                    var rends = oldVis.GetComponentsInChildren<Renderer>();
                    Bounds bb = rends.Length > 0 ? rends[0].bounds : new Bounds(oldVis.transform.position, Vector3.zero);
                    foreach (var rr in rends) bb.Encapsulate(rr.bounds);
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(wrapperAsset, scene);
                    inst.transform.SetParent(oldVis.transform.parent, true);
                    inst.transform.position = new Vector3(bb.center.x, bb.min.y, bb.center.z);
                    inst.transform.rotation = Quaternion.Euler(0f, oldVis.transform.eulerAngles.y, 0f);
                    inst.name = "UnwrappedBurger";
                    inst.SetActive(wasActive);
                    so.FindProperty("unwrappedFoodVisual").objectReferenceValue = inst;
                    Object.DestroyImmediate(oldVis);
                    notes.Add($"bancada: lanche no papel em {inst.transform.position}");
                }
                else notes.Add("bancada: já era o lanche no papel");

                // celular do pedido: overlay próprio com a cutscene (irmão do DeadPhoneOverlay)
                var dead = so.FindProperty("orderPhoneOverlay").objectReferenceValue as GameObject;
                Transform canvasParent = null;
                if (dead != null && dead.GetComponent<PhoneOrderCutscene>() == null) canvasParent = dead.transform.parent;
                else if (dead != null) canvasParent = dead.transform.parent;
                if (canvasParent != null)
                {
                    var ovT = canvasParent.Find("OrderPhoneOverlay");
                    GameObject ov = ovT != null ? ovT.gameObject : new GameObject("OrderPhoneOverlay", typeof(RectTransform));
                    ov.transform.SetParent(canvasParent, false);
                    var rt = (RectTransform)ov.transform;
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
                    rt.SetAsLastSibling();
                    for (int i = ov.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(ov.transform.GetChild(i).gameObject);
                    var cut = ov.GetComponent<PhoneOrderCutscene>(); if (cut == null) cut = ov.AddComponent<PhoneOrderCutscene>();
                    if (ov.GetComponent<CanvasGroup>() == null) ov.AddComponent<CanvasGroup>();
                    var cso = new SerializedObject(cut);
                    cso.FindProperty("roundedSprite").objectReferenceValue = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                    cso.FindProperty("circleSprite").objectReferenceValue = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
                    var ph = cso.FindProperty("menuPhotos"); ph.arraySize = photos.Count;
                    for (int i = 0; i < photos.Count; i++) ph.GetArrayElementAtIndex(i).objectReferenceValue = photos[i];
                    cso.FindProperty("font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Imports/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
                    var taps = cso.FindProperty("tapClips"); taps.arraySize = 3;
                    for (int i = 0; i < 3; i++) taps.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxDir}PhoneTap_{i + 1:00}.wav");
                    cso.FindProperty("paymentApprovedClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxDir + "PhonePaymentApproved.wav");
                    cso.ApplyModifiedPropertiesWithoutUndo();
                    ov.SetActive(false);
                    so.FindProperty("orderPhoneOverlay").objectReferenceValue = ov;
                    notes.Add($"celular: OrderPhoneOverlay em '{canvasParent.name}' ({photos.Count} fotos)");
                }
                else notes.Add("celular: overlay original não encontrado (orderPhoneOverlay vazio)");

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(a3);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[Act3FoodPhoneSetup] OK | " + string.Join(" | ", notes));
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }
    }
}
