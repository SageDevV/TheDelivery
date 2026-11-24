using UnityEngine;

namespace TheDelivery.Player
{
    /// <summary>
    /// O PISO DESTA CENA (G-02, 2026-11-23). O player é um prefab só para o jogo inteiro e anda com o loop
    /// genérico de caminhada; uma cena que tem chão próprio (o piso frio da Recepção) põe este componente
    /// com as variações de passo daquele chão, e no Start ele troca os passos do player para elas
    /// (<see cref="PlayerController.SetFootstepSurface"/>). Cena sem este componente = passos de sempre.
    /// Montado pelos setups de áudio de cada cena (ex.: ReceptionAudioSetup).
    /// </summary>
    public sealed class SceneFootsteps : MonoBehaviour
    {
        [Tooltip("O player da cena.")]
        [SerializeField] private PlayerController player;
        [Tooltip("Variações de passo deste chão (3–6 evitam o padrão repetido).")]
        [SerializeField] private AudioClip[] stepClips;
        [Tooltip("Volume do passo andando (negativo = mantém o do player).")]
        [SerializeField] private float walkVolume = 0.4f;
        [Tooltip("Volume do passo correndo.")]
        [SerializeField] private float runVolume = 0.62f;
        [Tooltip("Volume do passo agachada (baixo: agachar é não ser ouvida).")]
        [SerializeField] private float crouchVolume = 0.14f;

        private void Start()
        {
            if (player == null)
            {
                Debug.LogWarning("[SceneFootsteps] Player não atribuído; os passos ficam os de sempre.", this);
                return;
            }
            player.SetFootstepSurface(stepClips, walkVolume, runVolume, crouchVolume);
        }
    }
}
