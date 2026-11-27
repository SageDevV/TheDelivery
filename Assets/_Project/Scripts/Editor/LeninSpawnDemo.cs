using System.Collections;
using TheDelivery.Act5;
using TheDelivery.AI;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace TheDelivery.EditorTools
{
    // Run from Escape via GameScreenshotTool with prep "leninspawn".
    [InitializeOnLoad]
    public static class LeninSpawnDemo
    {
        static LeninSpawnDemo() => GameScreenshotTool.PlayReady += prep =>
        {
            if (prep != "leninspawn") return;
            var rec = new GameObject("LeninSpawnRecorder").AddComponent<DemoFrameRecorder>();
            rec.RenderActiveCamera = true;
            rec.RenderSize = new Vector2Int(1280, 720);
            rec.StartCoroutine(Run(rec));
        };

        private static IEnumerator Run(DemoFrameRecorder rec)
        {
            var director = Object.FindAnyObjectByType<Act5Director>();
            yield return new WaitUntil(() => director.CurrentBeat == Act5Beat.CellPuzzle);
            director.AdvanceToBeat(Act5Beat.Explore);
            yield return null;
            var player = Object.FindAnyObjectByType<PlayerController>();
            var enemy = Object.FindAnyObjectByType<AntagonistAI>(FindObjectsInactive.Include).gameObject;
            // Keep the camera operator alive while filming the approach and the separate gate reveal.
            enemy.GetComponent<KillerCapture>().enabled = false;
            var marker = GameObject.Find("LeninSpawn_EngineSE").transform;
            var station = GameObject.Find("Generator_2").GetComponent<GeneratorStation>();
            var cam = Camera.main;
            var serialized = new SerializedObject(director);
            var points = serialized.FindProperty("leninSpawnPoints");
            // Only the test instance is constrained to one candidate, as if the others were unavailable.
            points.arraySize = 1;
            points.GetArrayElementAtIndex(0).objectReferenceValue = marker;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            foreach (var id in new[] { "Generator_1", "Generator_3" })
            {
                var gen = GameObject.Find(id).GetComponent<GeneratorStation>();
                gen.Interact(null);
                yield return new WaitUntil(() => gen.Running);
                Debug.Assert(!enemy.activeSelf, "Lenin apareceu antes do terceiro gerador");
            }
            void Stand(Vector3 feet)
            {
                var controller = player.GetComponent<CharacterController>();
                controller.enabled = false;
                player.transform.position = feet;
                controller.enabled = true;
                player.CanMove = false;
            }
            void Look(Vector3 at)
            {
                var dir = at - cam.transform.position;
                var flat = new Vector3(dir.x, 0f, dir.z);
                player.transform.rotation = Quaternion.LookRotation(flat);
                float pitch = -Mathf.Atan2(dir.y, flat.magnitude) * Mathf.Rad2Deg;
                player.CameraHolder.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                player.SyncCameraState(pitch, player.EyeHeight);
            }
            IEnumerator Turn(Quaternion to, float duration)
            {
                var from = player.transform.rotation;
                for (float t = 0f; t < duration; t += Time.deltaTime)
                {
                    player.transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
                    yield return null;
                }
                player.transform.rotation = to;
            }

            Stand(new Vector3(11.5f, 0.06f, -6.8f));
            Look(station.transform.position + Vector3.up);
            rec.Begin(29f, 24, true);
            yield return new WaitForSeconds(4f);
            station.Interact(null);
            yield return new WaitForSeconds(0.5f);
            var direction = marker.position - player.transform.position;
            var toward = Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.z));
            yield return Turn(toward, 1.2f);
            Look(marker.position + Vector3.up);
            yield return new WaitUntil(() => station.Running);
            yield return new WaitForSeconds(2f);
            Debug.Assert(director.CurrentBeat == Act5Beat.Hunt && !enemy.activeSelf, "Spawn visível no terceiro gerador");

            // The center is beyond the side edge, but part of the body is still in frame.
            float halfAngle = Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect) * 0.5f;
            yield return Turn(Quaternion.AngleAxis(halfAngle + 0.5f, Vector3.up) * toward, 1.5f);
            yield return new WaitForSeconds(0.5f);
            Debug.Assert(!enemy.activeSelf, "Spawn parcial na borda da câmera");
            yield return Turn(Quaternion.AngleAxis(115f, Vector3.up) * toward, 1.6f);
            yield return new WaitForSeconds(0.4f);
            Debug.Assert(enemy.activeSelf && enemy.GetComponent<NavMeshAgent>().isOnNavMesh, "Spawn seguro não ocorreu");
            Debug.Assert(!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam),
                new Bounds(enemy.transform.position + Vector3.up, new Vector3(1.8f, 2.3f, 1.8f))));
            Debug.Log("[LeninSpawnDemo] G1/G3 sem Lenin; G2 foi o terceiro. Centro e borda bloqueados; spawn fora de quadro.");
            GameObject.Find("Generator_4").GetComponent<GeneratorStation>().Interact(null);
            yield return new WaitForSeconds(0.2f);
            GameObject.Find("Generator_5").GetComponent<GeneratorStation>().Interact(null);
            yield return Turn(toward, 0.8f);
            Look(enemy.transform.position + Vector3.up);
            yield return new WaitUntil(() => director.CurrentBeat == Act5Beat.Powered);

            // Enter the finale looking directly at its destination, as when backing out through the gate.
            var watch = GameObject.Find("LeninWatchPoint").transform;
            Stand(new Vector3(35f, 0.06f, -8f));
            Look(watch.position + Vector3.up);
            yield return new WaitUntil(() => director.CurrentBeat == Act5Beat.Exit);
            yield return new WaitForSeconds(3.5f);
            Debug.Assert(enemy.activeSelf && Vector3.Distance(enemy.transform.position, watch.position) < 0.1f,
                "Lenin não ficou no portão para a revelação final");
            Debug.Log("[LeninSpawnDemo] Final de costas: Lenin no portão para o giro da Clear.");
            yield return new WaitForSeconds(2f);
            rec.FinishEarly();
            yield return new WaitUntil(() => System.IO.File.Exists(DemoFrameRecorder.DoneMarker));
            EditorApplication.ExitPlaymode();
        }
    }
}
