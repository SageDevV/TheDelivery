using System.Collections;
using TheDelivery.Core;
using TheDelivery.FX;
using TheDelivery.Interaction;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheDelivery.UI
{
    /// <summary>
    /// CENÁRIO 3D DA TELA DE INÍCIO: carrega a cena do apartamento ADITIVAMENTE por baixo
    /// do MainMenu e a transforma num "set de filmagem" — só ambiente e luz, nada de jogo.
    ///
    /// Por que aditivo, e não o menu dentro da Apartment: a Apartment é o Ato 3/4, cheia
    /// de directors, IA e um player que começam a agir no Start. Mantê-la como cena
    /// HÓSPEDE, com o MainMenu dono da câmera/áudio/UI, deixa o gameplay dela desligado
    /// por construção, e o "Novo Jogo" descarrega as duas de uma vez (o load Single do
    /// GameManager leva tudo embora) — nenhum estado do cenário vaza para a partida.
    ///
    /// O desligamento acontece no <c>sceneLoaded</c>, que roda DEPOIS do Awake/OnEnable
    /// dos objetos da cena e ANTES de qualquer Start — e é no Start que os directors
    /// começam os beats. Desligados ali, eles nunca chegam a começar.
    ///
    /// Conflitos de câmera/áudio: a câmera do player está dentro de _Actors (desligado);
    /// qualquer outra Camera ou AudioListener da cena hóspede é desligada também. A ÚNICA
    /// câmera e o ÚNICO listener ativos são os do MainMenu.
    /// </summary>
    public sealed class MainMenuBackdrop : MonoBehaviour
    {
        [Header("Cena de fundo")]
        [SerializeField] private GameScene backdropScene = GameScene.Apartment;
        [Tooltip("Raízes da cena de fundo que são GAMEPLAY e ficam desligadas no menu.")]
        [SerializeField] private string[] rootsToDisable = { "_Actors", "_Managers", "_UI", "_PatrolPoints" };

        [Header("Câmera")]
        [Tooltip("A câmera do menu (desta cena). Vai para o marcador abaixo quando o cenário carrega.")]
        [SerializeField] private Camera menuCamera;
        [Tooltip("Nome do Transform, na cena de fundo, com a pose da câmera do menu.")]
        [SerializeField] private string cameraPointName = "MenuCameraPoint";

        [Header("Revelação")]
        [Tooltip("Imagem que cobre a tela até o cenário estar pronto (o Background do Canvas). Some com fade.")]
        [SerializeField] private Graphic cover;
        [Tooltip("Alpha final da cobertura: um véu escuro leve mantém o título e os botões legíveis sobre o 3D.")]
        [Range(0f, 1f)]
        [SerializeField] private float coverFinalAlpha = 0.35f;
        [Min(0.01f)]
        [SerializeField] private float revealDuration = 1.6f;

        [Header("Composição")]
        [Tooltip("Portas (por nome do GameObject) que aparecem ABERTAS no cenário do menu.")]
        [SerializeField] private string[] doorsToOpen = { "ApartmentDoor" };
        [Tooltip("Objetos da cena de fundo autorados DESLIGADOS que só existem no menu (ex.: a sombra " +
                 "do homem no corredor). São ligados ao montar o cenário.")]
        [SerializeField] private string[] objectsToActivate = { "MenuShadowFigure" };

        [Header("Sirenes")]
        [Tooltip("Liga (só as LUZES, sem som) as sirenes ativas do cenário: o vermelho/azul da " +
                 "viatura lá fora varrendo a sala pela janela.")]
        [SerializeField] private bool startSirenLights = true;

        [Header("Luzes")]
        [Tooltip("Liga os FlickeringLight da cena de fundo (autorados DESLIGADOS lá, para piscarem só no menu).")]
        [SerializeField] private bool enableFlickeringLights = true;

        private Scene backdrop;
        private bool requested;

        private void OnEnable() => SceneManager.sceneLoaded += HandleSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= HandleSceneLoaded;

        private void Start()
        {
            string sceneName = backdropScene.ToString();
            Scene already = SceneManager.GetSceneByName(sceneName);
            if (already.IsValid() && already.isLoaded)
            {
                // Já estava aberta (ex.: as duas abertas juntas no Editor ao dar Play).
                Setup(already);
                return;
            }

            requested = true;
            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (load == null)
            {
                Debug.LogError($"[MainMenuBackdrop] Não consegui carregar \"{sceneName}\" (está no Build Settings?). " +
                               "O menu segue sem cenário.", this);
                StartCoroutine(Reveal(1f)); // mantém a cobertura opaca: melhor fundo preto que tela vazia
            }
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!requested || mode != LoadSceneMode.Additive || scene.name != backdropScene.ToString())
                return;
            Setup(scene);
        }

        private void Setup(Scene scene)
        {
            backdrop = scene;
            AudioListener menuListener = menuCamera != null ? menuCamera.GetComponent<AudioListener>() : null;
            Transform point = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(rootsToDisable, root.name) >= 0)
                {
                    root.SetActive(false);
                    continue;
                }

                if (point == null)
                    point = FindDeep(root.transform, cameraPointName);

                // Sobrou alguma câmera/listener fora das raízes de gameplay? Desliga.
                foreach (Camera c in root.GetComponentsInChildren<Camera>(true))
                    c.enabled = false;
                foreach (AudioListener l in root.GetComponentsInChildren<AudioListener>(true))
                    l.enabled = false;

                if (enableFlickeringLights)
                    foreach (FlickeringLight f in root.GetComponentsInChildren<FlickeringLight>(true))
                        f.enabled = true;

                // Só as sirenes ATIVAS (a viatura estacionada lá fora). As das viaturas que
                // chegam no Ato 4 estão desligadas na cena e continuam assim.
                if (startSirenLights)
                    foreach (SirenLightEffect siren in root.GetComponentsInChildren<SirenLightEffect>(false))
                        siren.StartSiren(playAudio: false);

                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (System.Array.IndexOf(objectsToActivate, t.name) >= 0)
                        t.gameObject.SetActive(true);

                foreach (Door door in root.GetComponentsInChildren<Door>(true))
                    if (System.Array.IndexOf(doorsToOpen, door.name) >= 0)
                        door.SetOpenInstant(true);
            }

            if (menuListener != null)
                menuListener.enabled = true;

            // A cena ATIVA dita skybox, luz ambiente, fog e reflexos: sem isto a sala seria
            // iluminada com o ambiente vazio do MainMenu.
            SceneManager.SetActiveScene(scene);

            if (menuCamera != null)
            {
                if (point != null)
                    menuCamera.transform.SetPositionAndRotation(point.position, point.rotation);
                else
                    Debug.LogWarning($"[MainMenuBackdrop] \"{cameraPointName}\" não existe em {scene.name}; " +
                                     "a câmera do menu fica onde está. Rode Tools/The Delivery/Main Menu - Cenário.", this);

                menuCamera.clearFlags = CameraClearFlags.Skybox;
                // O tratamento visual (Global Volume do GameManager + o da Apartment) só
                // aparece com pós-processamento ligado na câmera.
                menuCamera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            }

            StartCoroutine(Reveal(coverFinalAlpha));
        }

        private IEnumerator Reveal(float finalAlpha)
        {
            if (cover == null)
                yield break;

            // Dois quadros para o primeiro frame pesado da sala passar escondido.
            yield return null;
            yield return null;

            Color c = cover.color;
            float start = c.a;
            float t = 0f;
            while (t < revealDuration)
            {
                t += Time.unscaledDeltaTime;
                c.a = Mathf.Lerp(start, finalAlpha, Mathf.SmoothStep(0f, 1f, t / revealDuration));
                cover.color = c;
                yield return null;
            }
            c.a = finalAlpha;
            cover.color = c;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }
    }
}
