using System.Text;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// CONFIGURA O ASSOVIO DO ESTRANHO na cena: põe o AudioSource no ator, com os
    /// números certos, e liga tudo no <see cref="PercursoDirector"/>.
    ///
    /// POR QUE ISTO É UM COMANDO: um AudioSource montado à mão erra de três jeitos que
    /// não se distinguem jogando. Com <b>Play On Awake</b> (a caixinha vem MARCADA num
    /// componente novo) o assovio toca no carregamento da cena, sem gatilho nenhum. Com
    /// <b>Spatial Blend</b> em 0 ele sai de dentro da cabeça da Clear e não aponta
    /// direção — que é a única coisa que ele precisa fazer. Com <b>Max Distance</b>
    /// curto demais ele toca INAUDÍVEL lá do gatilho, e isso é indistinguível de um
    /// clipe não atribuído. Os três dão o mesmo sintoma: "o assovio não funciona".
    ///
    /// O ALCANCE É CALCULADO DE TRÁS PARA A FRENTE: em vez de escolher um Max Distance e
    /// torcer, este comando mede a distância do ator até o gatilho — onde a Clear está no
    /// instante em que o som nasce — e entrega essa medida ao
    /// <see cref="PercursoDirector.ApplyWhistleSpatialSettings"/>, que resolve o alcance
    /// a partir de QUÃO ALTO o assovio deve chegar ali. A conta mora lá, e não aqui,
    /// porque o director também cria esse source sozinho em runtime: duas contas em dois
    /// arquivos significariam afinar o som no Editor e ouvir outro no Play.
    ///
    /// A DIFERENÇA DE RODAR ISTO, já que o director monta o source sozinho em runtime, é
    /// que ele passa a EXISTIR NA CENA: dá para ouvir o clipe no Inspector, ver a curva
    /// de rolloff desenhada na Scene view e afinar o alcance à mão — nada disso é
    /// possível num componente que só nasce no Play.
    /// </summary>
    public static class LeaningWhistleSetup
    {

        [MenuItem("Tools/The Delivery/Estrada - Assovio do Leaning")]
        private static void Run()
        {
            Scene scene = LeaningEncounterSetup.EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            PercursoDirector director = LeaningEncounterSetup.FindInScene<PercursoDirector>(scene);
            if (director == null)
            {
                Fail("PercursoDirector não encontrado na cena Estrada.");
                return;
            }

            string report = Configure(director, force: true, out AudioSource source);
            if (source == null)
            {
                Fail(report);
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = source.gameObject;
            EditorGUIUtility.PingObject(source);

            Debug.Log($"[LeaningWhistleSetup] {report}", source);
            EditorUtility.DisplayDialog("Assovio do Leaning", report, "Ok");
        }

        /// <summary>
        /// Põe (ou reaproveita) o AudioSource do assovio no ator e liga no director.
        /// Devolve o relatório do que foi feito, e o source em <paramref name="source"/>
        /// (null se faltou alguma peça — aí o relatório diz qual).
        ///
        /// <paramref name="force"/> false é o modo do comando de montagem geral: cria e
        /// configura um source que não existe, mas num que JÁ EXISTE só desarma o que é
        /// perigoso (Play On Awake e loop) e não encosta em rolloff nem em distâncias —
        /// quem afinou aquilo à mão afinou de ouvido, e um comando que desfaz isso a cada
        /// execução é um comando que ninguém roda duas vezes.
        ///
        /// <paramref name="force"/> true é o modo do comando dedicado: reconfigura tudo,
        /// porque foi exatamente isso que quem o chamou pediu.
        /// </summary>
        internal static string Configure(PercursoDirector director, bool force, out AudioSource source)
        {
            source = null;

            var so = new SerializedObject(director);
            SerializedProperty sourceField = so.FindProperty("leaningWhistleSource");
            SerializedProperty clipField = so.FindProperty("leaningWhistle");

            var actor = so.FindProperty("leaningActor").objectReferenceValue as Transform;
            if (actor == null)
            {
                return "O PercursoDirector está sem Leaning Actor — não há em quem pôr o AudioSource.\n\n" +
                       "Rode primeiro: Tools > The Delivery > Estrada - Encontro do Leaning";
            }

            var trigger = so.FindProperty("leaningTriggerPoint").objectReferenceValue as Transform;
            float volume = so.FindProperty("leaningWhistleVolume").floatValue;
            float fadeIn = so.FindProperty("leaningWhistleFadeIn").floatValue;
            float fadeOut = so.FindProperty("leaningWhistleFadeOut").floatValue;

            AudioClip clip = LeaningEncounterSetup.AssignWhistle(clipField);
            bool importFixed = ConfigureClipImport(clip);

            // O source já ligado no director vence; senão, um que já esteja no ator (para
            // não empilhar um segundo em cima do primeiro a cada execução); senão, novo.
            source = sourceField.objectReferenceValue as AudioSource;
            bool created = false;
            if (source == null)
                source = actor.GetComponent<AudioSource>();
            if (source == null)
            {
                source = Undo.AddComponent<AudioSource>(actor.gameObject);
                created = true;
            }

            var sb = new StringBuilder();

            // SEMPRE, inclusive sem force: são os dois que fazem o som tocar quando
            // ninguém pediu. Play On Awake dispara no load da cena; loop deixa o assovio
            // repetindo por cima da conversa.
            bool disarmed = source.playOnAwake || source.loop;
            source.playOnAwake = false;
            source.loop = false;

            if (created || force)
            {
                // A CURVA VEM DO DIRECTOR, não de uma cópia daqui: ele precisa saber
                // montar o mesmo source em runtime (quando o campo está vazio), e duas
                // contas de alcance em dois arquivos significam afinar o som no Editor e
                // ouvir outro no Play.
                PercursoDirector.ApplyWhistleSpatialSettings(source, Range(actor, trigger));
                source.volume = Mathf.Clamp01(volume);
            }

            // O clipe no source é para o EDITOR: é ele que permite dar play no preview do
            // Inspector e ouvir o assovio sem entrar no Play. Em runtime o director o
            // reatribui de qualquer jeito antes de tocar.
            if (clip != null)
                source.clip = clip;

            EditorUtility.SetDirty(source);

            AudioSource call = EnsureCallSource(so, actor, source, trigger, force, out bool callCreated);
            AudioClip callClip = so.FindProperty("leaningCall").objectReferenceValue as AudioClip;
            bool callImportFixed = ConfigureClipImport(callClip);

            sourceField.objectReferenceValue = source;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(director);

            sb.AppendLine(created
                ? $"AudioSource CRIADO em \"{actor.name}\" e ligado no PercursoDirector."
                : $"AudioSource de \"{actor.name}\" reaproveitado e ligado no PercursoDirector.");
            sb.AppendLine();

            sb.AppendLine(clip != null
                ? $"• Clipe: \"{clip.name}\" ({clip.length:0.0} s)"
                : "• Clipe: NENHUM — largue um assovio em Assets/_Project/SoundEffects/whistle.mp3 e rode de novo");

            sb.AppendLine($"• Fade in: {fadeIn:0.00} s | fade out: {fadeOut:0.00} s" +
                          (fadeIn <= 0f
                              ? "  <- em ZERO: o assovio entra no volume cheio e ESTALA no ataque. " +
                                "Rode Tools > The Delivery > Estrada - Encontro do Leaning para gravar o padrão na cena."
                              : string.Empty));

            if (created || force)
            {
                sb.AppendLine($"• 3D (Spatial Blend 1), sem doppler, rolloff linear");
                sb.AppendLine($"• Alcance: {source.minDistance:0.0} m a {source.maxDistance:0.0} m");

                if (trigger != null)
                {
                    float toTrigger = Vector3.Distance(actor.position, trigger.position);
                    float atTrigger = Mathf.Clamp01((source.maxDistance - toTrigger) /
                                                    Mathf.Max(0.01f, source.maxDistance - source.minDistance));
                    sb.AppendLine($"  (o gatilho está a {toTrigger:0.0} m do ator, e o alcance foi calculado para o " +
                                  $"assovio chegar lá com {atTrigger:0.00} do volume — longe o bastante para SOAR " +
                                  "longe, alto o bastante para ela notar)");
                }
                else
                {
                    sb.AppendLine("  (o director está sem Leaning Trigger Point: sem essa distância não há geometria " +
                                  "para calcular, e o alcance ficou no mínimo)");
                }

                sb.AppendLine($"• Volume: {source.volume:0.00}");
            }
            else
            {
                sb.AppendLine("• Rolloff e distâncias PRESERVADOS (já estavam configurados à mão)");
            }

            sb.AppendLine("• Play On Awake e Loop desligados" + (disarmed ? " (estavam ligados)" : ""));

            sb.AppendLine();
            sb.AppendLine(callCreated
                ? $"SEGUNDO AudioSource CRIADO em \"{actor.name}\" para o CHAMADO (a voz dele no beat 2), com a mesma " +
                  "curva do assovio. São dois porque o chamado sai enquanto o assovio está em fade out, e um source " +
                  "só não faz as duas coisas."
                : $"AudioSource do CHAMADO reaproveitado em \"{actor.name}\".");
            sb.AppendLine(callClip != null
                ? $"• Clipe do chamado: \"{callClip.name}\" ({callClip.length:0.0} s) | volume {call.volume:0.00}"
                : "• Clipe do chamado: NENHUM — arraste o one-shot da voz dele no campo Leaning Call do director. " +
                  "Sem ele, ela vira só com o assovio morrendo.");

            if (callImportFixed)
                sb.AppendLine("• IMPORT DO CLIPE DO CHAMADO CORRIGIDO (mesmo motivo do assovio).");

            if (importFixed)
            {
                sb.AppendLine("• IMPORT DO CLIPE CORRIGIDO: Preload Audio Data ligado, Load In Background desligado, " +
                              "Decompress On Load. Sem preload, o primeiro Play() de um clipe ainda não carregado sai " +
                              "atrasado — ou não sai — e o sintoma é o assovio \"não tocar\" justamente na primeira " +
                              "vez, que é a única que importa aqui.");
            }

            if (clip != null && clip.name.Contains("humming"))
            {
                sb.AppendLine();
                sb.AppendLine("O clipe é um PLACEHOLDER: o projeto ainda não tem um assovio. Serve para afinar o " +
                              "timing, não para ficar. Largue o som certo em whistle.mp3, limpe o campo Leaning " +
                              "Whistle no director e rode este comando de novo.");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Deixa o clipe pronto para DISPARAR NA HORA. Um cue de gatilho não pode ter
        /// latência: ele toca no quadro exato em que a Clear cruza o ponto, uma vez só, e
        /// se atrasar meio segundo o giro dela deixa de ser resposta a ele.
        ///
        /// O CULPADO É O <c>preloadAudioData</c> DESLIGADO: sem preload, os dados do
        /// clipe só começam a ser carregados no primeiro <c>Play()</c>, e o som sai
        /// atrasado — ou não sai. E o sintoma é o pior possível para debugar: ele falha
        /// justamente na PRIMEIRA vez, que num beat one-shot é a única que existe.
        ///
        /// Decompress On Load é o certo para um cue curto (o contrário do que a
        /// <see cref="AmbientMusicSetup"/> faz com a trilha, que é longa e vai para
        /// Streaming). No-op se já estiver assim — evita um reimport a cada execução.
        /// </summary>
        private static bool ConfigureClipImport(AudioClip clip)
        {
            if (clip == null)
                return false;

            string path = AssetDatabase.GetAssetPath(clip);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                return false;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;

            bool ok = settings.preloadAudioData
                   && !importer.loadInBackground
                   && settings.loadType == AudioClipLoadType.DecompressOnLoad;
            if (ok)
                return false;

            settings.preloadAudioData = true;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            Debug.Log($"[LeaningWhistleSetup] Import de \"{clip.name}\" ajustado: Preload Audio Data ligado, " +
                      "Load In Background desligado, Decompress On Load.");
            return true;
        }

        /// <summary>
        /// Põe (ou reaproveita) o SEGUNDO AudioSource do ator, o do chamado, e o liga no
        /// director.
        ///
        /// POR QUE UM SEGUNDO, no mesmo objeto: os dois sons saem do mesmo lugar do mundo
        /// mas têm vidas independentes. No instante em que o chamado sai, o assovio está
        /// em pleno fade out — e como o volume de um source é a alça desse fade, um
        /// one-shot tocado por ele sairia descendo junto, e o Stop() do fim do fade o
        /// cortaria no meio.
        ///
        /// A BUSCA IGNORA O SOURCE DO ASSOVIO de propósito: sem isso, rodar o comando
        /// ligaria os dois campos no MESMO componente, que é exatamente o problema que
        /// este segundo source existe para não ter.
        /// </summary>
        private static AudioSource EnsureCallSource(
            SerializedObject so, Transform actor, AudioSource whistle, Transform trigger, bool force, out bool created)
        {
            SerializedProperty field = so.FindProperty("leaningCallSource");
            var call = field.objectReferenceValue as AudioSource;
            created = false;

            if (call == null)
            {
                foreach (AudioSource existing in actor.GetComponents<AudioSource>())
                {
                    if (existing == whistle)
                        continue;

                    call = existing;
                    break;
                }
            }

            if (call == null)
            {
                call = Undo.AddComponent<AudioSource>(actor.gameObject);
                created = true;
            }

            call.playOnAwake = false;
            call.loop = false;

            if (created || force)
            {
                PercursoDirector.ApplyWhistleSpatialSettings(call, Range(actor, trigger));
                call.volume = Mathf.Clamp01(so.FindProperty("leaningCallVolume").floatValue);
            }

            EditorUtility.SetDirty(call);
            field.objectReferenceValue = call;
            return call;
        }

        /// <summary>
        /// A distância que o assovio precisa vencer: do ator até o gatilho, que é onde a
        /// Clear está no instante em que o som nasce. Só a MEDIDA sai daqui — quem a
        /// transforma em Max Distance é o
        /// <see cref="PercursoDirector.ApplyWhistleSpatialSettings"/>.
        /// </summary>
        private static float Range(Transform actor, Transform trigger)
        {
            return trigger != null ? Vector3.Distance(actor.position, trigger.position) : 0f;
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[LeaningWhistleSetup] {message}");
            EditorUtility.DisplayDialog("Assovio do Leaning", message, "Ok");
        }
    }
}
