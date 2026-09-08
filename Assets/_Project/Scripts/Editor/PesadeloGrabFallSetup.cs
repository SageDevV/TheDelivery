using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Acerta na CENA os campos da queda cujo valor gravado ficou para trás do beat.
    ///
    /// POR QUE ISTO EXISTE em vez de "mude uns números no Inspector": nenhum deles QUEBRA
    /// alguma coisa — eles só entregam uma cena pior sem dizer por quê, que é o tipo de
    /// pendência que fica para sempre. E como os campos ganham padrões novos a cada rodada de
    /// ajuste, um padrão novo NÃO alcança uma cena que já serializou o valor antigo: é este
    /// comando que faz a ponte.
    ///
    ///   • <c>grabLingerDuration</c> — o tempo entre o pouso e o corte. Era 0,7 quando o
    ///     último frame do pesadelo era a Clear no chão; agora atrasa o baque.
    ///   • <c>grabDropGravityScale</c> — a duração da QUEDA LIVRE. Em 1 (a gravidade do
    ///     mundo) ela dura menos de um segundo e some antes de ser lida como queda.
    ///   • <c>grabFallDuration</c> — a duração de uma queda MARCADA, escrito só quando há
    ///     Fall Landing Point. Ver o comentário no corpo para por que só nesse caso.
    ///   • <c>grabFallLookPitch</c> — para onde ela olha caindo. Positivo mira o chão, que
    ///     conta a história de quem se atirou; ela foi SOLTA.
    ///
    /// O <c>grabReleaseNormalizedTime</c> NÃO entra: é o quadro em que a mão abre, uma POSE,
    /// e o comando não tem como saber qual é. Ele diz o que fazer em vez de chutar — ver
    /// <see cref="ReleaseAdvice"/>.
    ///
    /// Idempotente: rodar de novo não muda nada, e diz isso.
    /// </summary>
    public static class PesadeloGrabFallSetup
    {
        [MenuItem("Tools/The Delivery/Pesadelo - Queda da Pegada (ajustar a cena)")]
        private static void Run()
        {
            Scene scene = PesadeloGrabSetup.EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            PesadeloDirector director = PesadeloGrabSetup.FindDirector(scene);
            if (director == null)
            {
                EditorUtility.DisplayDialog(
                    "Queda da Pegada",
                    "PesadeloDirector não encontrado na cena Pesadelo.",
                    "Ok");
                return;
            }

            var so = new SerializedObject(director);

            // ZERO. Quem acorda de uma queda acorda NO baque: o som do corte tem que cair no
            // mesmo quadro em que ela bate no chão.
            bool linger = Set(so, "grabLingerDuration", 0f);

            // A GRAVIDADE REAL É CURTA DEMAIS AQUI. Solta de dois metros e meio, uma queda
            // honesta dura 0,7 s e some antes de o jogador entender que ela está caindo. 0.4
            // alonga em ~58% (o tempo cresce com a raiz do inverso), que é o "um pouco mais"
            // sem a Clear começar a boiar. Só vale quando a queda é livre.
            bool gravity = Set(so, "grabDropGravityScale", 0.4f);

            // O PRAZO SÓ É ESCRITO QUANDO HÁ MARCADOR DE POUSO, e é a única decisão deste
            // comando que depende do estado da cena.
            //
            // Sem marcador, a Clear cai do braço da criatura e a física dá um tempo bom
            // sozinha — fixar segundos ali seria trocar uma medida por um palpite. COM
            // marcador, a distância é uma decisão de cena e pode ser qualquer uma, e aí a
            // queda livre não serve: um quilômetro leva 15 s na gravidade do mundo, e mexer
            // na gravidade só faz demorar mais. É o caso desta cena, e é onde o prazo passa a
            // ser o parafuso certo.
            bool duration = so.FindProperty("fallLandingPoint")?.objectReferenceValue != null
                            && Set(so, "grabFallDuration", 5f);

            // NEGATIVO OLHA PARA CIMA: ela foi solta, e o que fica em quadro durante a queda
            // é a criatura ficando para trás lá em cima.
            bool look = Set(so, "grabFallLookPitch", -50f);

            // A SOLTURA NÃO É ESCRITA AQUI, de propósito. O quadro em que a mão abre é uma
            // POSE, e pose não se deduz — quem acha é o olho. Escrever um número plausível
            // deixaria o campo parecendo autorado sem nunca ter sido olhado, que é o pior dos
            // dois mundos: sem aviso no Console e sem estar certo. Se o valor da cena estiver
            // fora, o log abaixo manda marcar.
            var release = so.FindProperty("grabReleaseNormalizedTime");
            float releaseValue = release != null ? release.floatValue : 0f;

            if (!linger && !gravity && !look && !duration)
            {
                Debug.Log("[PesadeloGrabFallSetup] A cena já está ajustada — nada a fazer.\n" +
                          ReleaseAdvice(releaseValue), director);
                return;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = director.gameObject;
            EditorGUIUtility.PingObject(director);

            Debug.Log("[PesadeloGrabFallSetup] Cena ajustada e salva: " +
                      (linger ? "corte no quadro do pouso; " : "") +
                      (gravity ? "gravidade da queda livre em 0.4; " : "") +
                      (duration ? "queda marcada em 5 s; " : "") +
                      (look ? "olhar para cima durante a queda; " : "") +
                      "o tempo agora é o Grab Fall Duration, no Inspector — mexa nele direto daqui em diante.\n" +
                      ReleaseAdvice(releaseValue),
                      director);
        }

        /// <summary>
        /// O que fazer com o ponto de soltura, dito em função do que está gravado na cena.
        ///
        /// Existe porque este é o único ajuste do beat que o comando NÃO pode fazer sozinho —
        /// e um comando que arruma três coisas e cala sobre a quarta deixa quem o rodou
        /// achando que acabou.
        /// </summary>
        private static string ReleaseAdvice(float release)
        {
            if (release >= 0.999f)
            {
                return "ATENÇÃO: o Grab Release Normalized Time está em 1, ou seja, no último quadro do clipe. " +
                       "A Clear fica pendurada numa mão já aberta até a animação acabar. Abra Tools ▸ The Delivery " +
                       "▸ Pesadelo - Ajustar a Pegada, ache o quadro em que a mão abre e clique em \"Marcar a " +
                       "soltura neste quadro\" — e, se houver um Fall Start Point, use \"Recolocar os marcadores no " +
                       "cálculo automático\" depois, senão ele vai continuar apontando para a pose antiga.";
            }

            if (release <= 0f)
            {
                return "O Grab Release Normalized Time está em 0 (nunca marcado): o beat vai usar 0.8 e reclamar no " +
                       "Console a cada execução. Marque o quadro certo em Tools ▸ The Delivery ▸ Pesadelo - Ajustar " +
                       "a Pegada.";
            }

            return $"O ponto de soltura está em {release:0.###} — confira na janela de ajuste se é mesmo o quadro " +
                   "em que a mão abre.";
        }

        /// <returns>true se o valor mudou de fato — é o que decide se há o que salvar.</returns>
        private static bool Set(SerializedObject so, string path, float value)
        {
            SerializedProperty property = so.FindProperty(path);
            if (property == null)
            {
                Debug.LogWarning($"[PesadeloGrabFallSetup] O campo \"{path}\" não existe mais no PesadeloDirector.");
                return false;
            }

            if (Mathf.Approximately(property.floatValue, value))
                return false;

            Debug.Log($"[PesadeloGrabFallSetup] {path}: {property.floatValue} -> {value}");
            property.floatValue = value;
            return true;
        }
    }
}
