using System;
using UnityEngine;

namespace TheDelivery.Interaction
{
    /// <summary>
    /// Telefone fixo da sala. É o MESMO objeto usado em dois momentos da cena
    /// compartilhada do apartamento: no Ato 3 (Beat 5, "ligação muda") a Clear ATENDE
    /// o telefone que toca; no Ato 4 (Beat 5) ela corre até ele para LIGAR. Implementa
    /// <see cref="IInteractable"/> mas só responde DEPOIS de ser ARMADO por um Director.
    /// Antes disso <see cref="CanInteract"/> é false e nem o prompt aparece.
    ///
    /// A notificação é DESACOPLADA do Director concreto (como no <see cref="DeadPhone"/>):
    /// ao ser usado, apenas INVOCA o callback registrado em <see cref="Arm(Action,string)"/>.
    /// Cada Director arma com o seu próprio callback (e, opcionalmente, o seu próprio
    /// prompt — "Atender o telefone" no Ato 3, "Usar o telefone" no Ato 4). Como
    /// <see cref="Arm(Action,string)"/> sobrescreve, os atos não conflitam mesmo rodando
    /// na MESMA cena. Injetar o callback em runtime evita referência circular no Inspector.
    /// </summary>
    public sealed class Landline : MonoBehaviour, IInteractable
    {
        [Header("Landline")]
        [Tooltip("Verbo exibido no prompt por padrão. Um Director pode sobrescrever ao armar (ex.: \"Atender o telefone\" no Ato 3).")]
        [SerializeField] private string interactionPrompt = "Usar o telefone";

        private Action onUsed;
        private string promptOverride;
        private bool armed;

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt =>
            string.IsNullOrEmpty(promptOverride) ? interactionPrompt : promptOverride;

        /// <summary>Só interativo após um Director armar (com um callback).</summary>
        public bool CanInteract => armed && onUsed != null;

        /// <summary>Chamado pelo PlayerInteraction ao apertar F: avisa o Director.</summary>
        public void Interact(PlayerInteraction source)
        {
            if (!CanInteract)
                return;

            armed = false; // consome: uma interação só (o Director também guarda contra duplo disparo).
            onUsed.Invoke();
        }

        // --- API do Director ----------------------------------------------

        /// <summary>
        /// Liga a interação e registra o callback a ser invocado quando o player usar o
        /// telefone (F). <paramref name="promptOverride"/> opcional troca o verbo do prompt
        /// para o contexto do ato (ex.: "Atender o telefone"); se nulo/vazio, usa o padrão.
        /// Sobrescreve um arme anterior.
        /// </summary>
        public void Arm(Action onUsed, string promptOverride = null)
        {
            this.onUsed = onUsed;
            this.promptOverride = promptOverride;
            armed = true;
        }

        /// <summary>
        /// Desarma sem notificar — para o prompt sumir e o telefone não ficar interativo
        /// fora do beat que o usa (ex.: atendido, ou salto de beat em debug).
        /// </summary>
        public void Disarm()
        {
            armed = false;
        }
    }
}
