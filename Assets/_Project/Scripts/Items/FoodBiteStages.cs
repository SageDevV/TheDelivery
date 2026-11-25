using UnityEngine;

namespace TheDelivery.Items
{
    /// <summary>
    /// Comida que vai sendo comida: cada estágio é um filho (inteiro, 1ª mordida, 2ª, 3ª...). Só um
    /// fica ativo. <see cref="Advance"/> passa para o próximo (o Act3Director chama uma vez por
    /// mordida, quando a comida chega à boca). Fica no prefab segurado na mão (ItemData.HeldPrefab).
    /// </summary>
    public sealed class FoodBiteStages : MonoBehaviour
    {
        [Tooltip("Estágios em ordem: inteiro primeiro, depois cada mordida.")]
        [SerializeField] private GameObject[] stages;

        private int current;

        public int StageCount => stages != null ? stages.Length : 0;
        public int CurrentStage => current;

        private void Awake() => Show(0);

        /// <summary>Mostra o estágio seguinte. Devolve false se já estava no último.</summary>
        public bool Advance()
        {
            if (stages == null || current >= stages.Length - 1)
                return false;
            Show(current + 1);
            return true;
        }

        public void Show(int index)
        {
            if (stages == null || stages.Length == 0)
                return;
            current = Mathf.Clamp(index, 0, stages.Length - 1);
            for (int i = 0; i < stages.Length; i++)
                if (stages[i] != null)
                    stages[i].SetActive(i == current);
        }
    }
}
