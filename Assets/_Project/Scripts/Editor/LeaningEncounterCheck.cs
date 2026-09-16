using System.Collections.Generic;
using System.Text;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// DIZ SE O ENCONTRO DO LEANING VAI FUNCIONAR, sem rodar a cena.
    ///
    /// POR QUE ISTO EXISTE: o beat fica no meio de um trajeto de um minuto, e quando ele
    /// não acontece o sintoma é sempre o mesmo — "passei pelo cara e nada" — que pode
    /// ser meia dúzia de coisas indistinguíveis de dentro do Play: o gatilho ficou fora
    /// da rota, o raio é pequeno demais para quem não anda na linha, o ponto ficou
    /// DEPOIS do estranho, o ator não está ligado no director. Nenhuma delas gera um
    /// erro no Console — todas geram silêncio. Aqui cada uma vira uma linha do relatório.
    ///
    /// E VERIFICA TAMBÉM O QUE ACONTECE SE ELE FUNCIONAR: quanto tempo o jogador fica
    /// sem controle, e se algum pensamento da rua vai ser atropelado pela conversa.
    ///
    /// AS CONTAS SÃO AS MESMAS que o jogo faz. Se este comando disser "a conversa leva
    /// 58 s", é 58 s que o jogador vai passar parado.
    /// </summary>
    public static class LeaningEncounterCheck
    {
        /// <summary>Severidade de uma linha do relatório. A ordem importa: define o veredito final.</summary>
        private enum Level { Ok, Info, Aviso, Erro }

        [MenuItem("Tools/The Delivery/Estrada - Checar encontro do Leaning")]
        private static void Run()
        {
            Scene scene = LeaningEncounterSetup.EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            PercursoDirector director = LeaningEncounterSetup.FindInScene<PercursoDirector>(scene);
            if (director == null)
            {
                Show("PercursoDirector não encontrado na cena Estrada — é nele que o beat mora.", null);
                return;
            }

            var lines = new List<(Level level, string text)>();
            GameObject blame = null;

            var so = new SerializedObject(director);

            var player = Get<PlayerController>(so, "playerController");
            var spawn = Get<Transform>(so, "spawnPoint");
            var destination = Get<Transform>(so, "destinationPoint");
            float destinationRadius = so.FindProperty("destinationRadius").floatValue;

            var actor = Get<Transform>(so, "leaningActor");
            var triggerPoint = Get<Transform>(so, "leaningTriggerPoint");
            var noticePoint = Get<Transform>(so, "leaningNoticePoint");
            var approachPoint = Get<Transform>(so, "leaningApproachPoint");
            var dialogue = Get<DialogueData>(so, "leaningDialogue");

            float triggerRadius = so.FindProperty("leaningTriggerRadius").floatValue;
            float noticeRadius = so.FindProperty("leaningNoticeRadius").floatValue;
            float stopDistance = so.FindProperty("leaningStopDistance").floatValue;
            float turnSpeed = so.FindProperty("leaningTurnSpeed").floatValue;
            float noticePause = so.FindProperty("leaningNoticePause").floatValue;
            float pauseBefore = so.FindProperty("leaningPauseBeforeDialogue").floatValue;
            float approachTimeout = so.FindProperty("leaningApproachTimeout").floatValue;

            var startBeat = (PercursoBeat)so.FindProperty("startBeat").enumValueIndex;
            bool debugMode = so.FindProperty("debugMode").boolValue;

            // --- Os campos zerados ------------------------------------------
            //
            // PRIMEIRO DE TUDO, porque explica todos os outros: com números em zero, o
            // resto do relatório vira uma lista de sintomas de uma causa só.

            string zeroed = FindZeroedFields(so);
            if (zeroed != null)
            {
                lines.Add((Level.Erro, $"CAMPOS ZERADOS no encontro: {zeroed}.\n" +
                                       "     O Unity não aplica os valores padrão do C# em campos NOVOS de um " +
                                       "componente que já estava salvo na cena — eles entram como zero. Com o Trigger " +
                                       "Radius em 0 a abordagem nunca dispara.\n" +
                                       "     Conserto: rode Tools > The Delivery > Estrada - Encontro do Leaning."));
                blame ??= director.gameObject;
            }

            // --- As referências ---------------------------------------------

            if (actor == null)
            {
                lines.Add((Level.Erro, "Leaning Actor VAZIO no PercursoDirector — o encontro nunca acontece."));
                blame ??= director.gameObject;
            }
            else
            {
                lines.Add((Level.Ok, $"Ator: \"{actor.name}\""));
                if (!actor.gameObject.activeInHierarchy)
                {
                    lines.Add((Level.Erro, $"O ator \"{actor.name}\" está DESATIVADO na cena."));
                    blame ??= actor.gameObject;
                }
            }

            if (triggerPoint == null)
            {
                lines.Add((Level.Erro, "Leaning Trigger Point VAZIO — não há onde a abordagem começar."));
                blame ??= director.gameObject;
            }
            else
            {
                lines.Add((Level.Ok, $"Gatilho do assovio: \"{triggerPoint.name}\", raio {triggerRadius:0.0} m"));

                // Um collider aqui é resto da versão antiga do beat: invisível, inerte
                // para o director (que vigia por raio) e confuso para quem abrir a cena.
                if (triggerPoint.GetComponent<Collider>() != null)
                {
                    lines.Add((Level.Aviso, $"\"{triggerPoint.name}\" ainda tem um Collider da versão antiga do beat. " +
                                            "Ele não faz nada agora — rode o comando de montagem para removê-lo."));
                }
            }

            // --- O segundo ponto: onde o giro começa -------------------------

            if (noticePoint == null)
            {
                lines.Add((Level.Aviso, "Leaning Notice Point VAZIO — o gatilho do assovio está acumulando os dois " +
                                        "papéis (o chamado e o giro no mesmo lugar), que é o comportamento antigo.\n" +
                                        "     Com ele, o assovio em loop não tem trecho para insistir: o beat 2 " +
                                        "começa e o desce no mesmo quadro em que ele nasce.\n" +
                                        "     Conserto: rode Tools > The Delivery > Estrada - Encontro do Leaning."));
                blame ??= director.gameObject;
            }
            else
            {
                var box = noticePoint.GetComponent<BoxCollider>();
                lines.Add((Level.Ok, box != null
                    ? $"Gatilho do giro: \"{noticePoint.name}\", pela FORMA do BoxCollider ({box.size.x:0.#} x " +
                      $"{box.size.z:0.#} m em XZ)"
                    : $"Gatilho do giro: \"{noticePoint.name}\", raio {noticeRadius:0.0} m"));

                if (box != null && !box.isTrigger)
                {
                    lines.Add((Level.Erro, $"O BoxCollider de \"{noticePoint.name}\" NÃO está marcado como Is Trigger: " +
                                           "ele virou uma parede no meio da rua e a Clear esbarra nele antes de " +
                                           "cruzá-lo."));
                    blame ??= noticePoint.gameObject;
                }

                if (triggerPoint != null)
                {
                    float gap = Vector3.Distance(triggerPoint.position, noticePoint.position);
                    lines.Add((gap < 2f ? Level.Aviso : Level.Ok,
                        $"Trecho livre entre os dois pontos: {gap:0.0} m" +
                        (gap < 2f
                            ? " — CURTO DEMAIS. É o pedaço em que ela anda ouvindo o chamado e ainda pode ignorá-lo; " +
                              "assim, o assovio e a perda de controle acontecem praticamente juntos."
                            : string.Empty)));
                }
            }

            if (player == null)
            {
                lines.Add((Level.Erro, "Player Controller VAZIO no PercursoDirector."));
                blame ??= director.gameObject;
            }

            if (dialogue == null)
                lines.Add((Level.Aviso, "Leaning Dialogue VAZIO — ele aborda a Clear e fica calado."));

            CheckWhistle(so, actor, triggerPoint, triggerRadius, lines);

            lines.Add((Level.Info, approachPoint != null
                ? $"Approach Point: \"{approachPoint.name}\" (os pés vão até lá; o olhar continua no ator)"
                : $"Sem Approach Point: ela anda reto até o ator e para a {stopDistance:0.0} m dele"));

            // --- A geometria da rota ----------------------------------------

            float walkDistance = -1f;

            if (spawn == null || destination == null)
            {
                lines.Add((Level.Aviso, "O PercursoDirector está sem Spawn ou sem Destino: a rota é desconhecida, " +
                                        "então a posição do gatilho não foi conferida."));
            }
            else if (actor != null && triggerPoint != null)
            {
                Vector3 from = spawn.position;
                Vector3 route = destination.position - from;
                route.y = 0f;
                Vector3 dir = route.normalized;
                float routeLength = route.magnitude;

                float alongTrigger = Along(triggerPoint.position, from, dir);
                float alongActor = Along(actor.position, from, dir);

                // O erro que mais parece "o script não funciona": o gatilho ficou depois
                // do estranho, e ela só é abordada quando já passou por ele.
                if (alongTrigger >= alongActor)
                {
                    lines.Add((Level.Erro, $"O gatilho está DEPOIS do estranho na rota (a {alongTrigger - alongActor:0.0} m " +
                                           "além dele): quando o beat disparar, ela já terá passado por ele e vai voltar " +
                                           "andando pelo caminho. Arraste o gatilho para trás."));
                    blame ??= triggerPoint.gameObject;
                }
                else
                {
                    walkDistance = alongActor - alongTrigger;
                    lines.Add((Level.Ok, $"O gatilho está {walkDistance:0.0} m antes do estranho na rota"));

                    if (walkDistance < 3f)
                    {
                        lines.Add((Level.Aviso, "Menos de 3 m de caminhada: ela já está praticamente em cima dele " +
                                                "quando o nota, e o beat perde a aproximação inteira."));
                    }
                    else if (walkDistance > 25f)
                    {
                        lines.Add((Level.Aviso, $"{walkDistance:0.0} m de caminhada dirigida é muito tempo sem controle " +
                                                "só andando. Considere aproximar o gatilho."));
                    }
                }

                // O raio alcança a linha por onde ela anda? Um gatilho deslocado ou
                // apertado é um beat que "às vezes não acontece".
                float perpendicular = Perpendicular(triggerPoint.position, from, dir);
                if (perpendicular > triggerRadius)
                {
                    lines.Add((Level.Erro, $"O raio do gatilho NÃO alcança a rota: o ponto está a {perpendicular:0.0} m " +
                                           $"da linha Spawn->Destino e o raio é {triggerRadius:0.0} m. Ela passa ao lado."));
                    blame ??= triggerPoint.gameObject;
                }
                else if (perpendicular > triggerRadius * 0.6f)
                {
                    lines.Add((Level.Aviso, $"O gatilho pega a rota pela borda ({perpendicular:0.0} m de " +
                                            $"{triggerRadius:0.0} m de raio): quem andar um pouco fora da linha pode " +
                                            "não disparar o beat. Aumente o raio ou centralize o ponto."));
                }
                else
                {
                    lines.Add((Level.Ok, $"O gatilho cobre a rota (folga de {triggerRadius - perpendicular:0.0} m)"));
                }

                if (alongTrigger > routeLength)
                {
                    lines.Add((Level.Erro, "O gatilho está ALÉM do Destino: a cena troca para a Recepção antes de a " +
                                           "Clear chegar nele."));
                    blame ??= triggerPoint.gameObject;
                }

                // A zona do estranho encostando na do prédio: o beat ainda funciona (as
                // vigias só rodam no Walk, então a conversa não é cortada), mas ela sai
                // da conversa já dentro do raio de chegada e a rua acaba no mesmo passo.
                Vector3 stop = approachPoint != null ? approachPoint.position : actor.position;
                float margin = Flat(stop - destination.position) - (approachPoint != null ? 0f : stopDistance) - destinationRadius;
                if (margin < 0f)
                {
                    lines.Add((Level.Aviso, $"Ela termina a conversa JÁ DENTRO do raio de chegada do Destino " +
                                            $"(faltam {-margin:0.0} m): o controle volta e a cena troca no passo " +
                                            "seguinte, sem respiro entre o estranho e o prédio."));
                }
            }

            // --- Os tempos ---------------------------------------------------

            float walkTime = 0f;
            if (player != null && walkDistance > 0f)
            {
                float walkSpeed = new SerializedObject(player).FindProperty("walkSpeed").floatValue;
                if (walkSpeed > 0.01f)
                {
                    // +15% pela aceleração suavizada do PlayerController: ela não parte
                    // na velocidade final, e a conta seca sempre subestima.
                    float arriveAt = approachPoint != null ? 0.15f : stopDistance;
                    walkTime = Mathf.Max(0f, walkDistance - arriveAt) / walkSpeed * 1.15f;
                    lines.Add((Level.Info, $"Caminhada dirigida: ~{walkTime:0.0} s a {walkSpeed:0.0} m/s"));

                    if (approachTimeout < walkTime * 1.5f)
                    {
                        lines.Add((Level.Aviso, $"Approach Timeout ({approachTimeout:0.0} s) tem pouca folga para uma " +
                                                $"caminhada de {walkTime:0.0} s. Suba para pelo menos {walkTime * 2f:0} s."));
                    }
                }
            }

            float dialogueTime = EstimateDialogue(scene, dialogue, out int lineCount);
            if (dialogue != null)
                lines.Add((Level.Info, $"Conversa: {lineCount} falas, ~{dialogueTime:0} s"));

            // O respiro do assovio só conta se houver assovio — sem clipe o beat não espera.
            float whistleLead = Get<AudioClip>(so, "leaningWhistle") != null
                ? so.FindProperty("leaningWhistleLead").floatValue
                : 0f;

            float turnTime = turnSpeed > 1f ? 90f / turnSpeed : 0f;
            float total = whistleLead + turnTime + noticePause + walkTime + pauseBefore + dialogueTime;
            lines.Add((Level.Info, $"TEMPO TOTAL SEM CONTROLE: ~{total:0} s"));
            if (total > 90f)
            {
                lines.Add((Level.Aviso, $"~{total:0} s é muito tempo com o jogador sem poder fazer nada. " +
                                        "Corte falas no asset do diálogo ou aproxime o gatilho do estranho."));
            }

            // --- Vizinhança e sistemas ------------------------------------------

            if (triggerPoint != null && actor != null && walkDistance > 0f)
                WarnAboutThoughtTriggers(scene, triggerPoint.position, actor.position, lines);

            if (LeaningEncounterSetup.FindInScene<DialogueSystem>(scene) == null)
            {
                lines.Add((Level.Erro, "Nenhum DialogueSystem na cena (ele vive dentro do prefab do Player): " +
                                       "a conversa não vai aparecer na tela."));
            }

            // --- Debug ------------------------------------------------------------

            // O jeito mais fácil de "quebrar" o ato sem quebrar nada: deixar o Start
            // Beat num beat de teste depois de debugar. Ele não gera erro nenhum — só
            // começa a cena no lugar errado, e parece um bug no director.
            if (startBeat != PercursoBeat.Walk)
            {
                lines.Add((Level.Aviso, $"Start Beat está em \"{startBeat}\", não em \"Walk\": a Clear vai nascer no " +
                                        "meio da coreografia (e em outro ponto da rua), e sem assovio — ele é efeito " +
                                        "de cruzar o gatilho. Volte para Walk quando terminar de debugar."));
                blame ??= director.gameObject;
            }

            lines.Add((Level.Info, debugMode
                ? "Debug Mode LIGADO: teclas 1-6 pulam entre os beats no Play e cada troca vai para o Console."
                : "Debug Mode desligado. Ligue-o no PercursoDirector para usar as teclas 1-6 durante o Play."));

            Report(lines, blame);
        }

        /// <summary>
        /// O ASSOVIO, e os três jeitos de ele existir sem se ouvir: sem clipe, num
        /// source 2D (que sai de dentro da cabeça dela, sem direção — e a direção é a
        /// única coisa que ele precisa dar), ou com alcance curto demais para chegar até
        /// o gatilho, onde a Clear está quando ele toca. Os três soam idênticos jogando:
        /// ela vira a cabeça sozinha, do nada.
        /// </summary>
        private static void CheckWhistle(SerializedObject so, Transform actor, Transform triggerPoint,
                                         float triggerRadius, List<(Level, string)> lines)
        {
            var clip = Get<AudioClip>(so, "leaningWhistle");
            var source = Get<AudioSource>(so, "leaningWhistleSource");
            float lead = so.FindProperty("leaningWhistleLead").floatValue;
            float fadeOut = so.FindProperty("leaningWhistleFadeOut").floatValue;

            if (clip == null)
            {
                lines.Add((Level.Aviso, "Leaning Whistle VAZIO: ela vira a cabeça em silêncio, para um homem parado " +
                                        "que não fez nada. O giro acontece, mas parece bug de câmera."));
                return;
            }

            lines.Add((Level.Ok, $"Assovio: \"{clip.name}\" ({clip.length:0.0} s) — começa EM LOOP ao cruzar o gatilho " +
                                 $"e desce num fade de {fadeOut:0.0} s assim que ela cruza o ponto do giro"));

            // --- O chamado, que é o som a que ela de fato responde ------------

            var call = Get<AudioClip>(so, "leaningCall");
            var callSource = Get<AudioSource>(so, "leaningCallSource");
            float callVolume = so.FindProperty("leaningCallVolume").floatValue;

            if (call == null)
            {
                lines.Add((Level.Aviso, "Leaning Call VAZIO: no ponto do giro o estranho não diz nada, e ela vira só " +
                                        "com o assovio morrendo. O giro acontece porque o jogador pisou num lugar, " +
                                        "não porque alguém falou com ela."));
            }
            else
            {
                lines.Add((Level.Ok, $"Chamado: \"{call.name}\" ({call.length:0.0} s) — one-shot ao cruzar o ponto do " +
                                     $"giro, volume {callVolume:0.00}, respiro de {lead:0.0} s antes dela virar"));

                if (callVolume <= 0f)
                {
                    lines.Add((Level.Erro, "Leaning Call Volume em ZERO: o chamado TOCA e toca mudo — o Console fica " +
                                           "limpo e o defeito é indistinguível de um clipe errado.\n" +
                                           "     Conserto: rode Tools > The Delivery > Estrada - Encontro do Leaning."));
                }

                if (lead > call.length)
                {
                    lines.Add((Level.Aviso, $"O respiro ({lead:0.0} s) é MAIOR que o chamado ({call.length:0.0} s): ele " +
                                            "termina de falar e ela fica parada olhando para a frente antes de virar. " +
                                            "A resposta chega tarde demais para parecer resposta."));
                }

                if (callSource != null && callSource == source)
                {
                    lines.Add((Level.Erro, "Leaning Call Source e Leaning Whistle Source são o MESMO AudioSource. O " +
                                           "chamado sai enquanto o assovio está em fade out, e o volume do source é a " +
                                           "alça desse fade: o chamado vai sumir junto e o Stop() do fim do fade o " +
                                           "corta no meio.\n" +
                                           "     Conserto: rode Tools > The Delivery > Estrada - Assovio do Leaning."));
                }
            }

            if (fadeOut <= 0.05f)
            {
                lines.Add((Level.Aviso, "Fade Out do assovio em 0: o som é cortado SECO quando ela termina de virar, e " +
                                        "o estalo do corte no meio da onda é mais audível que o próprio assovio."));
            }

            if (source == null)
            {
                // O caso normal: o director cria um configurado no ator, em runtime.
                float needed = triggerPoint != null && actor != null
                    ? (Vector3.Distance(actor.position, triggerPoint.position) + triggerRadius) * 2f
                    : 0f;
                lines.Add((Level.Info, $"AudioSource do assovio: criado em runtime no ator, 3D, alcance " +
                                       $"{Mathf.Max(40f, needed):0} m"));
                return;
            }

            // A caixinha vem MARCADA por padrão num AudioSource novo, e marcada ela faz o
            // assovio tocar no carregamento da cena — sem gatilho, sem beat, sem nada. O
            // director desliga isso em runtime, mas o certo é sair da cena assim.
            if (source.playOnAwake)
            {
                lines.Add((Level.Aviso, $"O AudioSource \"{source.name}\" está com Play On Awake: o assovio tocaria ao " +
                                        "carregar a cena, antes de a Clear dar o primeiro passo. O director desliga " +
                                        "isso ao assumir, mas desmarque a caixinha no Inspector."));
            }

            if (source.spatialBlend < 0.5f)
            {
                lines.Add((Level.Aviso, $"O AudioSource \"{source.name}\" está quase 2D (Spatial Blend " +
                                        $"{source.spatialBlend:0.00}): o assovio sai de dentro da cabeça da Clear e não " +
                                        "aponta direção nenhuma. Ponha em 1, ou limpe o campo para o director criar um."));
            }

            if (actor != null && triggerPoint != null)
            {
                float distance = Vector3.Distance(actor.position, triggerPoint.position) + triggerRadius;
                if (source.maxDistance < distance)
                {
                    lines.Add((Level.Erro, $"O alcance do assovio ({source.maxDistance:0.0} m) é menor que a distância " +
                                           $"do gatilho até o ator ({distance:0.0} m): o som TOCA, inaudível — o que é " +
                                           "indistinguível de não tocar. Suba o Max Distance do AudioSource."));
                }
            }
        }

        /// <summary>
        /// Lista os campos do encontro que estão em zero, ou null se está tudo
        /// preenchido. Um zero aqui quase nunca é escolha de alguém: é um campo que
        /// entrou no script depois de a cena ter sido salva (ver o relatório).
        /// </summary>
        private static string FindZeroedFields(SerializedObject so)
        {
            string[] fields =
            {
                "leaningTriggerRadius",
                "leaningLookHeight",
                "leaningTurnSpeed",
                "leaningLookTimeout",
                "leaningStopDistance",
                "leaningApproachTimeout",
            };

            var zeroed = new List<string>();
            foreach (string field in fields)
            {
                SerializedProperty p = so.FindProperty(field);
                if (p != null && p.floatValue == 0f)
                    zeroed.Add(ObjectNames.NicifyVariableName(field));
            }

            SerializedProperty startBeat = so.FindProperty("startBeat");
            if (startBeat != null && startBeat.enumValueIndex == (int)PercursoBeat.None)
                zeroed.Add("Start Beat (None)");

            return zeroed.Count > 0 ? string.Join(", ", zeroed) : null;
        }

        // --- Contas -----------------------------------------------------------

        /// <summary>Quanto o ponto avançou ao longo da rota (m), medido no plano.</summary>
        private static float Along(Vector3 point, Vector3 from, Vector3 dir)
        {
            Vector3 v = point - from;
            return Vector3.Dot(new Vector3(v.x, 0f, v.z), dir);
        }

        /// <summary>A que distância o ponto está da LINHA da rota (m), no plano.</summary>
        private static float Perpendicular(Vector3 point, Vector3 from, Vector3 dir)
        {
            Vector3 v = point - from;
            v.y = 0f;
            return (v - dir * Vector3.Dot(v, dir)).magnitude;
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        /// <summary>
        /// Quanto tempo a conversa leva, com a MESMA conta do
        /// <see cref="DialogueSystem"/>: por fala, fade in + (base + caracteres * ritmo
        /// + extraHold) + fade out + intervalo. Os números de timing são lidos do
        /// componente que está na cena — se alguém os afinou, a estimativa acompanha.
        /// </summary>
        private static float EstimateDialogue(Scene scene, DialogueData dialogue, out int lineCount)
        {
            lineCount = 0;
            if (dialogue == null || dialogue.lines == null)
                return 0f;

            lineCount = dialogue.lines.Length;

            float baseTime = 1.2f, timePerChar = 0.045f, fade = 0.3f, gap = 0.3f;

            DialogueSystem system = LeaningEncounterSetup.FindInScene<DialogueSystem>(scene);
            if (system != null)
            {
                var so = new SerializedObject(system);
                baseTime = so.FindProperty("baseTime").floatValue;
                timePerChar = so.FindProperty("timePerChar").floatValue;
                fade = so.FindProperty("fadeDuration").floatValue;
                gap = so.FindProperty("lineGap").floatValue;
            }

            float total = 0f;
            foreach (var line in dialogue.lines)
            {
                int len = line.text != null ? line.text.Length : 0;
                total += fade + (baseTime + len * timePerChar + line.extraHold) + fade + gap;
            }

            return total;
        }

        /// <summary>
        /// Avisa sobre ThoughtTriggers no TRECHO da caminhada dirigida. Não é um
        /// defeito: é que o <see cref="DialogueSystem"/> interrompe qualquer pensamento
        /// ao abrir a boca, então um pensamento colocado ali vai ser cortado no meio —
        /// e o sintoma ("aquela frase às vezes não aparece") não sugere em nada o
        /// estranho parado na calçada.
        /// </summary>
        private static void WarnAboutThoughtTriggers(Scene scene, Vector3 triggerPos, Vector3 actorPos,
                                                     List<(Level, string)> lines)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (ThoughtTrigger t in root.GetComponentsInChildren<ThoughtTrigger>(includeInactive: true))
                {
                    Vector3 p = t.transform.position;
                    // Dentro da elipse formada pelo gatilho e pelo ator, com folga.
                    bool between = Flat(p - triggerPos) + Flat(p - actorPos) <= Flat(actorPos - triggerPos) + 6f;
                    if (between)
                    {
                        lines.Add((Level.Aviso, $"\"{t.name}\" fica no meio do trecho da abordagem: o pensamento dele " +
                                                "vai ser interrompido quando o estranho começar a falar."));
                    }
                }
            }
        }

        // --- Saída --------------------------------------------------------------

        private static void Report(List<(Level level, string text)> lines, GameObject blame)
        {
            var worst = Level.Ok;
            var sb = new StringBuilder();

            foreach ((Level level, string text) in lines)
            {
                if (level > worst)
                    worst = level;

                string mark = level switch
                {
                    Level.Erro => "[X]",
                    Level.Aviso => "[!]",
                    Level.Info => "[ ]",
                    _ => "[ok]",
                };
                sb.AppendLine($"{mark} {text}");
            }

            string verdict = worst switch
            {
                Level.Erro => "O BEAT NÃO VAI FUNCIONAR",
                Level.Aviso => "O beat funciona, mas com ressalvas",
                _ => "Tudo certo — o beat deve funcionar",
            };

            string report = $"{verdict}\n\n{sb}";

            if (worst == Level.Erro)
                Debug.LogError($"[LeaningEncounterCheck] {report}", blame);
            else if (worst == Level.Aviso)
                Debug.LogWarning($"[LeaningEncounterCheck] {report}", blame);
            else
                Debug.Log($"[LeaningEncounterCheck] {report}", blame);

            if (blame != null)
            {
                Selection.activeGameObject = blame;
                EditorGUIUtility.PingObject(blame);
            }

            Show(report, blame);
        }

        private static void Show(string message, GameObject blame)
        {
            EditorUtility.DisplayDialog("Checar encontro do Leaning", message, "Ok");
            if (blame != null)
                EditorGUIUtility.PingObject(blame);
        }

        private static T Get<T>(SerializedObject so, string field) where T : Object
        {
            SerializedProperty p = so.FindProperty(field);
            return p != null ? p.objectReferenceValue as T : null;
        }
    }
}
