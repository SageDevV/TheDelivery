using System.Text;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// MONTA O ENCONTRO COM O LEANING no <see cref="PercursoDirector"/>: cria a conversa
    /// e os pensamentos, cria o ponto vazio que dispara a abordagem na rua e liga tudo
    /// nos campos do director de uma vez.
    ///
    /// POR QUE ISTO É UM COMANDO E NÃO UM PARÁGRAFO DE INSTRUÇÕES: são seis referências
    /// entre dois objetos e três assets, e uma referência arrastada à mão numa cena que
    /// alguém pode descartar não é uma montagem — é uma lembrança. Rodar o menu de novo
    /// reconstrói o beat inteiro, sem duplicar nada.
    ///
    /// ELE TAMBÉM LIMPA A VERSÃO ANTIGA: enquanto o beat viveu em componentes próprios
    /// (<c>LeaningEncounter</c> e <c>LeaningEncounterTrigger</c>), o gatilho era um
    /// BoxCollider. Agora quem vigia é o director, por raio, igual ao Destino — então o
    /// collider e os scripts que sobraram na cena são removidos aqui, inclusive quando
    /// já estão como "missing script".
    ///
    /// ONDE ELE COLOCA O GATILHO, e por que ali: o ponto nasce sobre a ROTA (a reta
    /// entre o Spawn e o Destino), na altura do Leaning e recuado
    /// <see cref="TriggerBackOff"/> metros na direção de onde a Clear vem — perto o
    /// bastante para ela ver o estranho, longe o bastante para a caminhada até ele
    /// significar alguma coisa. É um CHUTE EDUCADO, não uma decisão final: arraste o
    /// ponto na Scene view até o beat começar na hora certa (o gizmo laranja do
    /// director desenha o raio dele).
    ///
    /// O QUE ELE FALA NÃO É DAQUI depois da primeira execução: o texto vira asset
    /// (<see cref="DialoguePath"/>) e se edita no Inspector, como todas as outras
    /// conversas do jogo. Rodar o menu de novo NÃO sobrescreve um asset já existente.
    /// </summary>
    public static class LeaningEncounterSetup
    {
        private const string ScenePath = "Assets/Scenes/Estrada.unity";
        private const string DialoguePath = "Assets/_Project/ScriptableObjects/Dialogues/Dialogue_ActPer-1.asset";
        private const string NoticeThoughtPath = "Assets/_Project/ScriptableObjects/Thoughts/Thought_ActPer-8.asset";
        private const string AfterThoughtPath = "Assets/_Project/ScriptableObjects/Thoughts/Thought_ActPer-9.asset";

        /// <summary>
        /// Candidatos a ASSOVIO, na ordem de preferência. O primeiro que existir é
        /// ligado — assim, no dia em que você largar um <c>whistle.mp3</c> de verdade na
        /// pasta, rodar o comando de novo troca o placeholder por ele sem mais nada.
        ///
        /// O último da lista é um placeholder de conveniência: um homem cantarolando não
        /// é um assovio, mas é o som mais próximo que o projeto já tem de "alguém na rua
        /// fazendo barulho para ser notado", e é infinitamente melhor do que o silêncio
        /// para afinar o timing do beat.
        /// </summary>
        private static readonly string[] WhistleCandidates =
        {
            "Assets/_Project/SoundEffects/whistle.mp3",
            "Assets/_Project/SoundEffects/whistle.wav",
            "Assets/_Project/SoundEffects/whistle_placeholder.mp3",
            "Assets/_Project/SoundEffects/whistle_placeholder.wav",
            "Assets/_Project/SoundEffects/man_humming_placeholder.mp3",
        };

        private const string ActorName = "Leaning";
        private const string TriggerName = "Trigger_Leaning";
        private const string NoticeName = "LeaningNotice";
        private const string MarkersName = "_Markers";

        /// <summary>
        /// Quantos metros ANTES do Leaning (medidos na rota) o ponto nasce. É a
        /// distância que a Clear percorre dirigida, encarando o estranho — o beat todo.
        /// Curto demais e ela já está em cima dele quando o nota; longo demais e a
        /// caminhada vira uma espera.
        /// </summary>
        private const float TriggerBackOff = 9f;

        /// <summary>
        /// Quantos metros ANTES do Leaning nasce o ponto do GIRO. A diferença para o
        /// <see cref="TriggerBackOff"/> é o trecho que a Clear anda LIVRE com o assovio
        /// em loop no ouvido — o pedaço em que ela ainda pode fingir que não é com ela.
        /// Zerá-lo devolve o comportamento antigo, com o chamado e o giro no mesmo lugar.
        /// </summary>
        private const float NoticeBackOff = 5f;

        [MenuItem("Tools/The Delivery/Estrada - Encontro do Leaning")]
        private static void Run()
        {
            Scene scene = EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            GameObject actor = FindByName(scene, ActorName);
            if (actor == null)
            {
                Fail($"Não achei nenhum objeto chamado \"{ActorName}\" na cena Estrada.\n\n" +
                     "Ele é o ator que aborda a Clear. Confira o nome no Hierarchy (a busca é exata).");
                return;
            }

            PercursoDirector director = FindInScene<PercursoDirector>(scene);
            if (director == null)
            {
                Fail("PercursoDirector não encontrado na cena Estrada — é nele que o beat mora.");
                return;
            }

            var so = new SerializedObject(director);
            var spawn = so.FindProperty("spawnPoint").objectReferenceValue as Transform;
            var destination = so.FindProperty("destinationPoint").objectReferenceValue as Transform;

            DialogueData dialogue = LoadOrCreateDialogue();
            ThoughtData notice = LoadOrCreateThought(NoticeThoughtPath, NoticeLines());
            ThoughtData after = LoadOrCreateThought(AfterThoughtPath, AfterLines());

            GameObject trigger = EnsureTriggerPoint(scene, actor, spawn, destination, out bool triggerCreated);
            int cleaned = CleanUpLegacyComponents(scene, actor, trigger);

            // A LIMPEZA DE COLLIDER ACONTECE ANTES E SÓ NO GATILHO DO ASSOVIO, de
            // propósito: no ponto do giro o collider não é resto de versão antiga, é a
            // forma do portão que o director lê. Passá-lo pelo CleanUpLegacyComponents
            // apagaria justamente o que dá precisão ao beat.
            GameObject noticePoint = EnsureNoticePoint(scene, actor, spawn, destination, out bool noticeCreated);

            so.FindProperty("leaningActor").objectReferenceValue = actor.transform;
            so.FindProperty("leaningTriggerPoint").objectReferenceValue = trigger.transform;
            so.FindProperty("leaningNoticePoint").objectReferenceValue = noticePoint.transform;
            so.FindProperty("leaningDialogue").objectReferenceValue = dialogue;
            so.FindProperty("leaningNoticeThought").objectReferenceValue = notice;
            so.FindProperty("leaningAfterThought").objectReferenceValue = after;

            int defaults = WriteMissingDefaults(so);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(director);

            // O assovio tem comando próprio (Estrada - Assovio do Leaning), mas a
            // montagem geral também o deixa de pé — em force:false, que cria e configura
            // um source ausente sem desfazer o rolloff de um que alguém já afinou.
            LeaningWhistleSetup.Configure(director, force: false, out AudioSource whistleSource);
            var whistle = whistleSource != null ? whistleSource.clip : null;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = trigger;
            EditorGUIUtility.PingObject(trigger);

            Report(director, actor, trigger, triggerCreated, noticePoint, noticeCreated,
                   cleaned, defaults, whistle, spawn, destination);
        }

        /// <summary>
        /// GRAVA OS NÚMEROS DO ENCONTRO NA CENA, mas só os que estão em ZERO.
        ///
        /// POR QUE ISTO PRECISA EXISTIR: quando campos novos entram num script cujo
        /// componente já está numa cena salva, o Unity os desserializa como zero e IGNORA
        /// o valor padrão escrito no C#. O inicializador de campo só vale para instâncias
        /// criadas do zero; o arquivo da cena simplesmente não tem entrada para eles.
        ///
        /// O sintoma é cruel de debugar porque não parece uma configuração faltando —
        /// parece o script não funcionar. Com Trigger Radius em 0 a Clear atravessa a rua
        /// inteira e o estranho nunca a chama; com Stop Distance em 0 ela andaria para
        /// dentro dele. Nenhum dos dois gera erro.
        ///
        /// SÓ ESCREVE EM CIMA DE ZERO, nunca de um valor: quem afinou o Stop Distance
        /// olhando a rua não pode perdê-lo por rodar o comando de montagem de novo.
        /// Devolve quantos campos foram preenchidos.
        /// </summary>
        private static int WriteMissingDefaults(SerializedObject so)
        {
            int written = 0;

            // O Start Beat em None é o mesmo caso: o dropdown mostra a primeira entrada
            // do enum, de aparência inocente, e o ato inteiro fica sem rotina nenhuma.
            SerializedProperty startBeat = so.FindProperty("startBeat");
            if (startBeat != null && startBeat.enumValueIndex == (int)PercursoBeat.None)
            {
                startBeat.enumValueIndex = (int)PercursoBeat.Walk;
                written++;
            }

            written += SetIfZero(so, "leaningTriggerRadius", 5f);
            written += SetIfZero(so, "leaningNoticeRadius", 3f);
            written += SetIfZero(so, "leaningLookHeight", 1.6f);
            written += SetIfZero(so, "leaningTurnSpeed", 160f);
            written += SetIfZero(so, "leaningLookThreshold", 6f);
            written += SetIfZero(so, "leaningLookTimeout", 4f);
            written += SetIfZero(so, "leaningNoticePause", 1.2f);
            written += SetIfZero(so, "leaningStopDistance", 1.8f);
            written += SetIfZero(so, "leaningApproachTimeout", 20f);
            written += SetIfZero(so, "leaningPauseBeforeDialogue", 0.8f);
            written += SetIfZero(so, "leaningActorTurnSpeed", 120f);
            written += SetIfZero(so, "leaningWhistleVolume", 0.9f);
            written += SetIfZero(so, "leaningCallVolume", 1f);
            written += SetIfZero(so, "leaningWhistleLead", 0.8f);
            written += SetIfZero(so, "leaningWhistleFadeIn", 0.35f);
            written += SetIfZero(so, "leaningWhistleFadeOut", 1.5f);
            written += SetIfZero(so, "leaningWhistleMinAirtime", 2.5f);

            return written;
        }

        /// <summary>Escreve o valor só se o campo estiver em zero. Devolve 1 se escreveu.</summary>
        private static int SetIfZero(SerializedObject so, string field, float value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null || p.floatValue != 0f)
                return 0;

            p.floatValue = value;
            return 1;
        }

        // --- Cena -----------------------------------------------------------

        /// <summary>
        /// Cria (ou reaproveita) o objeto VAZIO do gatilho, sob o grupo <c>_Markers</c>.
        /// Um ponto que JÁ EXISTE não é movido: quem rodar o comando de novo depois de
        /// posicionar o gatilho à mão não perde o posicionamento — que é justamente a
        /// parte que só um humano olhando a rua sabe fazer.
        /// </summary>
        private static GameObject EnsureTriggerPoint(
            Scene scene, GameObject actor, Transform spawn, Transform destination, out bool created)
        {
            return EnsureMarker(scene, TriggerName, actor, spawn, destination, TriggerBackOff,
                                "Criar gatilho do Leaning", out created);
        }

        /// <summary>
        /// Cria (ou reaproveita) o ponto do GIRO — o segundo gatilho do encontro, onde a
        /// abordagem toma a rua. Mesma regra do primeiro: um ponto que já existe não é
        /// movido nem tem componente nenhum removido.
        ///
        /// O COLLIDER DELE, SE HOUVER, É PARA SER LIDO COMO FORMA e não como física: o
        /// director testa a caixa em XZ por conta própria (ver <c>PlayerInMarker</c>),
        /// sem depender de evento de trigger nenhum. Um BoxCollider aqui é como se
        /// desenha um PORTÃO atravessado na rua, que dispara na linha certa em vez de num
        /// círculo — e é por isso que este comando, ao contrário do gatilho do assovio,
        /// não limpa collider nenhum daqui. Ele precisa continuar marcado como
        /// <c>isTrigger</c>: um collider sólido no meio da rua vira uma parede.
        /// </summary>
        private static GameObject EnsureNoticePoint(
            Scene scene, GameObject actor, Transform spawn, Transform destination, out bool created)
        {
            return EnsureMarker(scene, NoticeName, actor, spawn, destination, NoticeBackOff,
                                "Criar ponto do giro do Leaning", out created);
        }

        /// <summary>
        /// A parte comum dos dois pontos: acha pelo nome, e só cria (sob <c>_Markers</c>,
        /// posicionado na rota) quando não existe. UM PONTO QUE JÁ EXISTE NÃO É MOVIDO:
        /// quem rodar o comando de novo depois de posicionar o marcador à mão não perde o
        /// posicionamento — que é justamente a parte que só um humano olhando a rua sabe
        /// fazer.
        /// </summary>
        private static GameObject EnsureMarker(
            Scene scene, string name, GameObject actor, Transform spawn, Transform destination,
            float backOff, string undoLabel, out bool created)
        {
            GameObject marker = FindByName(scene, name);
            created = marker == null;

            if (!created)
                return marker;

            marker = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(marker, undoLabel);
            SceneManager.MoveGameObjectToScene(marker, scene);

            GameObject markers = FindByName(scene, MarkersName);
            if (markers != null)
                marker.transform.SetParent(markers.transform, worldPositionStays: true);

            PlaceOnRoute(marker.transform, actor.transform, spawn, destination, backOff);
            return marker;
        }

        /// <summary>
        /// Remove o que sobrou da versão em que o beat vivia em componentes próprios: os
        /// scripts <c>LeaningEncounter</c> / <c>LeaningEncounterTrigger</c> (que agora
        /// são "missing script", porque os arquivos foram apagados) e o BoxCollider do
        /// gatilho, que virou um raio no director.
        ///
        /// O COLLIDER PRECISA MESMO SAIR, e não é limpeza cosmética: ele ficou marcado
        /// como <c>isTrigger</c>, então continua invisível — mas continua também
        /// gerando eventos de física, e um dia alguém vai passar meia hora procurando
        /// por que um volume que "não faz nada" aparece nas queries da cena.
        /// </summary>
        private static int CleanUpLegacyComponents(Scene scene, GameObject actor, GameObject trigger)
        {
            int removed = 0;

            removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(actor);
            removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(trigger);

            var box = trigger.GetComponent<Collider>();
            if (box != null)
            {
                Undo.DestroyObjectImmediate(box);
                removed++;
            }

            // A remoção automática NÃO É GARANTIDA num prefab instance (o script antigo
            // entrou lá como componente adicionado, e o Unity nem sempre deixa tirá-lo
            // por código). Avisar é melhor do que dizer que limpou e deixar um "missing
            // script" amarelo no Inspector do ator sem explicação.
            int leftover = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(actor)
                         + GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(trigger);
            if (leftover > 0)
            {
                Debug.LogWarning($"[LeaningEncounterSetup] Sobrou(ram) {leftover} componente(s) com script faltando " +
                                 $"em \"{actor.name}\"/\"{trigger.name}\". Remova pelo Inspector (botão direito no " +
                                 "componente amarelo > Remove Component) — eles são restos da versão antiga do beat " +
                                 "e não fazem nada.", actor);
            }

            return removed;
        }

        /// <summary>
        /// O CHUTE EDUCADO da posição do gatilho: projeta o Leaning sobre a reta
        /// Spawn -> Destino (a rota) e recua <see cref="TriggerBackOff"/> metros na
        /// direção de onde a Clear vem.
        ///
        /// Sem rota conhecida (Spawn ou Destino não ligados no director) o ponto nasce
        /// alguns metros à frente do próprio ator, no eixo dele. Continua sendo um ponto
        /// de partida para arrastar — só um pouco mais burro.
        /// </summary>
        private static void PlaceOnRoute(Transform trigger, Transform actor, Transform spawn, Transform destination, float backOff)
        {
            if (spawn == null || destination == null)
            {
                trigger.SetPositionAndRotation(
                    actor.position + actor.forward * backOff,
                    Quaternion.LookRotation(-actor.forward, Vector3.up));
                return;
            }

            Vector3 from = spawn.position;
            Vector3 to = destination.position;
            Vector3 route = to - from;
            route.y = 0f;

            if (route.sqrMagnitude <= 1e-4f)
            {
                trigger.position = actor.position;
                return;
            }

            Vector3 dir = route.normalized;

            // Projeção escalar do ator sobre a rota, presa ao trecho — um ator fora do
            // segmento (atrás do spawn, além do destino) não pode empurrar o gatilho
            // para um lugar onde a Clear nunca vai passar.
            Vector3 toActor = actor.position - from;
            float along = Mathf.Clamp(Vector3.Dot(new Vector3(toActor.x, 0f, toActor.z), dir), 0f, route.magnitude);
            float placed = Mathf.Max(0f, along - backOff);

            Vector3 point = from + dir * placed;
            // A altura vem do ator, não da rota: o Spawn está no chão da calçada de
            // origem, que pode estar em outro nível.
            point.y = actor.position.y;

            trigger.SetPositionAndRotation(point, Quaternion.LookRotation(dir, Vector3.up));
        }

        /// <summary>
        /// Liga o assovio, se o campo estiver vazio. UM CLIPE JÁ ESCOLHIDO NÃO É
        /// TROCADO: o som certo é achado ouvindo, e um comando que substitui a escolha a
        /// cada execução é um comando que ninguém roda de novo. Devolve o clipe que
        /// ficou no campo (o novo ou o que já estava), ou null se não há nenhum.
        /// </summary>
        internal static AudioClip AssignWhistle(SerializedProperty field)
        {
            if (field == null)
                return null;

            if (field.objectReferenceValue is AudioClip current)
                return current;

            foreach (string path in WhistleCandidates)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                    continue;

                field.objectReferenceValue = clip;
                return clip;
            }

            return null;
        }

        // --- Assets ----------------------------------------------------------

        /// <summary>
        /// Cria o asset da conversa com o texto de partida, se ele ainda não existir.
        /// UM ASSET EXISTENTE NUNCA É SOBRESCRITO: o texto é a parte que se afina lendo
        /// em voz alta, e um comando que apaga a afinação a cada execução é um comando
        /// que ninguém roda duas vezes.
        /// </summary>
        private static DialogueData LoadOrCreateDialogue()
        {
            var existing = AssetDatabase.LoadAssetAtPath<DialogueData>(DialoguePath);
            if (existing != null)
                return existing;

            var dialogue = ScriptableObject.CreateInstance<DialogueData>();
            var so = new SerializedObject(dialogue);
            SerializedProperty lines = so.FindProperty("lines");

            (string speaker, string text, float hold)[] script = DialogueScript();
            lines.arraySize = script.Length;
            for (int i = 0; i < script.Length; i++)
            {
                SerializedProperty line = lines.GetArrayElementAtIndex(i);
                line.FindPropertyRelative("speaker").stringValue = script[i].speaker;
                line.FindPropertyRelative("text").stringValue = script[i].text;
                line.FindPropertyRelative("extraHold").floatValue = script[i].hold;
                line.FindPropertyRelative("sfx").objectReferenceValue = null;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(dialogue, DialoguePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LeaningEncounterSetup] Asset criado: {DialoguePath}");

            return AssetDatabase.LoadAssetAtPath<DialogueData>(DialoguePath);
        }

        /// <summary>
        /// A ABORDAGEM. Ele nunca diz O QUE vai acontecer, e essa é a regra do texto
        /// inteiro: cada resposta dele troca uma pergunta por outra maior. O único fato
        /// concreto da cena é o último — que existe um "ele" — e é dito de passagem,
        /// como quem já foi longe demais.
        ///
        /// O <c>extraHold</c> é onde mora o desconforto: as pausas longas estão nas
        /// falas em que ele NÃO responde, para o silêncio ficar na tela tempo suficiente
        /// de o jogador perceber que ficou sem resposta.
        ///
        /// Ele não usa o nome da Clear DE PROPÓSITO: esse arrepio é do Jef, na recepção
        /// do prédio, e ele só funciona uma vez no jogo inteiro.
        /// </summary>
        private static (string, string, float)[] DialogueScript()
        {
            const string ele = "Estranho";
            const string ela = "Você";

            return new (string, string, float)[]
            {
                (ele, "Você não devia estar na rua hoje.", 1f),
                (ela, "...desculpa? Eu te conheço?", 0f),
                (ele, "Não. Mas eu já vi isso acontecer antes.", 2f),
                (ela, "Viu o quê?", 0f),
                (ele, "Sempre começa igual. Um dia comum. Alguém indo pra casa.", 2f),
                (ele, "Se eu falasse com todas as letras, você ia me achar louco. Assim, você só me acha estranho.", 1f),
                (ela, "Você tá me assustando.", 0f),
                (ele, "Bom. Vai ter uma hora hoje em que você vai poder escolher. Não vai parecer importante na hora.", 3f),
                (ela, "Escolher o quê?", 0f),
                (ele, "Pode ir. Ele não gosta que eu demore.", 4f),
                (ela, "...ele quem?", 2f),
            };
        }

        /// <summary>Cria o pensamento no caminho dado, se ainda não existir. Mesma regra do diálogo: nunca sobrescreve.</summary>
        private static ThoughtData LoadOrCreateThought(string path, (string text, float delay)[] script)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ThoughtData>(path);
            if (existing != null)
                return existing;

            var thought = ScriptableObject.CreateInstance<ThoughtData>();
            var so = new SerializedObject(thought);
            SerializedProperty lines = so.FindProperty("lines");

            lines.arraySize = script.Length;
            for (int i = 0; i < script.Length; i++)
            {
                SerializedProperty line = lines.GetArrayElementAtIndex(i);
                line.FindPropertyRelative("text").stringValue = script[i].text;
                // duration 0 = o ThoughtSystem calcula pelo tamanho do texto, como nos outros.
                line.FindPropertyRelative("duration").floatValue = 0f;
                line.FindPropertyRelative("delay").floatValue = script[i].delay;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(thought, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LeaningEncounterSetup] Asset criado: {path}");

            return AssetDatabase.LoadAssetAtPath<ThoughtData>(path);
        }

        /// <summary>O que ela pensa no instante em que o nota — enquanto a cabeça ainda está virando.</summary>
        private static (string, float)[] NoticeLines() => new (string, float)[]
        {
            ("Tem alguém ali.", 0f),
        };

        /// <summary>
        /// O que sobra depois. A segunda linha é a Clear tentando fechar o assunto —
        /// e é ela que denuncia que o assunto não fechou.
        /// </summary>
        private static (string, float)[] AfterLines() => new (string, float)[]
        {
            ("Ele não respondeu.", 1f),
            ("É só um doido de rua. É só isso.", 1.5f),
        };

        // --- Utilitários ------------------------------------------------------

        private static void Report(PercursoDirector director, GameObject actor, GameObject trigger,
                                   bool triggerCreated, GameObject noticePoint, bool noticeCreated,
                                   int cleaned, int defaults, AudioClip whistle,
                                   Transform spawn, Transform destination)
        {
            var sb = new StringBuilder();
            sb.AppendLine("O encontro está montado no PercursoDirector e a cena Estrada foi salva.");
            sb.AppendLine();
            sb.AppendLine($"• Ator: \"{actor.name}\"");
            sb.AppendLine(triggerCreated
                ? $"• \"{TriggerName}\" (começa o ASSOVIO, em loop) CRIADO na rota"
                : $"• \"{TriggerName}\" (começa o ASSOVIO, em loop) já existia — posição preservada");

            bool gate = noticePoint.GetComponent<BoxCollider>() != null;
            sb.AppendLine((noticeCreated
                    ? $"• \"{NoticeName}\" (começa o GIRO, beat 2) CRIADO na rota"
                    : $"• \"{NoticeName}\" (começa o GIRO, beat 2) já existia — posição preservada")
                + (gate
                    ? ", e tem BoxCollider: quem manda é a FORMA dele (portão), não o raio"
                    : ", sem collider: vale o Leaning Notice Radius"));

            float gap = Vector3.Distance(trigger.transform.position, noticePoint.transform.position);
            sb.AppendLine($"• Entre os dois: {gap:0.0} m — é o trecho que ela anda LIVRE com o assovio insistindo. " +
                          "Se ficar curto demais, o loop não chega a dar a segunda volta e o chamado vira um " +
                          "efeito sonoro qualquer.");

            sb.AppendLine($"• Conversa: {DialoguePath}");
            sb.AppendLine(whistle != null
                ? $"• Assovio: \"{whistle.name}\" ({whistle.length:0.0} s)"
                : "• Assovio: NENHUM — ela vai virar a cabeça em silêncio");
            if (cleaned > 0)
                sb.AppendLine($"• {cleaned} componente(s) da versão antiga removidos (scripts avulsos e o collider do gatilho)");
            if (defaults > 0)
            {
                sb.AppendLine($"• {defaults} campo(s) do encontro estavam ZERADOS e foram preenchidos — o Unity não " +
                              "aplica os valores padrão do C# em campos novos de um componente já salvo na cena, e " +
                              "com o Trigger Radius em 0 a abordagem nunca dispararia.");
            }
            sb.AppendLine();

            if (triggerCreated && (spawn == null || destination == null))
            {
                sb.AppendLine("ATENÇÃO: o PercursoDirector está sem Spawn ou sem Destino, então o gatilho não pôde ser " +
                              "posto sobre a rota — ele nasceu na frente do ator. Arraste-o.");
                sb.AppendLine();
            }

            sb.AppendLine("AGORA É COM VOCÊ, e é a parte que só dá para fazer olhando a rua:");
            sb.AppendLine($"1. Arraste o \"{TriggerName}\" até onde o estranho deve COMEÇAR A ASSOVIAR, e o " +
                          $"\"{NoticeName}\" até onde a abordagem deve TOMAR A RUA. Selecione o PercursoDirector " +
                          "para ver o raio do primeiro (esfera laranja). A distância entre os dois é o beat.");
            sb.AppendLine("2. Para testar só o encontro: no PercursoDirector, Debug > Start Beat = LeaningNotice. " +
                          "A Clear nasce no gatilho em vez do começo da rua.");
            sb.AppendLine("3. Ligue o Debug Mode para pular entre beats com as teclas 1-6 durante o Play.");
            sb.AppendLine("4. O texto se edita no asset do diálogo — a conversa leva perto de um minuto com o " +
                          "jogador parado; corte falas se for demais.");

            if (whistle != null && whistle.name.Contains("humming"))
            {
                sb.AppendLine();
                sb.AppendLine("SOBRE O ASSOVIO: o projeto ainda não tem um, então entrou o \"" + whistle.name +
                              "\" como placeholder — serve para afinar o timing, mas não é o som certo. Largue um " +
                              "assovio em Assets/_Project/SoundEffects/whistle.mp3 e limpe o campo Leaning Whistle " +
                              "no director; rodar este comando de novo pega o novo arquivo.");
            }

            Debug.Log($"[LeaningEncounterSetup] {sb}", director);
            EditorUtility.DisplayDialog("Encontro do Leaning", sb.ToString(), "Ok");
        }

        internal static GameObject FindByName(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root;

                foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (t.name == name)
                        return t.gameObject;
                }
            }

            return null;
        }

        internal static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(includeInactive: true);
                if (found != null)
                    return found;
            }

            return null;
        }

        internal static Scene EnsureSceneOpen()
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
            Debug.LogError($"[LeaningEncounterSetup] {message}");
            EditorUtility.DisplayDialog("Encontro do Leaning", message, "Ok");
        }
    }
}
