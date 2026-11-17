using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TheDelivery.Core
{
    /// <summary>
    /// PLAY ISOLADO DE UM ATO, SÓ NO EDITOR. Dando Play direto na cena de um ato (sem a Boot),
    /// cria o GameManager da Boot (prefab) ANTES de a cena acordar e marca o ato daquela cena —
    /// o ato roda como se tivesse chegado pelo fluxo normal: tratamento visual global (VHS),
    /// fade, estamina com o ajuste da cena, save/Game Over/troca de ato funcionando.
    ///
    /// Qual ato: o da cena no <see cref="GameActCatalog"/>. Cena com mais de um ato (Apartment:
    /// Atos 3 e 4) usa o diretor marcado com autoStartForDebug; sem marca, o primeiro da história.
    ///
    /// Isolado NÃO salva na chegada (não sobrescreve o save de quem está testando). Liga/desliga
    /// em Tools/The Delivery/Play isolado - subir o GameManager. Em build não existe.
    /// </summary>
    public static class IsolatedPlayBootstrap
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/GameManager.prefab";
        private const string PrefKey = "TheDelivery.IsolatedPlayBootstrap";

#if UNITY_EDITOR
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        // BeforeSceneLoad: os objetos da cena já estão na memória, mas nenhum Awake rodou — o
        // GameManager (e o StaminaSystem dele) nasce primeiro e os diretores já o encontram no Start.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!Enabled || GameManager.Instance != null)
                return;

            string sceneName = SceneManager.GetActiveScene().name;
            if (!System.Enum.TryParse(sceneName, out GameScene scene) || scene == GameScene.Boot || scene == GameScene.MainMenu)
                return;

            GameAct act = ResolveAct(scene);
            if (act == GameAct.None)
                return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[IsolatedPlay] {PrefabPath} não existe — rode Tools/The Delivery/Play isolado - Criar prefab do GameManager. " +
                                 "Seguindo sem GameManager.");
                return;
            }

            GameManager.PrepareIsolatedPlay(act);
            Object.Instantiate(prefab).name = "GameManager (Play isolado)";
            Debug.Log($"[IsolatedPlay] Play direto em \"{sceneName}\": GameManager criado, ato {act}. " +
                      "(Desligue em Tools/The Delivery/Play isolado - subir o GameManager.)");
        }

        private static GameAct ResolveAct(GameScene scene)
        {
            GameAct first = GameAct.None;
            foreach (GameActCatalog.ActInfo info in GameActCatalog.Story)
            {
                if (info.Scene != scene) continue;
                if (first == GameAct.None) first = info.Act;
                if (DirectorWantsAutoStart(info.Act)) return info.Act;
            }
            return first;
        }

        /// <summary>O diretor deste ato está com autoStartForDebug ligado na cena?</summary>
        private static bool DirectorWantsAutoStart(GameAct act)
        {
            string type = act switch
            {
                GameAct.ActNightmare => "NightmareDirector",
                GameAct.Act1 => "Act1Director",
                GameAct.ActJourney => "JourneyDirector",
                GameAct.Act2 => "Act2Director",
                GameAct.Act3 => "Act3Director",
                GameAct.Act4 => "Act4Director",
                _ => null,
            };
            if (type == null) return false;
            foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (mb == null || mb.GetType().Name != type) continue;
                var f = mb.GetType().GetField("autoStartForDebug",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                return f != null && f.GetValue(mb) is bool b && b;
            }
            return false;
        }
#else
        public static bool Enabled => false;
#endif
    }
}
