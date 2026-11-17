using System.IO;
using System.Text;
using TheDelivery.FX;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// SONORIZAÇÃO DO INTERLÚDIO (Road) — aplica o mapa J-xx do <c>AudioMap.md</c>:
    /// <list type="bullet">
    /// <item><b>A cidade se apagando</b> (J-02..J-09): um <see cref="NightfallSoundscape"/> com as camadas
    /// presas ao progresso do anoitecer — cidade ao longe, pássaros, vento, tom de silêncio e o zumbido do
    /// poste — e os sons únicos: um carro, o cachorro, o poste acendendo.</item>
    /// <item><b>O estranho</b>: assovio humano novo (J-10), sting na primeira fala (J-13, no asset do
    /// diálogo), tensão da abordagem (J-12/J-14) e a porta do prédio no corte (J-15).</item>
    /// </list>
    /// IDEMPOTENTE. Automação: <c>Temp/RoadAudioSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadAudioSetup
    {
        private const string ScenePath = "Assets/Scenes/Road.unity";
        private const string DialoguePath = "Assets/_Project/ScriptableObjects/Dialogues/Dialogue_ActJourney-1.asset";
        private const string MarkerPath = "Temp/RoadAudioSetup.pending";
        private const string HostName = "NightfallSoundscape";

        private static double nextPoll;

        static RoadAudioSetup() => EditorApplication.update += AutoRun;

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
                if (Apply(interactive: false))
                    File.Delete(MarkerPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RoadAudioSetup] Tentativa interrompida ({e.Message}); tento de novo.");
            }
        }

        [MenuItem("Tools/The Delivery/Road - Sonorização (atribuir áudios)")]
        private static void Run() => Apply(interactive: true);

        public static bool Apply(bool interactive)
        {
            var missing = new StringBuilder();
            AssetDatabase.Refresh();

            // --- J-13: sting na PRIMEIRA fala do estranho ("Você não devia estar na rua hoje.") ---
            var dialogue = AssetDatabase.LoadAssetAtPath<DialogueData>(DialoguePath);
            if (dialogue != null)
            {
                var dso = new SerializedObject(dialogue);
                SerializedProperty lines = dso.FindProperty("lines");
                if (lines != null && lines.arraySize > 0)
                {
                    AudioClip sting = Clip("RoadStrangerSting", missing, spatial: false);
                    if (sting != null)
                        lines.GetArrayElementAtIndex(0).FindPropertyRelative("sfx").objectReferenceValue = sting;
                    dso.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(dialogue);
                }
            }
            else
                missing.AppendLine($"  {DialoguePath}");

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (!scene.IsValid())
            {
                Debug.LogError("[RoadAudioSetup] FALHOU | não abri a cena Road.");
                return false;
            }

            var report = new StringBuilder();
            try
            {
                JourneyDirector director = null;
                NightfallController nightfall = null;
                Transform streetLight = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    director ??= root.GetComponentInChildren<JourneyDirector>(true);
                    nightfall ??= root.GetComponentInChildren<NightfallController>(true);
                    if (streetLight == null)
                        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                            if (t.name.StartsWith("StreetLight")) { streetLight = t; break; }
                }

                // --- O estranho e a chegada (J-10, J-12/J-14, J-15) ---
                if (director != null)
                {
                    var so = new SerializedObject(director);
                    SetClip(so, "leaningWhistle", "RoadStrangerWhistle", missing, spatial: true);
                    SetClip(so, "approachTensionLoop", "RoadApproachTension", missing, spatial: false);
                    SetClip(so, "arrivalDoorSound", "RoadBuildingDoor", missing, spatial: false);
                    so.FindProperty("approachTensionVolume").floatValue = 0.5f;
                    so.FindProperty("arrivalDoorVolume").floatValue = 0.8f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    report.AppendLine("  JourneyDirector: assovio, tensão da abordagem, porta do prédio.");
                }
                else
                    report.AppendLine("  [aviso] JourneyDirector não encontrado.");

                // --- A cidade se apagando (J-02..J-09) ---
                GameObject host = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (root.name == HostName) { host = root; break; }
                if (host == null)
                {
                    host = new GameObject(HostName);
                    SceneManager.MoveGameObjectToScene(host, scene);
                }
                var scape = host.GetComponent<NightfallSoundscape>() ?? host.AddComponent<NightfallSoundscape>();
                var sso = new SerializedObject(scape);
                sso.FindProperty("nightfall").objectReferenceValue = nightfall;

                SerializedProperty layers = sso.FindProperty("layers");
                layers.arraySize = 0;
                //            nome            clipe                 vol   início sobe-de sobe-até some-de some-até âncora         alcance
                AddLayer(layers, "Cidade ao longe", "RoadCityDistant", 0.35f, 1f, 0f, 0f, 0.15f, 0.55f, null, 25f, missing);
                AddLayer(layers, "Pássaros", "RoadDuskBirds", 0.18f, 1f, 0f, 0f, 0.05f, 0.35f, null, 25f, missing);
                AddLayer(layers, "Vento", "RoadWind", 0.30f, 0.35f, 0.1f, 0.9f, 1f, 1f, null, 25f, missing);
                AddLayer(layers, "Tom de silêncio", "RoadSilenceTone", 0.15f, 0f, 0.6f, 0.85f, 1f, 1f, null, 25f, missing);
                // O zumbido do poste é ALTO e AGUDO no arquivo: volume baixo e alcance curto, só perto dele.
                AddLayer(layers, "Zumbido do poste", "RoadStreetLightHum", 0.05f, 0f, 0.6f, 0.62f, 1f, 1f, streetLight, 10f, missing);

                SerializedProperty shots = sso.FindProperty("oneShots");
                shots.arraySize = 0;
                AddShot(shots, "Um carro", "CafeteriaTrafficPass", 0.04f, 0.5f, null, new Vector3(25f, 0f, 15f), 0.8f, 60f, missing);
                AddShot(shots, "Cachorro ao longe", "RoadDistantDog", 0.30f, 0.5f, null, new Vector3(-35f, 3f, 25f), 0.7f, 90f, missing);
                AddShot(shots, "Poste acendendo", "RoadStreetLightOn", 0.60f, 0.5f, streetLight, Vector3.zero, 1f, 25f, missing);
                sso.ApplyModifiedPropertiesWithoutUndo();

                report.AppendLine($"  NightfallSoundscape: 5 camadas, 3 eventos; anoitecer={(nightfall != null ? "ok" : "AUSENTE")}, " +
                                  $"poste={(streetLight != null ? streetLight.name : "AUSENTE")}.");

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            AssetDatabase.SaveAssets();

            string summary = "[RoadAudioSetup] OK\n" + report +
                             (missing.Length > 0 ? "  Faltando:\n" + missing : "  Todos os clipes encontrados.");
            Debug.Log(summary);
            if (interactive)
                EditorUtility.DisplayDialog("Road - Sonorização", summary, "Ok");
            return true;
        }

        private static AudioClip Clip(string name, StringBuilder missing, bool spatial) =>
            CafeteriaAudioSetup.LoadClip(name, missing, spatial);

        private static void SetClip(SerializedObject so, string field, string clip, StringBuilder missing, bool spatial)
        {
            SerializedProperty p = so.FindProperty(field);
            AudioClip c = Clip(clip, missing, spatial);
            if (p != null && c != null)
                p.objectReferenceValue = c;
        }

        private static void AddLayer(SerializedProperty arr, string name, string clip, float volume, float startLevel,
                                     float riseFrom, float riseTo, float fadeFrom, float fadeTo, Transform anchor,
                                     float maxDistance, StringBuilder missing)
        {
            AudioClip c = Clip(clip, missing, spatial: anchor != null);
            arr.arraySize++;
            SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            e.FindPropertyRelative("name").stringValue = name;
            e.FindPropertyRelative("clip").objectReferenceValue = c;
            e.FindPropertyRelative("volume").floatValue = volume;
            e.FindPropertyRelative("startLevel").floatValue = startLevel;
            e.FindPropertyRelative("riseFrom").floatValue = riseFrom;
            e.FindPropertyRelative("riseTo").floatValue = riseTo;
            e.FindPropertyRelative("fadeFrom").floatValue = fadeFrom;
            e.FindPropertyRelative("fadeTo").floatValue = fadeTo;
            e.FindPropertyRelative("anchor").objectReferenceValue = anchor;
            e.FindPropertyRelative("maxDistance").floatValue = maxDistance;
        }

        private static void AddShot(SerializedProperty arr, string name, string clip, float at, float volume,
                                    Transform anchor, Vector3 offset, float blend, float maxDistance, StringBuilder missing)
        {
            AudioClip c = Clip(clip, missing, spatial: true);
            arr.arraySize++;
            SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            e.FindPropertyRelative("name").stringValue = name;
            e.FindPropertyRelative("clip").objectReferenceValue = c;
            e.FindPropertyRelative("atProgress").floatValue = at;
            e.FindPropertyRelative("volume").floatValue = volume;
            e.FindPropertyRelative("anchor").objectReferenceValue = anchor;
            e.FindPropertyRelative("listenerOffset").vector3Value = offset;
            e.FindPropertyRelative("spatialBlend").floatValue = blend;
            e.FindPropertyRelative("maxDistance").floatValue = maxDistance;
        }
    }
}
