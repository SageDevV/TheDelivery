using System.Collections.Generic;
using System.IO;
using TheDelivery.FX;
using TheDelivery.Interaction;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// SONORIZAÇÃO DO ATO 2 — RECEPÇÃO (2026-11-23). A cena não tinha nenhuma fonte de som. Monta, de forma
    /// IDEMPOTENTE (roda de novo e só atualiza):
    /// <list type="bullet">
    /// <item>o ROOM TONE do saguão (AmbientMusic: reator das fluorescentes, ventilação) — A2-01;</item>
    /// <item>emissores 3D: zumbido em cada luminária, relógio e rádio entre estações no balcão (A2-02), vento
    /// atrás da porta de vidro (abafado), freezer na sala de produtos e o zelador trabalhando atrás da porta
    /// (A2-06);</item>
    /// <item>no Act2Director: o telefone do balcão, o balde tombando e a sola chiando no susto (A2-04), a
    /// sequência do elevador (A2-07);</item>
    /// <item>o rangido da porta da sala (A2-03), os stings da conversa do Jef (A2-05);</item>
    /// <item>no Player.prefab (os passos são os de sempre), o fôlego e a roupa da Clear
    /// (ClearBodySounds, G-03/G-04 — vale para o jogo todo).</item>
    /// </list>
    /// Os sons foram sintetizados (scratchpad nm_audio/reception_sfx.py) — sem créditos. Para trocar um por
    /// outro, salve um arquivo com o mesmo nome em SoundEffects e rode de novo.
    /// Menu Tools/The Delivery/Reception - Sonorização. Automação: Temp/ReceptionAudioSetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class ReceptionAudioSetup
    {
        private const string MarkerPath = "Temp/ReceptionAudioSetup.pending";
        private const string ScenePath = "Assets/Scenes/Reception.unity";
        private const string PlayerPrefab = "Assets/_Project/Prefabs/Player.prefab";
        private const string Dir = "Assets/_Project/SoundEffects/";
        private const string DialoguePath = "Assets/_Project/ScriptableObjects/Dialogues/Dialogue_Act2-1.asset";
        private const string Root = "_Audio";
        private static double nextPoll;

        static ReceptionAudioSetup() => EditorApplication.update += AutoRun;

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

        [MenuItem("Tools/The Delivery/Reception - Sonorização")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            var missing = new List<string>();
            AudioClip C(string name)
            {
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + name + ".wav")
                     ?? AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + name + ".mp3")
                     ?? AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + name + ".ogg");
                if (c == null) missing.Add(name);
                return c;
            }
            ConfigureImports();

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var all = new List<Transform>();
                foreach (var r in scene.GetRootGameObjects()) all.AddRange(r.GetComponentsInChildren<Transform>(true));
                Transform Find(string n) => all.Find(t => t.name == n);
                Vector3 Center(Transform t)
                {
                    var rs = t.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) return t.position;
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b.center;
                }
                Vector3 Top(Transform t)
                {
                    var rs = t.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) return t.position;
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return new Vector3(b.center.x, b.max.y, b.center.z);
                }

                Act2Director director = null; ProductsRoomDoor door = null; PlayerController player = null;
                foreach (var t in all)
                {
                    if (director == null) director = t.GetComponent<Act2Director>();
                    if (door == null) door = t.GetComponent<ProductsRoomDoor>();
                    if (player == null) player = t.GetComponent<PlayerController>();
                }
                if (director == null) { Debug.LogError("[ReceptionAudioSetup] Act2Director não encontrado."); return; }

                // raiz única do áudio (refeita a cada execução: idempotente)
                var old = scene.GetRootGameObjects();
                foreach (var g in old) if (g.name == Root) Object.DestroyImmediate(g);
                var root = new GameObject(Root);
                SceneManager.MoveGameObjectToScene(root, scene);

                // A2-01: room tone
                var amb = new GameObject("ReceptionAmbient"); amb.transform.SetParent(root.transform, false);
                var am = amb.AddComponent<AmbientMusic>();
                var aso = new SerializedObject(am);
                aso.FindProperty("clip").objectReferenceValue = C("ReceptionRoomTone");
                aso.FindProperty("volume").floatValue = 0.55f;
                aso.FindProperty("playOnStart").boolValue = true;
                aso.FindProperty("fadeInDuration").floatValue = 1.2f;
                aso.ApplyModifiedPropertiesWithoutUndo();

                GameObject Emitter(string name, Vector3 at, AudioClip loop, float vol, float min, float max)
                {
                    var go = new GameObject(name); go.transform.SetParent(root.transform, false); go.transform.position = at;
                    var e = go.AddComponent<AmbientSoundEmitter>();
                    var so = new SerializedObject(e);
                    so.FindProperty("loopClip").objectReferenceValue = loop;
                    so.FindProperty("loopVolume").floatValue = vol;
                    so.FindProperty("minDistance").floatValue = min;
                    so.FindProperty("maxDistance").floatValue = max;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    return go;
                }

                // zumbido em cada luminária
                int lamps = 0;
                foreach (var t in all)
                    if (t != null && t.name == "CeilingLamp" && t.parent != null && t.parent.name != "CeilingLamp")
                        Emitter("FluorescentHum_" + (++lamps), Center(t), C("ReceptionFluorescentBuzz"), 0.13f, 0.8f, 6.5f);

                // balcão: o rádio entre estações e o ponto do telefone (A2-02). O relógio saiu do ato a pedido (2026-11-23).
                Transform desk = Find("ReceptionDesk");
                Vector3 deskTop = desk != null ? Top(desk) : director.transform.position;
                Emitter("DeskRadio", deskTop + Vector3.up * 0.05f, C("ReceptionDeskRadio"), 0.17f, 0.8f, 7f);
                var phonePoint = new GameObject("DeskPhone"); phonePoint.transform.SetParent(root.transform, false);
                phonePoint.transform.position = deskTop + Vector3.up * 0.1f;

                // vento atrás da porta de vidro, abafado pelo vidro
                Transform glass = Find("GlassDoor");
                if (glass != null)
                {
                    var wind = Emitter("GlassDoorWind", Center(glass), C("RoadWind"), 0.28f, 1.5f, 10f);
                    // o filtro exige uma fonte no objeto já no Editor (o emissor só cria a dele em Play);
                    // filtro de áudio vale para todas as fontes do mesmo objeto
                    var holder = wind.AddComponent<AudioSource>(); holder.playOnAwake = false;
                    var lp = wind.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 750f;
                }

                // sala de produtos: freezer e o zelador trabalhando
                // (o freezer reaproveitado da Cafeteria saiu em 2026-11-23: o loop tem assovios de compressor em 1,2-4,4 kHz)
                Transform janitor = Find("Janitor");
                GameObject work = janitor != null ? Emitter("JanitorWork", janitor.position + Vector3.up * 0.6f, C("ReceptionJanitorMop"), 1f, 3f, 14f) : null; // a pista que faz abrir a porta: tem que ser ouvida

                // G-02: SEM passos próprios — a Clear usa os mesmos passos do jogo todo (Walking, do Player.prefab).
                // O piso frio (ClearFootTile_*) foi retirado a pedido em 2026-11-23.

                // diretor: telefone, susto, elevador
                Transform elevator = Find("Elevator");
                var dso = new SerializedObject(director);
                dso.FindProperty("deskPhoneSound").objectReferenceValue = C("ReceptionDeskPhoneRing");
                dso.FindProperty("deskPhonePoint").objectReferenceValue = phonePoint.transform;
                dso.FindProperty("deskPhoneVolume").floatValue = 0.42f; // estranheza, não susto: muito abaixo do golpe
                dso.FindProperty("scareVolume").floatValue = 0.8f;      // o balde (3D)
                dso.FindProperty("scareSound").objectReferenceValue = C("ReceptionJanitorScare");
                dso.FindProperty("scareHitSound").objectReferenceValue = C("JumpscareHit"); // o susto-base do jogo (2D), sem variação da cena
                dso.FindProperty("scareHitVolume").floatValue = 1f;
                dso.FindProperty("janitorSlideSound").objectReferenceValue = C("ReceptionJanitorSlide");
                dso.FindProperty("janitorWorkSound").objectReferenceValue = work;
                dso.FindProperty("elevatorButtonSound").objectReferenceValue = C("ReceptionElevatorButton");
                dso.FindProperty("elevatorDingSound").objectReferenceValue = C("ReceptionElevatorDing");
                dso.FindProperty("elevatorDoorsOpenSound").objectReferenceValue = C("ReceptionElevatorDoorsOpen");
                dso.FindProperty("elevatorDoorsCloseSound").objectReferenceValue = C("ReceptionElevatorDoorsClose");
                dso.FindProperty("elevatorRideSound").objectReferenceValue = C("ReceptionElevatorRide");
                dso.FindProperty("elevatorSoundPoint").objectReferenceValue = elevator;
                dso.ApplyModifiedPropertiesWithoutUndo();

                // A2-03: rangido da porta (abre devagar, junto com o rangido; o susto entra com ela entreaberta)
                if (door != null)
                {
                    var pso = new SerializedObject(door);
                    pso.FindProperty("openSound").objectReferenceValue = C("ReceptionProductsDoorCreak");
                    pso.FindProperty("openVolume").floatValue = 0.85f;
                    pso.FindProperty("openDuration").floatValue = 1.7f;
                    pso.FindProperty("notifyDelay").floatValue = 0.9f;
                    pso.ApplyModifiedPropertiesWithoutUndo();
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);

                // A2-05: stings da conversa do Jef ("a pessoa some" e "...eu não disse meu nome.")
                var dlg = AssetDatabase.LoadAssetAtPath<DialogueData>(DialoguePath);
                string dlgNote = "diálogo não encontrado";
                if (dlg != null)
                {
                    var gso = new SerializedObject(dlg);
                    var lines = gso.FindProperty("lines");
                    if (lines != null && lines.arraySize >= 10)
                    {
                        lines.GetArrayElementAtIndex(4).FindPropertyRelative("sfx").objectReferenceValue = C("ReceptionOminousLine");
                        lines.GetArrayElementAtIndex(9).FindPropertyRelative("sfx").objectReferenceValue = C("ReceptionNameSting");
                        gso.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(dlg);
                        dlgNote = "stings nas falas 5 e 10";
                    }
                    else dlgNote = "diálogo com formato inesperado";
                }

                // G-03/G-04: fôlego e roupa da Clear no Player.prefab (o jogo todo)
                string bodyNote = SetupPlayerBody(C("ClearPanting"), new[] { C("ClearClothRustle_01"), C("ClearClothRustle_02"), C("ClearClothRustle_03") });
                AssetDatabase.SaveAssets();

                Debug.Log($"[ReceptionAudioSetup] OK | {lamps} luminárias; rádio (sem relógio), vento, zelador (sem freezer); diretor (telefone, susto em 2 camadas, elevador); porta; {dlgNote}; passos; {bodyNote}" +
                          (missing.Count > 0 ? " | FALTANDO: " + string.Join(", ", missing) : ""));
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static string SetupPlayerBody(AudioClip breath, AudioClip[] cloth)
        {
            var prefabRoot = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var pc = prefabRoot.GetComponentInChildren<PlayerController>(true);
                if (pc == null) return "Player.prefab sem PlayerController";
                var body = pc.GetComponent<ClearBodySounds>();
                if (body == null) body = pc.gameObject.AddComponent<ClearBodySounds>();
                var so = new SerializedObject(body);
                so.FindProperty("player").objectReferenceValue = pc;
                so.FindProperty("breathLoop").objectReferenceValue = breath;
                var arr = so.FindProperty("clothClips"); arr.arraySize = cloth.Length;
                for (int i = 0; i < cloth.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = cloth[i];
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlayerPrefab);
                return "ClearBodySounds no Player.prefab";
            }
            finally { PrefabUtility.UnloadPrefabContents(prefabRoot); }
        }

        // loops longos comprimidos (Vorbis); one-shots curtos descomprimidos ao carregar (sem atraso no disparo)
        private static void ConfigureImports()
        {
            string[] loops = { "ReceptionRoomTone", "ReceptionFluorescentBuzz", "ReceptionDeskRadio", "ReceptionJanitorMop", "ReceptionElevatorRide" };
            string[] pcm = { "ReceptionJanitorScare", "JumpscareHit" };   // o susto sem ruído de ADPCM na cauda
            string[] stereo = { "ReceptionRoomTone", "JumpscareHit" };   // 2D largos
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/SoundEffects" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string n = Path.GetFileNameWithoutExtension(path);
                if (!(n.StartsWith("Reception") || n.StartsWith("ClearFootTile") || n.StartsWith("ClearClothRustle") || n == "JumpscareHit")) continue;
                if (!(AssetImporter.GetAtPath(path) is AudioImporter ai)) continue;
                var s = ai.defaultSampleSettings;
                bool loop = System.Array.IndexOf(loops, n) >= 0;
                s.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = loop ? AudioCompressionFormat.Vorbis : System.Array.IndexOf(pcm, n) >= 0 ? AudioCompressionFormat.PCM : AudioCompressionFormat.ADPCM;
                s.quality = loop ? 0.55f : 1f;
                s.preloadAudioData = true; // carregado com a cena: um one-shot sem preload só começa a carregar no Play (o susto atrasou 1,3 s)
                ai.defaultSampleSettings = s;
                ai.forceToMono = System.Array.IndexOf(stereo, n) < 0;
                ai.SaveAndReimport();
            }
        }
    }
}
