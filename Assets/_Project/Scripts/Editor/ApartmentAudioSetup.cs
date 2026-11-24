using System.Collections.Generic;
using System.IO;
using TheDelivery.AI;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// SONORIZAÇÃO DO APARTMENT (Atos 3 e 4), iniciada em 2026-11-23. Mapa completo em Docs/ApartmentMap.md.
    /// Idempotente: roda de novo e só atualiza. O que já monta:
    /// <list type="bullet">
    /// <item><b>Passos do killer</b> (A4-14, crítico; o entregador da cutscene do chamado fica sem passos): <see cref="CreatureFootsteps"/> com
    /// botas em madeira (KillerBootStep_01..06, sintetizados), 3D, um passo a cada passada medida pelo
    /// movimento. Sem isso o jogador escondido não ouve quem o caça.</item>
    /// </list>
    /// A captura (game over) já toca o JumpscareHit pelo GameOver.prefab (vale para o jogo todo).
    /// Menu Tools/The Delivery/Apartment - Sonorização. Automação: Temp/ApartmentAudioSetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class ApartmentAudioSetup
    {
        private const string MarkerPath = "Temp/ApartmentAudioSetup.pending";
        private const string ScenePath = "Assets/Scenes/Apartment.unity";
        private const string Dir = "Assets/_Project/SoundEffects/";
        private const string AudioRoot = "ApartmentAudio";
        private static double nextPoll;

        static ApartmentAudioSetup() => EditorApplication.update += AutoRun;

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

        [MenuItem("Tools/The Delivery/Apartment - Sonorização")]
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

                // quem anda: só o killer (AntagonistAI). O entregador do Ato 3 (Act3Director.deliveryman) chamando
                // a Clear da rua é cutscene: SEM passos, a pedido em 2026-11-23 (remove se uma versão antiga pôs).
                var walkers = new List<GameObject>();
                int removed = 0;
                foreach (var t in all)
                {
                    if (t.GetComponent<AntagonistAI>() != null && !walkers.Contains(t.gameObject)) walkers.Add(t.gameObject);
                }
                foreach (var t in all)
                {
                    var a3 = t.GetComponent<Act3Director>();
                    if (a3 == null) continue;
                    var d = new SerializedObject(a3).FindProperty("deliveryman")?.objectReferenceValue as GameObject;
                    if (d == null || walkers.Contains(d)) continue;
                    walkers.Add(d); removed++;   // 2026-11-24: o entregador ganha passos (corredor e entrega); o Act3Director os cala na sacada
                }

                var boots = new AudioClip[6];
                for (int i = 0; i < 6; i++) boots[i] = C($"KillerBootStep_{i + 1:00}");
                var names = new List<string>();
                foreach (var w in walkers)
                {
                    var fs = w.GetComponent<CreatureFootsteps>();
                    if (fs == null) fs = w.AddComponent<CreatureFootsteps>();
                    var so = new SerializedObject(fs);
                    so.FindProperty("enableFootsteps").boolValue = true;
                    so.FindProperty("footstepMode").intValue = (int)TheDelivery.Player.FootstepMode.PerStepClips;
                    var arr = so.FindProperty("footstepClips"); arr.arraySize = boots.Length;
                    for (int i = 0; i < boots.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = boots[i];
                    so.FindProperty("stepCadence").intValue = (int)FootstepCadence.Distance; // anda por NavMesh/roteiro: o passo segue a distância
                    so.FindProperty("stepStride").floatValue = 0.78f;           // passada de um homem andando
                    so.FindProperty("stepPitchRange").vector2Value = new Vector2(0.94f, 1.05f);
                    so.FindProperty("stepVolume").floatValue = 0.9f;
                    so.FindProperty("basePitch").floatValue = 1f;               // o 0,7 padrão é da criatura do pesadelo
                    so.FindProperty("matchPitchToPace").boolValue = false;
                    so.FindProperty("spatialBlend").floatValue = 1f;
                    so.FindProperty("minDistance").floatValue = 1.5f;
                    so.FindProperty("maxDistance").floatValue = 22f;            // ouvido através de uns dois cômodos
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(fs);
                    names.Add(w.name);
                }

                // A3-17/A3-18: o chamado "Clear… Clear…" que guia até a sacada (ElevenLabs, voz Nassif, tratado
                // como voz da rua) e o sting lento do entregador encarando lá de baixo (sintetizado).
                string callNote = "Act3Director não encontrado";
                Act3Director a3Found = null;
                foreach (var t in all)
                {
                    var a3 = t.GetComponent<Act3Director>();
                    if (a3 == null) continue;
                    var so = new SerializedObject(a3);
                    so.FindProperty("deliverymanCallingClip").objectReferenceValue = C("DeliverymanCallingClear");
                    so.FindProperty("balconyDeliverymanSound").objectReferenceValue = C("BalconyDeliverymanSting");
                    // ETAPA 3 (2026-11-24): a campainha da entrega e a insistente do fenômeno #1 (o mesmo Doorbell.mp3: o familiar vira ameaça)
                    so.FindProperty("doorbellSound").objectReferenceValue = C("ApartmentDoorbell");
                    so.FindProperty("repeatedDoorbellSound").objectReferenceValue = C("ApartmentDoorbellLoop");
                    // ETAPA 4 (2026-11-24): fenômenos do Beat 5 (scratchpad nm_audio/apartment_phenomena.py)
                    so.FindProperty("knockingSound").objectReferenceValue = C("ApartmentKnocking");
                    so.FindProperty("phoneRingSound").objectReferenceValue = C("LandlineRingLoop");
                    so.FindProperty("silentCallBreathSound").objectReferenceValue = C("SilentCallBreath");
                    so.FindProperty("flickerSound").objectReferenceValue = C("LightsRestoreThunk");
                    var steps = so.FindProperty("flickerStepSounds"); steps.arraySize = 4;
                    for (int i = 0; i < 4; i++) steps.GetArrayElementAtIndex(i).objectReferenceValue = C($"FlickerBuzz_{i + 1:00}");
                    so.ApplyModifiedPropertiesWithoutUndo();
                    var src = so.FindProperty("deliverymanCallingSource").objectReferenceValue as AudioSource;
                    if (src != null)
                    {
                        src.clip = C("DeliverymanCallingClear");
                        src.spatialBlend = 1f;
                        src.rolloffMode = AudioRolloffMode.Logarithmic; // linear até 13 m sumia antes de ela chegar perto da sacada
                        src.minDistance = 5f;
                        src.maxDistance = 40f;
                        src.dopplerLevel = 0f;
                        src.volume = 1f;
                        EditorUtility.SetDirty(src);
                    }
                    EditorUtility.SetDirty(a3);
                    callNote = src != null ? "chamado + sting da sacada" : "chamado (sem fonte dedicada) + sting";

                    // ETAPA 6 (2026-11-24): os momentos do Ato 3 que não tinham som (ElevenLabs, scratchpad nm_audio/el_a3_cut.py)
                    so.Update();
                    void Set(string field, string clip) { var c = C(clip); if (c != null) so.FindProperty(field).objectReferenceValue = c; }
                    void SetArr(string field, params string[] clips)
                    {
                        var list = new List<AudioClip>(); foreach (var n in clips) { var c = C(n); if (c != null) list.Add(c); }
                        if (list.Count == 0) return;
                        var p = so.FindProperty(field); p.arraySize = list.Count;
                        for (int i = 0; i < list.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
                    }
                    Set("deliverymanRevealSound", "JumpscareHit");   // o mesmo golpe do zelador (Ato 2), via JumpscareAudio
                    var dm = so.FindProperty("deliveryman").objectReferenceValue as GameObject;
                    if (dm != null) so.FindProperty("deliverymanFootsteps").objectReferenceValue = dm.GetComponent<CreatureFootsteps>();
                    so.FindProperty("goodNightSound").objectReferenceValue = null;   // o sting vai na fala "A noite é longa" (Dialogue_Act3-1)
                    Set("neighborVoiceSound", "NeighborGrunt");
                    SetArr("elevatorArrivalSounds", "ReceptionElevatorDing", "ReceptionElevatorDoorsOpen");
                    Set("apartmentKeysSound", "ApartmentKeysUnlock");
                    Set("phoneOrderSound", "PhoneOrderTaps");
                    Set("foodBagSound", "FoodBagRustle");
                    SetArr("eatBiteSounds", "EatBite_01", "EatBite_02", "EatBite_03");
                    Set("bedSheetsSound", "BedSheets");
                    so.ApplyModifiedPropertiesWithoutUndo();
                    a3Found = a3;
                }
                names.Add(callNote);

                // Falas: o sting no "Vai com calma. A noite é longa" e o drone no "Eu sei onde você mora agora".
                int lines = 0;
                void LineSfx(string asset, int index, AudioClip clip)
                {
                    var d = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/_Project/ScriptableObjects/Dialogues/" + asset + ".asset");
                    if (d == null) { missing.Add(asset); return; }
                    var dso = new SerializedObject(d);
                    SerializedProperty arr = null;
                    var it = dso.GetIterator();
                    while (it.NextVisible(true)) if (it.isArray && it.propertyType == SerializedPropertyType.Generic && it.arraySize > index &&
                                                     it.GetArrayElementAtIndex(index).FindPropertyRelative("sfx") != null) { arr = it.Copy(); break; }
                    if (arr == null) { missing.Add(asset + "[sfx]"); return; }
                    arr.GetArrayElementAtIndex(index).FindPropertyRelative("sfx").objectReferenceValue = clip;
                    dso.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(d); lines++;
                }
                var sting = C("Act3GoodNightSting"); if (sting != null) LineSfx("Dialogue_Act3-1", 2, sting);
                var drone = C("Act3OminousDrone");
                if (drone != null) { LineSfx("Dialogue_Act3-2", 2, drone); LineSfx("Dialogue_Act3-2", 4, null); }
                names.Add($"{lines} fala(s) com som");

                // ETAPA 1 (2026-11-24): room tone, geladeira e interruptores (sintetizados: scratchpad nm_audio/apartment_room.py).
                foreach (var g in scene.GetRootGameObjects()) if (g.name == AudioRoot) Object.DestroyImmediate(g);
                var root = new GameObject(AudioRoot);
                SceneManager.MoveGameObjectToScene(root, scene);

                var amb = new GameObject("ApartmentAmbient"); amb.transform.SetParent(root.transform, false);
                var am = amb.AddComponent<TheDelivery.FX.AmbientMusic>();
                var aso = new SerializedObject(am);
                aso.FindProperty("clip").objectReferenceValue = C("ApartmentRoomTone");
                aso.FindProperty("volume").floatValue = 0.5f;
                aso.FindProperty("playOnStart").boolValue = true;
                aso.FindProperty("fadeInDuration").floatValue = 1.5f;
                aso.ApplyModifiedPropertiesWithoutUndo();

                int fridges = 0;
                foreach (var t in all)
                {
                    if (t == null || t.GetComponent<TheDelivery.Interaction.FridgeDoor>() == null) continue;
                    var b = new Bounds(t.position, Vector3.zero);
                    foreach (var r in t.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                    var go = new GameObject("FridgeHum_" + (++fridges)); go.transform.SetParent(root.transform, false);
                    go.transform.position = b.center;
                    var e = go.AddComponent<TheDelivery.FX.AmbientSoundEmitter>();
                    var eso = new SerializedObject(e);
                    eso.FindProperty("loopClip").objectReferenceValue = C("ApartmentFridgeHum");
                    eso.FindProperty("loopVolume").floatValue = 0.16f;
                    eso.FindProperty("minDistance").floatValue = 0.8f;
                    eso.FindProperty("maxDistance").floatValue = 7f;
                    eso.ApplyModifiedPropertiesWithoutUndo();
                }

                // ETAPA 6: ambientes 3D do Ato 3 — corredor (luminária, REUSO da Recepção), TV abafada do vizinho e a rua na sacada (REUSO da Road)
                int ambs = 0;
                void Emitter(string name, Vector3 pos, string clip, float vol, float min, float max)
                {
                    var c = C(clip); if (c == null) return;
                    var go = new GameObject(name); go.transform.SetParent(root.transform, false); go.transform.position = pos;
                    var e = go.AddComponent<TheDelivery.FX.AmbientSoundEmitter>();
                    var eso = new SerializedObject(e);
                    eso.FindProperty("loopClip").objectReferenceValue = c;
                    eso.FindProperty("loopVolume").floatValue = vol;
                    eso.FindProperty("minDistance").floatValue = min;
                    eso.FindProperty("maxDistance").floatValue = max;
                    eso.ApplyModifiedPropertiesWithoutUndo(); ambs++;
                }
                if (a3Found != null)
                {
                    var a3so = new SerializedObject(a3Found);
                    Transform Ref(string f) { var o = a3so.FindProperty(f)?.objectReferenceValue; return o is Component cc ? cc.transform : o as Transform; }
                    var cross = Ref("deliverymanCrossPoint");
                    if (cross != null) Emitter("CorridorFluorescent", cross.position + Vector3.up * 2.4f, "ReceptionFluorescentBuzz", 0.18f, 1.5f, 9f);
                    var nd = Ref("neighborDoor");
                    if (nd != null) Emitter("NeighborTV", nd.position + Vector3.up * 1.2f, "NeighborTVMuffled", 0.35f, 1f, 8f);
                    var bal = Ref("balconyPoint");
                    if (bal != null)
                    {
                        // a rua: entre a sacada e o ponto onde o entregador fica lá embaixo (fora do apê)
                        var street = Ref("deliverymanBalconyPoint");
                        Vector3 outside = street != null ? Vector3.Lerp(bal.position, street.position, 0.35f) : bal.position;
                        Emitter("BalconyCity", outside + Vector3.up * 1.5f, "RoadCityDistant", 0.35f, 3f, 10f);
                        Emitter("BalconyWind", bal.position + Vector3.up * 1.5f, "RoadWind", 0.22f, 1.5f, 6f);
                    }
                }
                names.Add($"{ambs} ambiente(s) 3D do Ato 3");

                var clicks = new[] { C("LightSwitchClick_01"), C("LightSwitchClick_02"), C("LightSwitchClick_03") };
                var deads = new[] { C("LightSwitchDead_01"), C("LightSwitchDead_02") };
                int switches = 0;
                foreach (var t in all)
                {
                    if (t == null) continue;
                    foreach (var mb in new MonoBehaviour[] { t.GetComponent<TheDelivery.Interaction.LightSwitch>(), t.GetComponent<TheDelivery.Interaction.TableLamp>(), t.GetComponent<TheDelivery.FX.TVScreen>() })
                    {
                        if (mb == null) continue;
                        var so = new SerializedObject(mb);
                        var src = so.FindProperty("audioSource").objectReferenceValue as AudioSource;
                        if (src == null)
                        {
                            src = mb.GetComponent<AudioSource>();
                            if (src == null) src = mb.gameObject.AddComponent<AudioSource>();
                            so.FindProperty("audioSource").objectReferenceValue = src;
                        }
                        if (src.clip == null)   // a fonte é só dos cliques (a da TV toca o vídeo por outro caminho)
                        {
                            src.playOnAwake = false;
                            src.spatialBlend = 1f;
                            src.rolloffMode = AudioRolloffMode.Logarithmic;
                            src.minDistance = 0.6f;
                            src.maxDistance = 8f;
                            src.dopplerLevel = 0f;
                        }
                        so.FindProperty("toggleClip").objectReferenceValue = clicks[switches % clicks.Length];
                        var np = so.FindProperty("noPowerClip");
                        if (np != null) np.objectReferenceValue = deads[switches % deads.Length];
                        so.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(mb);
                        switches++;
                    }
                }
                names.Add($"room tone + {fridges} geladeira(s) + {switches} interruptor(es)/abajur(es)/TV");

                // ETAPA 2 (2026-11-24): portas e geladeira (scratchpad nm_audio/apartment_doors.py). O jogo toca o
                // som no início da animação; o trinco/baque de fechar já vem no fim dela (0,9 s porta, 0,8 s geladeira).
                AudioSource Src3D(MonoBehaviour mb, SerializedObject so, float max)
                {
                    var src = so.FindProperty("audioSource").objectReferenceValue as AudioSource;
                    if (src == null)
                    {
                        src = mb.GetComponent<AudioSource>();
                        if (src == null) src = mb.gameObject.AddComponent<AudioSource>();
                        so.FindProperty("audioSource").objectReferenceValue = src;
                    }
                    src.playOnAwake = false; src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Logarithmic;
                    src.minDistance = 1f; src.maxDistance = max; src.dopplerLevel = 0f;
                    EditorUtility.SetDirty(src);
                    return src;
                }
                int doors = 0, fdoors = 0;
                foreach (var t in all)
                {
                    if (t == null) continue;
                    var d = t.GetComponent<TheDelivery.Interaction.Door>();
                    if (d != null)
                    {
                        var so = new SerializedObject(d);
                        Src3D(d, so, 16f);
                        bool neighborDoor = !so.FindProperty("playerCanOpen").boolValue; // a do vizinho: só o roteiro abre, com corrente
                        so.FindProperty("openClip").objectReferenceValue = C(neighborDoor ? "NeighborDoorChainOpen" : "ApartmentDoorOpen");
                        so.FindProperty("closeClip").objectReferenceValue = C(neighborDoor ? "NeighborDoorChainClose" : "ApartmentDoorClose");
                        so.FindProperty("lockedClip").objectReferenceValue = C("ApartmentDoorLocked");
                        so.FindProperty("knockClip").objectReferenceValue = C("ApartmentDoorKnock");
                        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(d); doors++;
                    }
                    var f = t.GetComponent<TheDelivery.Interaction.FridgeDoor>();
                    if (f != null)
                    {
                        var so = new SerializedObject(f);
                        Src3D(f, so, 10f);
                        so.FindProperty("openClip").objectReferenceValue = C("FridgeDoorOpen");
                        so.FindProperty("closeClip").objectReferenceValue = C("FridgeDoorClose");
                        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(f); fdoors++;
                    }
                }
                names.Add($"{doors} porta(s) + {fdoors} porta(s) de geladeira");

                // ETAPA 4: as lâmpadas que piscam sozinhas (FlickeringLight) usam o mesmo estalo/zumbido grave
                int flick = 0;
                foreach (var t in all)
                {
                    var fl = t != null ? t.GetComponent<TheDelivery.FX.FlickeringLight>() : null;
                    if (fl == null) continue;
                    var so = new SerializedObject(fl);
                    var bs = so.FindProperty("buzzSource").objectReferenceValue as AudioSource;
                    if (bs == null)
                    {
                        bs = fl.GetComponent<AudioSource>(); if (bs == null) bs = fl.gameObject.AddComponent<AudioSource>();
                        bs.playOnAwake = false; bs.spatialBlend = 1f; bs.rolloffMode = AudioRolloffMode.Logarithmic;
                        bs.minDistance = 1f; bs.maxDistance = 10f; bs.dopplerLevel = 0f;
                        so.FindProperty("buzzSource").objectReferenceValue = bs;
                    }
                    var arr = so.FindProperty("buzzClips"); arr.arraySize = 4;
                    for (int i = 0; i < 4; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = C($"FlickerBuzz_{i + 1:00}");
                    so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(fl); flick++;
                }
                names.Add($"fenômenos do Beat 5 + {flick} lâmpada(s) piscando");
                names.Add($"passos no entregador: {removed}");

                // ETAPA 5: chuva e trovões do ElevenLabs no RainController (os clipes substituem o sintetizador)
                foreach (var t in all)
                {
                    var rc = t != null ? t.GetComponent<TheDelivery.FX.RainController>() : null;
                    if (rc == null) continue;
                    var so = new SerializedObject(rc);
                    var th = rc.transform.Find("RainThunder");
                    if (th == null) { th = new GameObject("RainThunder").transform; th.SetParent(rc.transform, false); }
                    var tsrc = th.GetComponent<AudioSource>(); if (tsrc == null) tsrc = th.gameObject.AddComponent<AudioSource>();
                    tsrc.playOnAwake = false; tsrc.loop = false; tsrc.spatialBlend = 0f; tsrc.volume = 0.8f; tsrc.dopplerLevel = 0f;
                    so.FindProperty("thunderSource").objectReferenceValue = tsrc;
                    var thc = so.FindProperty("thunderClips"); thc.arraySize = 2;
                    for (int i = 0; i < 2; i++) thc.GetArrayElementAtIndex(i).objectReferenceValue = C($"RainThunder_{i + 1:00}");
                    var loopClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + "RainLoop.wav");   // opcional até o take sair
                    if (loopClip != null)
                    {
                        so.FindProperty("rainLoopClip").objectReferenceValue = loopClip;
                        var cs = so.FindProperty("clipSource").objectReferenceValue as AudioSource;
                        if (cs == null)
                        {
                            var lt = rc.transform.Find("RainLoop");
                            if (lt == null) { lt = new GameObject("RainLoop").transform; lt.SetParent(rc.transform, false); }
                            cs = lt.GetComponent<AudioSource>(); if (cs == null) cs = lt.gameObject.AddComponent<AudioSource>();
                            so.FindProperty("clipSource").objectReferenceValue = cs;
                        }
                        cs.playOnAwake = false; cs.spatialBlend = 0f; cs.dopplerLevel = 0f;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(rc);
                    names.Add($"chuva: 2 trovões{(loopClip != null ? " + loop" : " (loop ainda sintetizado)")}");
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log($"[ApartmentAudioSetup] OK | passos de bota em: {string.Join(", ", names)}" +
                          (missing.Count > 0 ? " | FALTANDO: " + string.Join(", ", missing) : ""));
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static void ConfigureImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/SoundEffects" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string n = Path.GetFileNameWithoutExtension(path);
                bool loop = n == "ApartmentRoomTone" || n == "ApartmentFridgeHum" || n == "RainLoop" || n.StartsWith("RainThunder") || n == "NeighborTVMuffled" || n == "Act3OminousDrone";   // longos: Vorbis
                if (!(n.StartsWith("KillerBootStep") || n == "DeliverymanCallingClear" || n == "BalconyDeliverymanSting" ||
                      n.StartsWith("LightSwitch") || n.StartsWith("ApartmentDoor") || n.StartsWith("FridgeDoor") || n == "ApartmentKnocking" || n == "LandlineRingLoop" ||
                      n == "SilentCallBreath" || n.StartsWith("Act3") || n == "NeighborGrunt" || n == "ApartmentKeysUnlock" || n == "PhoneOrderTaps" || n == "FoodBagRustle" || n.StartsWith("EatBite") || n == "BedSheets" || n.StartsWith("FlickerBuzz") || n == "LightsRestoreThunk" || n.StartsWith("NeighborDoorChain") || loop)) continue;
                if (!(AssetImporter.GetAtPath(path) is AudioImporter ai)) continue;
                var s = ai.defaultSampleSettings;
                s.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = loop ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
                if (loop) s.quality = 0.6f;
                s.preloadAudioData = true;
                ai.defaultSampleSettings = s;
                ai.forceToMono = n != "ApartmentRoomTone";   // o room tone é estéreo largo (2D)
                ai.SaveAndReimport();
            }
        }
    }
}
