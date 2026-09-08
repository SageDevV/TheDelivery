using TheDelivery.AI;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// PÕE PASSOS NA CRIATURA da perseguição: acrescenta o <see cref="CreatureFootsteps"/>
    /// ao objeto que o PesadeloDirector usa como criatura e o deixa tocando.
    ///
    /// POR QUE NÃO É SÓ "ARRASTA O COMPONENTE": o objeto certo é o que está no campo
    /// Creature Object do director, e ele é UM entre três criaturas na cena (a que persegue,
    /// a do ataque e a da pegada). Pôr os passos na errada dá um resultado que parece
    /// funcionar — o componente existe, não reclama de nada — e não sai som nenhum durante a
    /// fuga, porque aquela criatura está desativada o beat inteiro.
    ///
    /// A Reference Speed é copiada do Creature Speed do próprio director: é a velocidade em
    /// que a criatura de fato anda, e é ela que faz o pitch ficar em 1x na marcha normal.
    /// Deixar os dois números soltos, um em cada Inspector, é convidar a que mudem separados.
    /// </summary>
    public static class PesadeloCreatureFootstepsSetup
    {
        private const string ScenePath = "Assets/Scenes/Pesadelo.unity";
        private const string PlaceholderClipPath = "Assets/_Project/SoundEffects/walking.mp3";

        [MenuItem("Tools/The Delivery/Pesadelo - Passos da Criatura")]
        private static void Run()
        {
            Scene scene = EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            PesadeloDirector director = FindDirector(scene);
            if (director == null)
            {
                Fail("PesadeloDirector não encontrado na cena Pesadelo.");
                return;
            }

            var directorSo = new SerializedObject(director);
            var creature = directorSo.FindProperty("creatureObject")?.objectReferenceValue as GameObject;
            if (creature == null)
            {
                Fail("O campo Creature Object do PesadeloDirector está vazio.\n\n" +
                     "É ele que diz QUAL das criaturas da cena persegue a Clear — sem isso não há em quem pôr os passos.");
                return;
            }

            CreatureFootsteps steps = creature.GetComponent<CreatureFootsteps>();
            bool added = steps == null;
            if (added)
                steps = Undo.AddComponent<CreatureFootsteps>(creature);

            var so = new SerializedObject(steps);

            SerializedProperty reference = so.FindProperty("referenceSpeed");
            SerializedProperty clip = so.FindProperty("walkLoopClip");
            if (reference == null || clip == null)
            {
                Fail("Os campos do CreatureFootsteps ainda não existem. Os scripts estão compilando? " +
                     "Espere a compilação terminar e rode o comando de novo.");
                return;
            }

            // A velocidade da criatura é do director; aqui ela só é COPIADA, para o pitch ter
            // uma régua que corresponde ao movimento real.
            float creatureSpeed = directorSo.FindProperty("creatureSpeed")?.floatValue ?? 2.4f;
            reference.floatValue = creatureSpeed;

            // O clipe só é preenchido quando está VAZIO: rodar o comando de novo não pode
            // desfazer a troca de quem já pôs o som definitivo da criatura no lugar.
            bool wiredClip = false;
            if (clip.objectReferenceValue == null)
            {
                var placeholder = AssetDatabase.LoadAssetAtPath<AudioClip>(PlaceholderClipPath);
                if (placeholder != null)
                {
                    clip.objectReferenceValue = placeholder;
                    wiredClip = true;
                }
            }

            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(steps);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            string resumo =
                $"Criatura: {creature.name}\n" +
                $"Componente: {(added ? "adicionado agora" : "já existia, valores conferidos")}\n" +
                $"Reference Speed: {creatureSpeed:0.##} m/s (copiada do Creature Speed)\n" +
                $"Clipe: {(wiredClip ? "walking.mp3 (PROVISÓRIO — é o dos passos da Clear, pitch 0.7 para pesar)" : (clip.objectReferenceValue != null ? clip.objectReferenceValue.name : "VAZIO — atribua um em Walk Loop Clip"))}";

            Debug.Log($"[PesadeloCreatureFootstepsSetup] Passos montados.\n{resumo}", steps);
            EditorUtility.DisplayDialog("Passos da Criatura",
                                        resumo + "\n\nA cena Pesadelo foi salva.",
                                        "Ok");
        }

        /// <summary>
        /// MEDE ONDE, NO CICLO DA ANIMAÇÃO, CADA PÉ ENCOSTA NO CHÃO e escreve as fases no
        /// Step Phases do <see cref="CreatureFootsteps"/>.
        ///
        /// POR QUE MEDIR EM VEZ DE DIGITAR: a fase do contato é uma propriedade do CLIPE,
        /// invisível no Inspector e impossível de acertar no olho — meio ciclo de erro é o
        /// som saindo com o pé no ar, e o defeito soa como "o áudio está atrasado", que leva
        /// a mexer em tudo menos no número errado. Aqui o clipe é varrido quadro a quadro e
        /// os números saem dele.
        ///
        /// O CONTATO É A DESCIDA, NÃO O PONTO MAIS BAIXO. Um pé plantado fica embaixo
        /// durante meio ciclo (a fase de apoio), então o mínimo cai no MEIO do apoio — tarde
        /// demais. O que faz barulho é a batida: o instante em que o pé CRUZA para baixo a
        /// faixa rasteira. É esse cruzamento que este comando procura.
        ///
        /// A pose é restaurada no fim: a varredura mexe no esqueleto de verdade, e uma
        /// criatura deixada na pose do quadro 137 é uma alteração de cena que ninguém pediu.
        /// </summary>
        [MenuItem("Tools/The Delivery/Pesadelo - Passos da Criatura: medir a cadência")]
        private static void MeasureCadence()
        {
            Scene scene = EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            PesadeloDirector director = FindDirector(scene);
            if (director == null)
            {
                Fail("PesadeloDirector não encontrado na cena Pesadelo.");
                return;
            }

            var directorSo = new SerializedObject(director);
            var creature = directorSo.FindProperty("creatureObject")?.objectReferenceValue as GameObject;
            if (creature == null)
            {
                Fail("O campo Creature Object do PesadeloDirector está vazio.");
                return;
            }

            CreatureFootsteps steps = creature.GetComponent<CreatureFootsteps>();
            if (steps == null)
            {
                Fail("A criatura ainda não tem o CreatureFootsteps.\n\n" +
                     "Rode antes: Tools ▸ The Delivery ▸ Pesadelo - Passos da Criatura.");
                return;
            }

            AnimationClip clip = FindWalkClip(creature);
            if (clip == null)
            {
                Fail("Não achei o clipe da caminhada da criatura (o Animator não tem controller, ou o controller " +
                     "não tem clipe nenhum).");
                return;
            }

            Transform[] feet = FindFeet(creature.transform);
            if (feet.Length == 0)
            {
                Fail("Não achei os ossos dos pés no esqueleto da criatura.\n\n" +
                     "A busca é por nomes contendo \"Foot\" ou \"ToeBase\" (mixamorig:LeftFoot etc.). Se este modelo " +
                     "usa outra nomenclatura, marque as fases à mão no Step Phases — ou ponha Animation Events " +
                     "no clipe chamando Footstep() e use a cadência AnimationEvent.");
                return;
            }

            float[] phases = MeasureContactPhases(creature, clip, feet, out string detail);
            if (phases.Length == 0)
            {
                Fail($"Varri o clipe \"{clip.name}\" e não achei contato nenhum: nenhum pé desce e sobe o " +
                     "suficiente para marcar uma passada.\n\n" + detail +
                     "\n\nO clipe é mesmo a caminhada? Uma pose parada não tem passada para medir.");
                return;
            }

            var so = new SerializedObject(steps);
            SerializedProperty list = so.FindProperty("stepPhases");
            if (list == null)
            {
                Fail("O campo \"stepPhases\" não existe no CreatureFootsteps. Espere a compilação terminar e rode de novo.");
                return;
            }

            list.arraySize = phases.Length;
            for (int i = 0; i < phases.Length; i++)
                list.GetArrayElementAtIndex(i).floatValue = phases[i];

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(steps);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            string lista = string.Join(", ", System.Array.ConvertAll(phases, p => p.ToString("0.###")));
            string resumo = $"Clipe: {clip.name} ({clip.length:0.##}s)\n" +
                            $"Pés medidos: {feet.Length}\n" +
                            $"Fases de contato: {lista}\n\n{detail}";

            Debug.Log($"[PesadeloCreatureFootstepsSetup] Cadência medida.\n{resumo}", steps);
            EditorUtility.DisplayDialog("Cadência dos passos",
                                        resumo + "\n\nEscritas no Step Phases. Elas só valem no modo PerStepClips " +
                                        "com a cadência AnimationCycle.",
                                        "Ok");
        }

        /// <summary>O primeiro clipe do controller do Animator — a caminhada, nos modelos deste projeto.</summary>
        private static AnimationClip FindWalkClip(GameObject creature)
        {
            Animator animator = creature.GetComponentInChildren<Animator>(includeInactive: true);
            RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller == null || controller.animationClips.Length == 0)
                return null;

            return controller.animationClips[0];
        }

        /// <summary>
        /// Os ossos dos pés. Prefere o TOE (a ponta do pé, que é o que toca primeiro numa
        /// caminhada e o que dá o contato mais nítido na medição) e cai no FOOT quando o
        /// modelo não tem osso de dedo.
        /// </summary>
        private static Transform[] FindFeet(Transform root)
        {
            var toes = new System.Collections.Generic.List<Transform>();
            var feet = new System.Collections.Generic.List<Transform>();

            foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                string name = t.name.ToLowerInvariant();
                if (name.Contains("toebase"))
                    toes.Add(t);
                else if (name.EndsWith("foot"))
                    feet.Add(t);
            }

            return toes.Count > 0 ? toes.ToArray() : feet.ToArray();
        }

        /// <summary>
        /// Varre o clipe e devolve as fases (0-1) em que os pés BATEM no chão, em ordem.
        ///
        /// A altura de cada pé é medida no espaço da CRIATURA, e não no mundo: no mundo ela
        /// vem somada ao deslocamento que o clipe carrega no osso do quadril, e o pé de trás
        /// de uma caminhada que anda para a frente terminaria "mais baixo" por estar mais
        /// atrás. No espaço do objeto sobra só o que interessa, que é subir e descer.
        /// </summary>
        private static float[] MeasureContactPhases(GameObject creature, AnimationClip clip, Transform[] feet, out string detail)
        {
            const int Samples = 240;
            // A faixa rasteira: 15% da amplitude vertical daquele pé, contados do chão. Larga
            // o bastante para não depender de um quadro exato, estreita o bastante para não
            // pegar o pé ainda no ar.
            const float ContactBand = 0.15f;

            Transform root = creature.transform;
            SavedTransform[] saved = RecordPose(root);
            bool wasActive = creature.activeSelf;

            var heights = new float[feet.Length][];
            for (int f = 0; f < feet.Length; f++)
                heights[f] = new float[Samples];

            try
            {
                if (!wasActive)
                    creature.SetActive(true);

                for (int i = 0; i < Samples; i++)
                {
                    clip.SampleAnimation(creature, i / (float)Samples * clip.length);

                    for (int f = 0; f < feet.Length; f++)
                        heights[f][i] = root.InverseTransformPoint(feet[f].position).y;
                }
            }
            finally
            {
                RestorePose(saved);
                if (!wasActive)
                    creature.SetActive(false);
            }

            var phases = new System.Collections.Generic.List<float>();
            var report = new System.Text.StringBuilder();

            for (int f = 0; f < feet.Length; f++)
            {
                float min = float.MaxValue;
                float max = float.MinValue;
                foreach (float y in heights[f])
                {
                    if (y < min) min = y;
                    if (y > max) max = y;
                }

                float span = max - min;
                report.AppendLine($"{feet[f].name}: sobe {span:0.###} un. no ciclo");

                // Um pé que mal se mexe não deu passo nenhum — é o modelo em pose parada, ou
                // o osso errado. Medi-lo daria uma fase aleatória, que é pior que nenhuma.
                if (span <= 0.0001f)
                    continue;

                float threshold = min + span * ContactBand;

                for (int i = 0; i < Samples; i++)
                {
                    // Circular: o contato pode acontecer na virada do ciclo, e essa é
                    // justamente a passada que uma varredura linear perderia.
                    float previous = heights[f][(i - 1 + Samples) % Samples];
                    float current = heights[f][i];

                    if (previous > threshold && current <= threshold)
                        phases.Add(i / (float)Samples);
                }
            }

            detail = report.ToString().TrimEnd();

            phases.Sort();

            // Duas descidas quase no mesmo ponto são a mesma batida vista por dois ossos (o
            // pé e o dedo do mesmo lado, quando os dois entram na lista) — vira uma fase só,
            // senão o passo sairia dobrado.
            var merged = new System.Collections.Generic.List<float>();
            foreach (float p in phases)
            {
                if (merged.Count > 0 && Mathf.Abs(p - merged[merged.Count - 1]) < 0.03f)
                    continue;

                merged.Add(p);
            }

            return merged.ToArray();
        }

        private struct SavedTransform
        {
            public Transform transform;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }

        private static SavedTransform[] RecordPose(Transform root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(includeInactive: true);
            var poses = new SavedTransform[all.Length];

            for (int i = 0; i < all.Length; i++)
            {
                poses[i] = new SavedTransform
                {
                    transform = all[i],
                    localPosition = all[i].localPosition,
                    localRotation = all[i].localRotation,
                    localScale = all[i].localScale
                };
            }

            return poses;
        }

        private static void RestorePose(SavedTransform[] poses)
        {
            foreach (SavedTransform pose in poses)
            {
                if (pose.transform == null)
                    continue;

                pose.transform.localPosition = pose.localPosition;
                pose.transform.localRotation = pose.localRotation;
                pose.transform.localScale = pose.localScale;
            }
        }

        private static PesadeloDirector FindDirector(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                PesadeloDirector found = root.GetComponentInChildren<PesadeloDirector>(includeInactive: true);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static Scene EnsureSceneOpen()
        {
            Scene open = EditorSceneManager.GetSceneByPath(ScenePath);
            if (open.IsValid() && open.isLoaded)
                return open;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return default;

            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[PesadeloCreatureFootstepsSetup] {message}");
            EditorUtility.DisplayDialog("Passos da Criatura", message, "Ok");
        }
    }
}
