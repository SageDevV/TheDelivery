using TheDelivery.FX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// LIGA O CÉU DA NOITE ao <see cref="NightfallController"/> da Estrada: quando o
    /// anoitecer fecha, o Skybox/Procedural do fim de tarde sai de cena e entra o
    /// "Cold Night" do AllSky.
    ///
    /// POR QUE TROCAR O MATERIAL EM VEZ DE SÓ ESCURECER: o Skybox/Procedural desenha
    /// espalhamento atmosférico, não firmamento. Ele sabe ficar escuro — e a rampa do
    /// anoitecer já o leva até lá — mas por baixo continua sendo um degradê liso: sem
    /// estrelas, sem nada para o olho pousar. Escurecer mais só troca um azul chapado
    /// por um azul mais fundo. O céu noturno tem que VIR DE UM ASSET.
    ///
    /// A TROCA em si mora no runtime (<c>NightfallController.nightSkybox</c>), que a
    /// faz pelo brilho, em três tempos — o céu do poente baixa até um fiapo, segura ali
    /// um instante (é aí que o material muda, invisível) e o céu da noite acende dali
    /// para cima, devagar. Os dois shaders não se misturam, então essa é a passagem
    /// possível.
    ///
    /// O FIAPO É O PONTO. A passagem NÃO vai até o preto: um céu totalmente apagado não
    /// é um céu escuro, é a ausência dele, e o Cold Night entrando depois lê como outra
    /// imagem em vez da mesma noite avançando. Os dois se encontram num brilho baixo
    /// comum — o <c>nightSkyboxHandoff</c>, que este comando grava na cena junto com a
    /// referência.
    ///
    /// Este comando só existe para as duas coisas não dependerem de alguém lembrar de
    /// pôr à mão.
    ///
    /// IDEMPOTENTE: rodar de novo com tudo no lugar não faz nada além de avisar, e um
    /// nível de encontro já afinado à mão não é tocado. Os ajustes finos (duração dos
    /// fades, exposure) ficam no Inspector, na seção "3c. Céu da noite fechada".
    /// </summary>
    public static class NightSkyboxSetup
    {
        private const string ScenePath = "Assets/Scenes/Estrada.unity";

        /// <summary>
        /// O "Cold Night" de 6 faces (Skybox/6 Sided). O pacote também traz uma versão
        /// Equirect do mesmo céu; a de 6 faces é a que casa com o resto da montagem e
        /// tem <c>_Exposure</c>, que é a propriedade pela qual o fade acontece.
        /// </summary>
        private const string SkyboxPath = "Assets/AllSkyFree/Cold Night/Cold Night.mat";

        private const string FieldName = "nightSkybox";

        /// <summary>
        /// O nível de brilho em que o céu do poente e o da noite se encontram, e o valor
        /// que este comando grava quando ele está em zero.
        ///
        /// POR QUE PRECISA SER GRAVADO: o campo é NOVO, e o Unity desserializa como ZERO
        /// um float que não existia quando a cena foi salva — o padrão escrito no C# não
        /// alcança um componente que já está numa cena. E zero, neste campo, é
        /// exatamente a passagem pelo preto que ele veio consertar.
        /// </summary>
        private const string HandoffField = "nightSkyboxHandoff";

        private const float DefaultHandoff = 0.25f;

        [MenuItem("Tools/The Delivery/Estrada - Céu da noite (Cold Night)")]
        private static void Run()
        {
            var skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (skybox == null)
            {
                Fail($"Material não encontrado:\n{SkyboxPath}");
                return;
            }

            Scene scene = EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            NightfallController nightfall = FindNightfall(scene);
            if (nightfall == null)
            {
                Fail("NightfallController não encontrado na cena Estrada.");
                return;
            }

            var so = new SerializedObject(nightfall);
            SerializedProperty field = so.FindProperty(FieldName);
            if (field == null)
            {
                Fail($"O campo \"{FieldName}\" não existe no NightfallController.");
                return;
            }

            bool linkedNow = field.objectReferenceValue != skybox;
            if (linkedNow)
                field.objectReferenceValue = skybox;

            // O NÍVEL DE ENCONTRO, pelo mesmo motivo da referência: sem ele gravado a
            // cena roda com zero, e zero é a troca passando pelo preto.
            //
            // SÓ SOBE DO ZERO. Um valor já afinado no Inspector é o ajuste de alguém que
            // estava olhando para o céu, e este comando não tem o que dizer sobre ele.
            SerializedProperty handoff = so.FindProperty(HandoffField);
            bool handoffNow = handoff != null && handoff.floatValue <= 0f;
            if (handoffNow)
                handoff.floatValue = DefaultHandoff;

            if (!linkedNow && !handoffNow)
            {
                Debug.Log($"[NightSkyboxSetup] \"{skybox.name}\" já está no Night Skybox do NightfallController, e o " +
                          "nível de encontro dos dois céus já tem valor.", nightfall);
                EditorUtility.DisplayDialog("Céu da noite",
                                            $"\"{skybox.name}\" já está ligado, e a troca já tem nível de encontro.\n\n" +
                                            "Os tempos da transição, o nível de encontro e a exposure se ajustam no " +
                                            "Inspector, em \"3c. Céu da noite fechada\".",
                                            "Ok");
                return;
            }

            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(nightfall);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            string done = linkedNow
                ? $"\"{skybox.name}\" ligado ao NightfallController"
                : "Nível de encontro dos dois céus gravado";
            if (linkedNow && handoffNow)
                done = $"\"{skybox.name}\" ligado ao NightfallController e nível de encontro dos dois céus gravado";

            Debug.Log($"[NightSkyboxSetup] {done}; cena Estrada salva.", nightfall);
            EditorUtility.DisplayDialog("Céu da noite",
                                        $"{done}, e a cena Estrada foi salva.\n\n" +
                                        "Quando o anoitecer fechar, o céu procedural baixa até um fiapo de brilho e " +
                                        "este acende dali para cima — sem passar pelo preto, que é o que fazia a " +
                                        "troca saltar aos olhos.\n\n" +
                                        "Ajustes no Inspector, em \"3c. Céu da noite fechada\":\n" +
                                        "• Night Skybox Start At — em que ponto do anoitecer a troca começa (0.8 = no\n" +
                                        "  fim da rampa, escondida atrás do sol descendo; 1 = só depois da noite fechar)\n" +
                                        "• Night Skybox Fade Out — segundos baixando o céu do poente\n" +
                                        "• Night Skybox Hold — a pausa no fiapo, onde a troca acontece\n" +
                                        "• Night Skybox Fade In — segundos acendendo as estrelas (deixe o mais longo)\n" +
                                        $"• Night Skybox Handoff — o fiapo em si ({DefaultHandoff:0.00} agora): suba se o céu\n" +
                                        "  some no meio da troca, baixe se ela pula para mais claro\n" +
                                        "• Night Skybox Exposure — multiplicador do brilho do céu novo",
                                        "Ok");
        }

        private static NightfallController FindNightfall(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                NightfallController found = root.GetComponentInChildren<NightfallController>(includeInactive: true);
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
            Debug.LogError($"[NightSkyboxSetup] {message}");
            EditorUtility.DisplayDialog("Céu da noite", message, "Ok");
        }
    }
}
