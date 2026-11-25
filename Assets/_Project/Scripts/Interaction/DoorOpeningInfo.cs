using UnityEngine;

namespace TheDelivery.Interaction
{
    /// <summary>
    /// Guarda as medidas do VÃO de uma porta (bounds da folha original, antes da troca pelo modelo novo). Fica na
    /// guarnição (DoorFrame_*) para o ApartmentDoorsSetup poder ser rodado de novo sem medir a folha nova. Só dados.
    /// </summary>
    public sealed class DoorOpeningInfo : MonoBehaviour
    {
        public Vector3 center;
        public Vector3 size;
    }
}
