using System.IO;
using System.Linq;
using System.Text;
using TheDelivery.AI;
using TheDelivery.FX;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Pendências do Ato 4 (2026-11-25) na cena Apartment:
    /// - sons, só de arquivos do ElevenLabs (os que ainda faltam ficam como estão até existirem);
    /// - a respiração escondida;
    /// - as texturas da cortina do box e da geladeira.
    /// É idempotente. Marcador Temp/Act4AudioSetup.pending; relatório em Temp/Act4AudioSetup.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class Act4AudioSetup
    {
        private const string Marker = "Temp/Act4AudioSetup.pending";
        private const string ScenePath = "Assets/Scenes/Apartment.unity";
        private const string Sfx = "Assets/_Project/SoundEffects/";
        private static double next;

        static Act4AudioSetup() => EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Marker)) return;
            File.Delete(Marker);
            Run();
        };

        private static StringBuilder log;

        private static AudioClip Clip(string name)
        {
            foreach (var ext in new[] { ".wav", ".mp3", ".ogg" })
            {
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>(Sfx + name + ext);
                if (c != null) return c;
            }
            log.AppendLine("  (sem arquivo ainda: " + name + ")");
            return null;
        }

        /// <summary>Atribui; com valor nulo só limpa se o atual for placeholder (nunca apaga um som final).</summary>
        private static void Set(Object target, string field, Object value, bool forceNull = false)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { log.AppendLine($"  CAMPO NÃO EXISTE: {target.GetType().Name}.{field}"); return; }
            if (value == null && !forceNull) return;
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"  {target.name}.{field} = {(value != null ? value.name : "vazio")}");
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { log.AppendLine($"  CAMPO NÃO EXISTE: {target.GetType().Name}.{field}"); return; }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"  {target.name}.{field} = [{string.Join(", ", values.Select(v => v != null ? v.name : "null"))}]");
        }

        [MenuItem("Tools/The Delivery/Ato 4 - Pendências (som e texturas)")]
        public static void Run()
        {
            log = new StringBuilder();
            AssetDatabase.Refresh();
            Textures();

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !(scene.IsValid() && scene.isLoaded);
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                var dir = all.Select(t => t.GetComponent<Act4Director>()).FirstOrDefault(c => c != null);

                log.AppendLine("Act4Director:");
                // reaproveitamentos (gravações que já estão no projeto)
                Set(dir, "footstepsSound", Clip("Act4KitchenSteps"));
                Set(dir, "footstepsApproachSound", Clip("KillerBootStep_03"));
                Set(dir, "footstepsLeavingSound", Clip("KillerStepLeaving_01"));
                SetArray(dir, "footstepsLeavingSequence", Enumerable.Range(1, 5).Select(i => (Object)Clip($"KillerStepLeaving_{i:00}")).ToArray());
                Set(dir, "headHitSound", Clip("Act4HeadHit"));
                SetArray(dir, "deadPhoneTapSounds", Enumerable.Range(1, 3).Select(i => (Object)Clip($"PhoneTap_{i:00}")).ToArray());
                Set(dir, "phoneDialSound", Clip("PhoneDialRing"));
                Set(dir, "operatorMurmurSound", Clip("OperatorMuffled"));
                // novos do ElevenLabs (entram quando os arquivos existirem)
                Set(dir, "tensionStingSound", Clip("Act4ShadowSting"));
                Set(dir, "shadowJumpscareSound", Clip("JumpscareHit"));
                Set(dir, "loudEchoSound", Clip("Act4KitchenCrash"));
                Set(dir, "distantAntagonistSound", Clip("KillerHumming"));
                Set(dir, "deadLineSound", Clip("Act4LineDead"));
                Set(dir, "lightsOutSound", Clip("Act4PowerOut"));
                Set(dir, "policeSirenSound", Clip("PoliceSirenBR"));

                log.AppendLine("Esconderijos:");
                foreach (var hs in all.Select(t => t.GetComponent<HideSpot>()).Where(h => h != null))
                {
                    string n = hs.name;
                    // guarda-roupa: a própria porta (Door.openClip) já soa ao abrir
                    AudioClip c = n.Contains("Shower") ? Clip("ShowerCurtainPull")
                                : n.Contains("Bed") ? Clip("BedSheets")
                                : n.Contains("Sofa") ? Clip("ClearClothRustle_02") : null;
                    Set(hs, "revealSound", c);
                }
                foreach (var po in all.Select(t => t.GetComponent<HideSpotPullOut>()).Where(h => h != null))
                {
                    Set(po, "doorOpenSound", null, forceNull: true); // cada esconderijo tem o seu (revealSound)
                    Set(po, "grabSound", Clip("GrabNeck"));
                    Set(po, "screamSound", Clip("ClearScream_Grab"));
                }

                log.AppendLine("Viaturas:");
                var engine = Clip("PoliceCarArrive");
                foreach (var car in all.Select(t => t.GetComponent<PoliceCarArrival>()).Where(c => c != null))
                {
                    if (engine == null) { log.AppendLine($"  {car.name}: motor ainda sem arquivo"); continue; }
                    var so = new SerializedObject(car);
                    var p = so.FindProperty("engineAudio");
                    var src = p.objectReferenceValue as AudioSource;
                    if (src == null) { src = car.gameObject.AddComponent<AudioSource>(); p.objectReferenceValue = src; so.ApplyModifiedPropertiesWithoutUndo(); }
                    src.clip = engine; src.loop = true; src.playOnAwake = false;
                    src.spatialBlend = 1f; src.minDistance = 4f; src.maxDistance = 70f; src.rolloffMode = AudioRolloffMode.Linear;
                    log.AppendLine($"  {car.name}.engineAudio = {engine.name}");
                }

                // respiração escondida (raiz própria na raiz da cena, recriada a cada execução)
                foreach (var g in scene.GetRootGameObjects()) if (g.name == "Act4Audio") Object.DestroyImmediate(g);
                var root = new GameObject("Act4Audio");
                SceneManager.MoveGameObjectToScene(root, scene);
                var breath = new GameObject("HidingBreath");
                breath.transform.SetParent(root.transform, false);
                breath.AddComponent<AudioSource>();
                var hb = breath.AddComponent<HidingBreath>();
                Set(hb, "breathLoop", Clip("HidingPanting") ?? Clip("WomanBreathing"));
                // música de perseguição (2026-11-25): o sistema já existe; os clips entram quando forem gerados
                var chase = new GameObject("ChaseMusic"); chase.transform.SetParent(root.transform, false);
                var cm = chase.AddComponent<ChaseMusic>();
                Set(cm, "chaseLoop", Clip("KillerChaseLoop"));
                Set(cm, "chaseSting", Clip("KillerChaseSting"));
                var hso = new SerializedObject(hb); hso.FindProperty("volume").floatValue = 0.35f; hso.ApplyModifiedPropertiesWithoutUndo();

                // 2026-11-25 (pedido do usuário): ao acordar só a luz da sala; de madrugada, sem cidade (buzina) lá fora;
                // a checagem da bateria é a cutscene do celular (o mesmo aparelho do pedido no Ato 3).
                log.AppendLine("Estado ao acordar / celular:");
                all = all.Where(t => t != null).ToList(); // a raiz Act4Audio antiga foi destruída acima
                var living = all.Select(t => t.GetComponent<TheDelivery.Interaction.LightSwitch>())
                                .FirstOrDefault(s => s != null && s.transform.parent != null && s.transform.parent.name == "LivingRoom");
                SetArray(dir, "lightsOnAtWake", living != null ? new Object[] { living } : new Object[0]);
                var cityGuid = AssetDatabase.AssetPathToGUID(Sfx + "RoadCityDistant.ogg");
                var city = all.Select(t => t.GetComponent<AmbientSoundEmitter>())
                              .Where(e => e != null && new SerializedObject(e).FindProperty("loopClip").objectReferenceValue is AudioClip c
                                          && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c)) == cityGuid)
                              .Select(e => (Object)e.gameObject).ToArray();
                SetArray(dir, "ambienceOffInAct4", city);
                var cut = all.Select(t => t.GetComponent<TheDelivery.UI.PhoneOrderCutscene>()).FirstOrDefault(c => c != null);
                Set(dir, "batteryCutscene", cut);
                // Giroflex nas janelas (2026-11-25, pedido do usuário): a ParkedSiren só entrava pela janela da SALA, e a
                // Clear está no box do banheiro e depois no corredor. Clona o rig (SirenLightEffect + spots vermelho/azul)
                // para fora das janelas do banheiro e do quarto, na pose das luzes de relâmpago de cada janela.
                log.AppendLine("Giroflex nas janelas:");
                var parked = all.FirstOrDefault(t => t.name == "ParkedSiren" && t.GetComponent("SirenLightEffect") != null);
                foreach (var g in all.Where(t => t != null && t.name.StartsWith("ParkedSiren_Window_")).Select(t => t.gameObject).ToList())
                    Object.DestroyImmediate(g);
                all = all.Where(t => t != null).ToList();
                var rigs = new System.Collections.Generic.List<Object> { parked != null ? parked.GetComponent("SirenLightEffect") : null };
                if (parked != null)
                {
                    int k = 0;
                    foreach (var (src, nm) in new[] { ("LightningWindow_Bathroom_1", "Bathroom"), ("LightningWindow_Bedroom_4", "Bedroom") })
                    {
                        var anchor = all.FirstOrDefault(t => t.name == src);
                        if (anchor == null) { log.AppendLine("  sem " + src); continue; }
                        var clone = Object.Instantiate(parked.gameObject, parked.parent);
                        clone.name = "ParkedSiren_Window_" + nm;
                        clone.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
                        // os spots filhos olham para a frente do rig (a janela) e ficam lado a lado
                        int c = 0;
                        foreach (var l in clone.GetComponentsInChildren<Light>(true))
                        {
                            l.transform.localPosition = new Vector3(c == 0 ? 0.22f : -0.22f, 0f, 0f);
                            l.transform.localRotation = Quaternion.identity;
                            c++;
                        }
                        var eso = new SerializedObject(clone.GetComponent("SirenLightEffect"));
                        var ph = eso.FindProperty("phaseOffset"); if (ph != null) ph.floatValue = 0.21f * (++k);
                        eso.ApplyModifiedPropertiesWithoutUndo();
                        rigs.Add(clone.GetComponent("SirenLightEffect"));
                        log.AppendLine($"  {clone.name} em {anchor.position:F1}, olhando {anchor.forward:F2}");
                    }
                }
                SetArray(dir, "parkedSirenEffects", rigs.Where(r => r != null).ToArray());

                // roomSwitches precisa ter TODOS os interruptores do apartamento (o corredor do prédio fica de fora)
                var dso = new SerializedObject(dir); var rs = dso.FindProperty("roomSwitches");
                var have = Enumerable.Range(0, rs.arraySize).Select(i => rs.GetArrayElementAtIndex(i).objectReferenceValue).Where(o => o != null).ToList();
                foreach (var sw in all.Select(t => t.GetComponent<TheDelivery.Interaction.LightSwitch>()).Where(s => s != null && s.transform.parent != null && s.transform.parent.name != "BuildingCorridor"))
                    if (!have.Contains(sw)) have.Add(sw);
                SetArray(dir, "roomSwitches", have.ToArray());

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/Act4AudioSetup.txt", log.ToString());
            Debug.Log("[Act4AudioSetup] OK\n" + log);
        }

        private static void Textures()
        {
            log.AppendLine("Texturas:");
            void Imp(string path, bool normal)
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { log.AppendLine("  FALTA " + path); return; }
                var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != want || ti.maxTextureSize != 2048) { ti.textureType = want; ti.maxTextureSize = 2048; ti.SaveAndReimport(); }
            }

            const string ct = "Assets/_Project/Textures/Curtain/";
            Imp(ct + "Curtain_BaseColor.png", false);
            Imp(ct + "Curtain_Normal.png", true);
            var curtain = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Curtain.mat");
            if (curtain != null)
            {
                var b = AssetDatabase.LoadAssetAtPath<Texture2D>(ct + "Curtain_BaseColor.png");
                var n = AssetDatabase.LoadAssetAtPath<Texture2D>(ct + "Curtain_Normal.png");
                curtain.SetTexture("_BaseMap", b); curtain.SetTexture("_MainTex", b);
                curtain.SetTexture("_BumpMap", n); curtain.EnableKeyword("_NORMALMAP");
                curtain.SetColor("_BaseColor", Color.white);
                curtain.SetFloat("_Smoothness", 0.35f); curtain.SetFloat("_Metallic", 0f);
                EditorUtility.SetDirty(curtain);
                log.AppendLine("  Curtain.mat: cor + normal (extraídas de dentro do Curtain.fbx)");
            }

            const string ft = "Assets/_Project/Textures/Fridge/";
            Imp(ft + "Fridge_BaseColor.png", false);
            foreach (var mn in new[] { "Fridge", "Fridge_02" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/_Project/Materials/{mn}.mat");
                if (m == null) continue;
                var b = AssetDatabase.LoadAssetAtPath<Texture2D>(ft + "Fridge_BaseColor.png");
                m.SetTexture("_BaseMap", b); m.SetTexture("_MainTex", b); m.SetColor("_BaseColor", Color.white);
                EditorUtility.SetDirty(m);
                log.AppendLine($"  {mn}.mat: Fridge_BaseColor");
            }
        }
    }
}
