using System.Collections.Generic;
using TheDelivery.FX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Coloca o <see cref="TrafficCar"/> nos carros SELECIONADOS na Hierarchy, escalona a
    /// largada de cada um (<c>startDelay</c> crescente + uma pitada de aleatório) e AMARRA as
    /// rodas visuais de cada carro, para os pneus girarem sozinhos.
    ///
    /// Uso: selecione os carros na cena (raiz de cada um) e rode
    /// <c>Tools ▸ The Delivery ▸ Traffic - Configurar Carros Selecionados</c>. Carros que já
    /// tenham o componente são respeitados — o comando reescalona a largada e só preenche as
    /// rodas se a lista estiver VAZIA (nunca atropela o que você amarrou na mão).
    ///
    /// Depois, em cada carro: confira o <c>Forward Axis</c> (se ele andar de ré ou de lado) e
    /// o <c>Travel Distance</c> — o gizmo amarelo no Scene view mostra o percurso que ele vai
    /// fazer a partir de onde está.
    /// </summary>
    public static class TrafficCarSetup
    {
        /// <summary>Segundos entre a largada de um carro e a do seguinte.</summary>
        private const float StaggerStep = 2.5f;

        /// <summary>
        /// Pedaços de nome que denunciam uma roda. Cobre os packs em inglês (o
        /// HellaFlush usa "FrontLeftWheels"/"RearRightWheels") e nomes em português.
        /// </summary>
        private static readonly string[] WheelNameHints = { "wheel", "tire", "roda", "pneu" };

        [MenuItem("Tools/The Delivery/Traffic - Configurar Carros Selecionados")]
        public static void SetupSelectedCars()
        {
            GameObject[] selection = Selection.gameObjects;

            if (selection.Length == 0)
            {
                Debug.LogWarning("[TrafficCar] Selecione na Hierarchy os carros (a raiz de cada um) antes de rodar o comando.");
                return;
            }

            int added = 0;
            int restaggered = 0;
            int touched = 0;

            for (int i = 0; i < selection.Length; i++)
            {
                GameObject car = selection[i];

                // Só objetos DA CENA: prefab no Project não tem pose de origem pra capturar.
                if (EditorUtility.IsPersistent(car))
                    continue;

                TrafficCar traffic = car.GetComponent<TrafficCar>();

                if (traffic == null)
                {
                    traffic = Undo.AddComponent<TrafficCar>(car);
                    added++;
                }
                else
                {
                    restaggered++;
                }

                touched++;

                var serialized = new SerializedObject(traffic);

                // Largada escalonada: 0s, 2.5s, 5s... com jitter para não ficar metronômico.
                SerializedProperty startDelay = serialized.FindProperty("startDelay");
                if (startDelay != null)
                    startDelay.floatValue = i * StaggerStep + Random.Range(0f, StaggerStep * 0.4f);

                int wheelCount = AssignWheels(car, serialized);

                serialized.ApplyModifiedProperties();

                if (wheelCount == 0)
                {
                    Debug.LogWarning($"[TrafficCar] '{car.name}': não achei as rodas pelo nome. " +
                                     "Arraste os objetos das rodas para a lista Wheels no Inspector.", car);
                }
            }

            if (touched > 0)
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"[TrafficCar] {added} carro(s) configurado(s), {restaggered} já tinha(m) o componente (largada reescalonada). " +
                      "Ajuste o Travel Distance e o Forward Axis de cada um no Inspector — o gizmo amarelo mostra o percurso.");
        }

        /// <summary>
        /// Preenche a lista de rodas do carro, se ela ainda estiver vazia. Devolve quantas
        /// rodas o componente tem ao final (as recém-achadas ou as que já estavam lá).
        /// </summary>
        private static int AssignWheels(GameObject car, SerializedObject serialized)
        {
            SerializedProperty wheels = serialized.FindProperty("wheels");

            if (wheels == null)
                return 0;

            if (wheels.arraySize > 0)
                return wheels.arraySize; // já amarrado na mão: não mexe

            List<Transform> found = FindWheels(car);
            wheels.arraySize = found.Count;

            for (int i = 0; i < found.Count; i++)
                wheels.GetArrayElementAtIndex(i).objectReferenceValue = found[i];

            return found.Count;
        }

        /// <summary>
        /// Acha os objetos VISUAIS das rodas dentro do carro, pelo nome. Três filtros evitam
        /// os enganos clássicos desses packs:
        /// - descarta quem tem <see cref="WheelCollider"/> (nesses prefabs a roda de FÍSICA
        ///   tem o MESMO nome da visual — girar essa não muda nada na tela);
        /// - exige um Renderer (sem malha não há o que girar);
        /// - descarta quem tem outra "roda" abaixo de si, que é o CONTAINER ("Wheels") — girar
        ///   o container arrastaria o grupo inteiro junto.
        /// </summary>
        private static List<Transform> FindWheels(GameObject car)
        {
            var found = new List<Transform>();

            foreach (Transform candidate in car.GetComponentsInChildren<Transform>(true))
            {
                if (candidate == car.transform || !IsWheelName(candidate.name))
                    continue;

                if (candidate.GetComponent<WheelCollider>() != null)
                    continue;

                if (candidate.GetComponentInChildren<Renderer>() == null)
                    continue;

                if (HasWheelDescendant(candidate))
                    continue;

                found.Add(candidate);
            }

            return found;
        }

        private static bool IsWheelName(string name)
        {
            string lower = name.ToLowerInvariant();

            foreach (string hint in WheelNameHints)
            {
                if (lower.Contains(hint))
                    return true;
            }

            return false;
        }

        private static bool HasWheelDescendant(Transform parent)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child != parent && IsWheelName(child.name))
                    return true;
            }

            return false;
        }
    }
}
