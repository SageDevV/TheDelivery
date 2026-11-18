using System.IO;
using System.Text;
using TheDelivery.FX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// TRÁFEGO DA RUA (Interlúdio): monta na Road um GameObject "Traffic" com a
    /// <see cref="StreetRoute"/> — o centro das ruas do percurso da Clear, medido nas paredes
    /// invisíveis (StreetCollider) — e oito <see cref="StreetTrafficCar"/>: quatro em cada mão (BBW30 + carros do Meshy),
    /// com os mesmos prefabs e a mesma escala da Cafeteria. Eles param e buzinam se a Clear estiver na pista, e somem
    /// com a noite (toque de recolher ligado ao NightfallController).
    /// IDEMPOTENTE: refaz o "Traffic" do zero. Automação: <c>Temp/RoadTrafficSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadTrafficSetup
    {
        private const string ScenePath = "Assets/Scenes/Road.unity";
        private const string MarkerPath = "Temp/RoadTrafficSetup.pending";
        private const string RootName = "Traffic";
        private const float CafeteriaCarScale = 1.6466801f;

        // Centro das ruas (mundo), nos cruzamentos do percurso: sobe (A), vira à esquerda (B),
        // desce (C), passa pelo estranho (D) e sobe até o prédio (E). Medido nas paredes:
        // A x 234,3..259,7 | B z 363,1..380,7 | C x -36,1..-18,0 | D z 89,9..106,7 | E x -171,2..-152,8.
        private static readonly Vector3[] RoutePoints =
        {
            new Vector3(247.0f, 0f, 252f),
            new Vector3(247.0f, 0f, 371.9f),
            new Vector3(-27.1f, 0f, 371.9f),
            new Vector3(-27.1f, 0f, 98.3f),
            new Vector3(-162.2f, 0f, 98.3f),
            new Vector3(-162.2f, 0f, 220f),
        };

        // Escala: os BBW30 usam a da Cafeteria (modelo cru pequeno); os do Meshy (RoadCarModelsSetup)
        // já vêm em metros, com a frente no +Z.
        private static readonly (string prefab, bool reverse, float delay, float scale)[] Cars =
        {
            ("Assets/_Project/Prefabs/BBW30_NA.prefab", false, 2f, CafeteriaCarScale),
            ("Assets/_Project/Prefabs/CarHatch.prefab", false, 12f, 1f),
            ("Assets/_Project/Prefabs/BBW30_UP.prefab", false, 24f, CafeteriaCarScale),
            ("Assets/_Project/Prefabs/CarWagon.prefab", false, 36f, 1f),
            ("Assets/_Project/Prefabs/BBW30_TuneSample.prefab", true, 1f, CafeteriaCarScale),
            ("Assets/_Project/Prefabs/CarPickup.prefab", true, 9f, 1f),
            ("Assets/_Project/Prefabs/CarSedan.prefab", true, 18f, 1f),
            ("Assets/_Project/Prefabs/BBW30_UP.prefab", true, 30f, CafeteriaCarScale),
        };

        private static double nextPoll;

        static RoadTrafficSetup() => EditorApplication.update += AutoRun;

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
                if (Apply())
                    File.Delete(MarkerPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RoadTrafficSetup] Tentativa interrompida ({e.Message}); tento de novo.");
            }
        }

        [MenuItem("Tools/The Delivery/Road - Tráfego (montar)")]
        private static void Run() => Apply();

        public static bool Apply()
        {
            var missing = new StringBuilder();
            AssetDatabase.Refresh();
            AudioClip pass = CafeteriaAudioSetup.LoadClip("CafeteriaTrafficPass", missing, spatial: true);
            AudioClip horn = CafeteriaAudioSetup.LoadClip("RoadCarHorn", missing, spatial: true);

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (!scene.IsValid())
            {
                Debug.LogError("[RoadTrafficSetup] FALHOU | não abri a cena Road.");
                return false;
            }

            int made = 0;
            try
            {
                NightfallController nightfall = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    nightfall ??= root.GetComponentInChildren<NightfallController>(true);
                    if (root.name == RootName)
                        Object.DestroyImmediate(root);
                }

                var traffic = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(traffic, scene);
                var route = traffic.AddComponent<StreetRoute>();
                var rso = new SerializedObject(route);
                SerializedProperty pts = rso.FindProperty("points");
                pts.arraySize = RoutePoints.Length;
                for (int i = 0; i < RoutePoints.Length; i++)
                    pts.GetArrayElementAtIndex(i).vector3Value = RoutePoints[i];
                rso.ApplyModifiedPropertiesWithoutUndo();

                int index = 0;
                foreach (var (prefabPath, reverse, delay, scale) in Cars)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefab == null)
                    {
                        missing.AppendLine($"  {prefabPath}");
                        continue;
                    }
                    var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    car.name = $"Car_{(reverse ? "Volta" : "Ida")}_{++index}";
                    car.transform.SetParent(traffic.transform, false);
                    // Mesma escala dos carros da Cafeteria (Ato 1): o modelo cru é pequeno demais.
                    car.transform.localScale = Vector3.one * scale;
                    // Parado na origem da pista até largar (fica escondido pelo próprio componente).
                    car.transform.position = reverse ? RoutePoints[RoutePoints.Length - 1] : RoutePoints[0];

                    var stc = car.AddComponent<StreetTrafficCar>();
                    var so = new SerializedObject(stc);
                    so.FindProperty("route").objectReferenceValue = route;
                    so.FindProperty("reverse").boolValue = reverse;
                    so.FindProperty("startDelay").floatValue = delay;
                    so.FindProperty("nightfall").objectReferenceValue = nightfall;
                    so.FindProperty("passSound").objectReferenceValue = pass;
                    so.FindProperty("passVolume").floatValue = 1f;
                    so.FindProperty("passMaxDistance").floatValue = 80f;
                    so.FindProperty("passMinDistance").floatValue = 7f;
                    so.FindProperty("hornSound").objectReferenceValue = horn;
                    so.FindProperty("hornVolume").floatValue = 0.45f;
                    SerializedProperty wheels = so.FindProperty("wheels");
                    var found = TrafficCarSetup.FindWheels(car);
                    wheels.arraySize = found.Count;
                    for (int w = 0; w < found.Count; w++)
                        wheels.GetArrayElementAtIndex(w).objectReferenceValue = found[w];
                    so.ApplyModifiedPropertiesWithoutUndo();
                    made++;
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            Debug.Log($"[RoadTrafficSetup] OK | rota com {RoutePoints.Length} pontos, {made} carros." +
                      (missing.Length > 0 ? "\n  Faltando:\n" + missing : ""));
            return true;
        }
    }
}
