using System.Collections;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text;
using TheDelivery.Core;
using TheDelivery.Narrative;
using TheDelivery.Persistence;
using TheDelivery.Player;
using TheDelivery.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// ROTEIRO DE VÍDEO "balconycallvideo" (2026-11-23): evidência do chamado "Clear… Clear…" e do sting da sacada
    /// (Ato 3, Beat 6). Play pela Boot (marcador "200 Assets/Scenes/Boot.unity balconycallvideo"): menu → Ato 3 →
    /// Beat EatAndSleep → pega a comida → fica parada na sala ouvindo o chamado → anda pela NavMesh até a sacada →
    /// o entregador lá embaixo, câmera trava, sting. Faz backup do save/perfil e restaura no fim.
    /// </summary>
    [InitializeOnLoad]
    public static class ApartmentAudioDemo
    {
        private const float Duration = 75f;

        static ApartmentAudioDemo() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep != "balconycallvideo" && prep != "apartmentroomvideo" && prep != "apartmentdoorsvideo" && prep != "apartmentdoorbellvideo" && prep != "apartmentphenomenavideo" && prep != "act3momentsvideo" && prep != "act3itemsvideo" && prep != "act3foodphonevideo" && prep != "newdoorsvideo" && prep != "aptourvideo") return;
            string bakDir = Path.Combine(Application.temporaryCachePath, "save-backup-balcony");
            Directory.CreateDirectory(bakDir);
            string[] files = { SaveManager.SavePath, SaveManager.ProgressPath };
            foreach (string f in files)
            {
                string b = Path.Combine(bakDir, Path.GetFileName(f));
                if (File.Exists(f)) File.Copy(f, b, true); else if (File.Exists(b)) File.Delete(b);
            }
            void Restore()
            {
                if (EditorApplication.isPlaying) return;
                EditorApplication.update -= Restore;
                foreach (string f in files)
                {
                    string b = Path.Combine(bakDir, Path.GetFileName(f));
                    if (File.Exists(b)) File.Copy(b, f, true); else if (File.Exists(f)) File.Delete(f);
                }
                Debug.Log("[BalconyDemo] Save e perfil restaurados do backup.");
            }
            EditorApplication.update += Restore;

            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true;
            rec.StartCoroutine(prep == "apartmentroomvideo" ? RoomScript(rec) : prep == "apartmentdoorsvideo" ? DoorsScript(rec) : prep == "apartmentdoorbellvideo" ? DoorbellScript(rec) : prep == "apartmentphenomenavideo" ? PhenomenaScript(rec) : prep == "act3momentsvideo" ? MomentsScript(rec) : prep == "act3itemsvideo" ? ItemsScript(rec) : prep == "act3foodphonevideo" ? FoodPhoneScript(rec) : prep == "newdoorsvideo" ? NewDoorsScript(rec) : prep == "aptourvideo" ? TourScript(rec) : Script(rec));
        }

        private static IEnumerator Script(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            float t0 = 0f;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[BalconyDemo] {Time.time - t0:0.0}s {s}"); }

            yield return new WaitUntil(() => Object.FindFirstObjectByType<MainMenuController>() != null);
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.Act3, GameScene.Apartment);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Apartment" && Object.FindFirstObjectByType<Act3Director>() != null);
            yield return new WaitForSeconds(2f);

            var director = Object.FindFirstObjectByType<Act3Director>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            director.AdvanceToBeat(Act3Beat.EatAndSleep);
            yield return null;
            // a comida "pega" direto (o roteiro do vídeo é o chamado, não a bancada)
            director.GetType().GetMethod("OnFoodTaken", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(director, null);

            rec.Begin(Duration, 24, true);
            t0 = Time.time;
            Ev("Beat 6 (EatAndSleep): comida pega; o chamado começa");

            Transform balcony = GetPrivate<Transform>(director, "balconyPoint");
            Transform reveal = GetPrivate<Transform>(director, "deliverymanBalconyRevealZone");
            // começa no meio da sala, de costas para a sacada: só o som diz para onde ir
            var cc = player.GetComponent<CharacterController>();
            // sala, perto da TV (a sacada fica a ~6 m, na janela da parede oposta)
            Vector3 start = balcony != null ? new Vector3(1.0f, balcony.position.y, 2.6f) : player.transform.position;
            if (NavMesh.SamplePosition(start, out var sh, 3f, NavMesh.AllAreas)) start = sh.position;
            cc.enabled = false; player.transform.position = start; cc.enabled = true;
            if (balcony != null) player.transform.rotation = Quaternion.LookRotation(Flat(start - balcony.position), Vector3.up);

            // espera os pensamentos da comida e fica ouvindo o chamado
            yield return new WaitUntil(() => player.CanMove || Time.time - t0 > 20f);
            var call = GetPrivate<AudioSource>(director, "deliverymanCallingSource");
            Ev($"livre; ouvindo o chamado parada (fonte tocando={call?.isPlaying}, clip={call?.clip?.name}, a {(call != null ? Vector3.Distance(call.transform.position, player.transform.position) : -1):0.0} m)");
            yield return new WaitUntil(() => (call != null && call.isPlaying) || Time.time - t0 > 25f);
            Ev($"o chamado começou (tocando={call?.isPlaying}, a {(call != null ? Vector3.Distance(call.transform.position, player.transform.position) : -1):0.0} m); ela fica parada um ciclo inteiro");
            yield return new WaitForSeconds(10f);

            Gamepad pad = InputSystem.AddDevice<Gamepad>("BalconyDemoGamepad");
            if (balcony != null)
            {
                // vira para o som e anda pela NavMesh até a sacada
                var path = new NavMeshPath();
                Vector3 goal = reveal != null ? reveal.position : balcony.position; // a zona da revelação fica na NavMesh; a sacada em si não
                if (NavMesh.SamplePosition(goal, out var gh, 3f, NavMesh.AllAreas)) goal = gh.position;
                NavMesh.CalculatePath(player.transform.position, goal, NavMesh.AllAreas, path);
                Ev($"anda até a sacada ({path.corners.Length} cantos, {path.status})");
                foreach (var c in path.corners)
                    yield return WalkTo(pad, player, c, 0.5f, 10f);
                yield return WalkTo(pad, player, balcony.position, 0.6f, 6f);
                QueueStick(pad, Vector2.zero);
                Ev("na sacada");
            }
            yield return new WaitUntil(() => Time.time - t0 > Duration - 1f || player == null);
            InputSystem.RemoveDevice(pad);
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "apartmentroomvideo" (2026-11-24, etapa 1): room tone + chuva parada na sala, interruptor de parede
        /// (apaga/acende), o mesmo sem energia (clique morto), o abajur e a geladeira zumbindo de perto.
        /// Marcador: "200 Assets/Scenes/Boot.unity apartmentroomvideo".
        /// </summary>
        private static IEnumerator RoomScript(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            float t0 = 0f;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[RoomDemo] {Time.time - t0:0.0}s {s}"); }

            yield return new WaitUntil(() => Object.FindFirstObjectByType<MainMenuController>() != null);
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.Act3, GameScene.Apartment);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Apartment" && Object.FindFirstObjectByType<Act3Director>() != null);
            yield return new WaitForSeconds(2f);

            var director = Object.FindFirstObjectByType<Act3Director>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            director.AdvanceToBeat(Act3Beat.HungerAndOrder);
            yield return null;

            var cc = player.GetComponent<CharacterController>();
            Vector3 start = new Vector3(1.0f, player.transform.position.y, 2.6f);
            if (NavMesh.SamplePosition(start, out var sh, 3f, NavMesh.AllAreas)) start = sh.position;
            cc.enabled = false; player.transform.position = start; cc.enabled = true;

            T Nearest<T>(Vector3 p) where T : Component
            {
                T best = null; float bd = float.MaxValue;
                foreach (var c in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
                {
                    float d = Vector3.Distance(c.transform.position, p);
                    if (d < bd) { bd = d; best = c; }
                }
                return best;
            }
            var sw = Nearest<TheDelivery.Interaction.LightSwitch>(start);
            var lamp = Nearest<TheDelivery.Interaction.TableLamp>(start);
            var fridge = Nearest<TheDelivery.Interaction.FridgeDoor>(start);

            rec.Begin(60f, 24, true);
            t0 = Time.time;
            Ev($"Beat 3; sala, parada ouvindo o room tone e a chuva (interruptor {sw?.name}, abajur {lamp?.name}, geladeira {fridge?.name})");
            yield return new WaitUntil(() => player.CanMove || Time.time - t0 > 15f);
            yield return new WaitForSeconds(6f);

            Gamepad pad = InputSystem.AddDevice<Gamepad>("RoomDemoGamepad");
            IEnumerator GoLook(Component target, float stop)
            {
                Vector3 goal = target.transform.position;
                if (NavMesh.SamplePosition(goal, out var gh, 2.5f, NavMesh.AllAreas)) goal = gh.position;
                var path = new NavMeshPath();
                NavMesh.CalculatePath(player.transform.position, goal, NavMesh.AllAreas, path);
                foreach (var c in path.corners) yield return WalkTo(pad, player, c, Mathf.Max(0.5f, stop), 10f);
                QueueStick(pad, Vector2.zero);
                Vector3 to = Flat(target.transform.position - player.transform.position);
                float t1 = Time.time;
                while (Time.time - t1 < 0.8f)
                {
                    player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, Quaternion.LookRotation(to), 240f * Time.deltaTime);
                    float pitch = -Mathf.Atan2(target.transform.position.y - player.CameraHolder.position.y,
                        Vector3.ProjectOnPlane(target.transform.position - player.transform.position, Vector3.up).magnitude) * Mathf.Rad2Deg;
                    player.SyncCameraState(pitch, player.EyeHeight);
                    yield return null;
                }
            }

            if (sw != null)
            {
                Ev("anda até o interruptor"); yield return GoLook(sw, 0.9f);
                sw.Interact(null); Ev("interruptor: apaga"); yield return new WaitForSeconds(1.6f);
                sw.Interact(null); Ev("interruptor: acende"); yield return new WaitForSeconds(1.6f);
                sw.Powered = false;
                sw.Interact(null); Ev("SEM ENERGIA: clique morto"); yield return new WaitForSeconds(1.2f);
                sw.Interact(null); Ev("SEM ENERGIA: clique morto de novo"); yield return new WaitForSeconds(1.4f);
                sw.Powered = true; Ev("energia volta (só para o vídeo)");
                yield return new WaitForSeconds(1f);
            }
            if (lamp != null)
            {
                Ev("anda até o abajur"); yield return GoLook(lamp, 1.0f);
                lamp.Interact(null); Ev("abajur: alterna"); yield return new WaitForSeconds(1.6f);
                lamp.Interact(null); Ev("abajur: alterna de volta"); yield return new WaitForSeconds(1.4f);
            }
            if (fridge != null)
            {
                Ev("anda até a geladeira"); yield return GoLook(fridge, 1.0f);
                Ev($"parada na geladeira ({Vector3.Distance(fridge.transform.position, player.transform.position):0.0} m), ouvindo o zumbido");
            }
            yield return new WaitUntil(() => Time.time - t0 > 59f || player == null);
            InputSystem.RemoveDevice(pad);
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "apartmentdoorsvideo" (2026-11-24, etapa 2): geladeira (abre/fecha), porta do apê (abre, fecha, trancada)
        /// e as batidas na porta do vizinho (só o som: o roteiro do vizinho não dispara).
        /// Marcador: "200 Assets/Scenes/Boot.unity apartmentdoorsvideo".
        /// </summary>
        private static IEnumerator DoorsScript(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            float t0 = 0f;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[DoorsDemo] {Time.time - t0:0.0}s {s}"); }

            yield return new WaitUntil(() => Object.FindFirstObjectByType<MainMenuController>() != null);
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.Act3, GameScene.Apartment);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Apartment" && Object.FindFirstObjectByType<Act3Director>() != null);
            yield return new WaitForSeconds(2f);

            var director = Object.FindFirstObjectByType<Act3Director>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            director.AdvanceToBeat(Act3Beat.HungerAndOrder);
            yield return null;

            var cc = player.GetComponent<CharacterController>();
            var fridge = Object.FindFirstObjectByType<TheDelivery.Interaction.FridgeDoor>();
            TheDelivery.Interaction.Door aptDoor = null, neighbor = null;
            foreach (var d in Object.FindObjectsByType<TheDelivery.Interaction.Door>(FindObjectsSortMode.None))
            {
                bool canOpen = (bool)typeof(TheDelivery.Interaction.Door).GetField("playerCanOpen", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(d);
                if (canOpen) aptDoor = d; else neighbor = d;
            }
            void Teleport(Vector3 p, Vector3 look)
            {
                if (NavMesh.SamplePosition(p, out var h, 3f, NavMesh.AllAreas)) p = h.position;
                cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(Flat(look - p)); cc.enabled = true;
            }
            Vector3 Front(Component c, float dist, Vector3 from)
            {
                // o lado da folha mais perto de "from", a "dist" metros dela
                Vector3 n = c.transform.forward; n.y = 0f; n.Normalize();
                Vector3 a = c.transform.position + n * dist, b = c.transform.position - n * dist;
                return (a - from).sqrMagnitude < (b - from).sqrMagnitude ? a : b;
            }
            void Look(Component target)
            {
                Vector3 to = target.transform.position - player.CameraHolder.position;
                float pitch = -Mathf.Atan2(to.y, Vector3.ProjectOnPlane(to, Vector3.up).magnitude) * Mathf.Rad2Deg;
                player.transform.rotation = Quaternion.LookRotation(Flat(to));
                player.SyncCameraState(Mathf.Clamp(pitch + 8f, -30f, 40f), player.EyeHeight);
            }

            rec.Begin(48f, 24, true);
            t0 = Time.time;
            yield return new WaitUntil(() => player.CanMove || Time.time - t0 > 15f);

            if (fridge != null)
            {
                var b = new Bounds(fridge.transform.position, Vector3.zero);
                foreach (var r in fridge.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                Vector3 inside = new Vector3(1.0f, player.transform.position.y, 2.6f);
                Teleport(Vector3.MoveTowards(new Vector3(b.center.x, player.transform.position.y, b.center.z), inside, 1.7f), b.center);
                for (float t = 0; t < 1f; t += Time.deltaTime) { Look(fridge); yield return null; }
                fridge.Interact(null); Ev("geladeira: abre");
                for (float t = 0; t < 2.6f; t += Time.deltaTime) { Look(fridge); yield return null; }
                fridge.Interact(null); Ev("geladeira: fecha");
                for (float t = 0; t < 2.4f; t += Time.deltaTime) { Look(fridge); yield return null; }
            }
            if (aptDoor != null)
            {
                Teleport(Front(aptDoor, 1.6f, new Vector3(1.0f, 0f, 2.6f)), aptDoor.transform.position);
                for (float t = 0; t < 1f; t += Time.deltaTime) { Look(aptDoor); yield return null; }
                bool wasLocked = aptDoor.IsLocked; aptDoor.IsLocked = false;
                if (!aptDoor.IsClosed) { aptDoor.Close(); yield return new WaitForSeconds(1.5f); }
                aptDoor.Interact(null); Ev("porta do apê: abre");
                for (float t = 0; t < 2.8f; t += Time.deltaTime) { Look(aptDoor); yield return null; }
                aptDoor.Interact(null); Ev("porta do apê: fecha");
                for (float t = 0; t < 2.6f; t += Time.deltaTime) { Look(aptDoor); yield return null; }
                aptDoor.IsLocked = true;
                aptDoor.Interact(null); Ev("porta do apê: TRANCADA");
                for (float t = 0; t < 1.6f; t += Time.deltaTime) { Look(aptDoor); yield return null; }
                aptDoor.Interact(null); Ev("porta do apê: TRANCADA de novo");
                for (float t = 0; t < 2f; t += Time.deltaTime) { Look(aptDoor); yield return null; }
                aptDoor.IsLocked = wasLocked;
            }
            if (neighbor != null)
            {
                Teleport(Front(neighbor, 1.3f, aptDoor != null ? aptDoor.transform.position : neighbor.transform.position), neighbor.transform.position);
                for (float t = 0; t < 1.2f; t += Time.deltaTime) { Look(neighbor); yield return null; }
                var play = typeof(TheDelivery.Interaction.Door).GetMethod("PlaySound", BindingFlags.NonPublic | BindingFlags.Instance);
                var knock = typeof(TheDelivery.Interaction.Door).GetField("knockClip", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(neighbor);
                play.Invoke(neighbor, new[] { knock }); Ev("bate na porta do vizinho");
                for (float t = 0; t < 2.6f; t += Time.deltaTime) { Look(neighbor); yield return null; }
                play.Invoke(neighbor, new[] { knock }); Ev("bate de novo");
                for (float t = 0; t < 2.5f; t += Time.deltaTime) { Look(neighbor); yield return null; }
            }
            Ev("FIM");
            yield return new WaitUntil(() => Time.time - t0 > 47f || player == null);
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "apartmentdoorbellvideo" (2026-11-24, etapa 3): a campainha da entrega (Beat 4, o entregador chega à porta)
        /// e a campainha insistente do fenômeno #1 (Beat 5), que só para quando a Clear abre a porta.
        /// Marcador: "200 Assets/Scenes/Boot.unity apartmentdoorbellvideo".
        /// </summary>
        private static IEnumerator DoorbellScript(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            float t0 = 0f;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[DoorbellDemo] {Time.time - t0:0.0}s {s}"); }

            yield return new WaitUntil(() => Object.FindFirstObjectByType<MainMenuController>() != null);
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.Act3, GameScene.Apartment);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Apartment" && Object.FindFirstObjectByType<Act3Director>() != null);
            yield return new WaitForSeconds(2f);

            var director = Object.FindFirstObjectByType<Act3Director>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            var cc = player.GetComponent<CharacterController>();
            var src = GetPrivate<AudioSource>(director, "audioSource");
            var door = GetPrivate<TheDelivery.Interaction.Door>(director, "apartmentDoor");
            void PlaceFacingDoor()
            {
                if (door == null) return;
                Vector3 inside = new Vector3(1.0f, player.transform.position.y, 2.6f);
                Vector3 p = Vector3.MoveTowards(new Vector3(door.transform.position.x, inside.y, door.transform.position.z), inside, 2.6f);
                if (NavMesh.SamplePosition(p, out var h, 2f, NavMesh.AllAreas)) p = h.position;
                cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(Flat(door.transform.position - p)); cc.enabled = true;
                player.SyncCameraState(4f, player.EyeHeight);
            }

            director.AdvanceToBeat(Act3Beat.Delivery);
            yield return null;
            PlaceFacingDoor();
            rec.Begin(80f, 24, true);
            t0 = Time.time;
            Ev("Beat 4 (Delivery): o entregador vem pelo corredor");
            yield return new WaitUntil(() => (src != null && src.isPlaying) || Time.time - t0 > 45f);
            Ev($"CAMPAINHA da entrega (tocando={src?.isPlaying})");
            yield return new WaitForSeconds(5f);

            typeof(Act3Director).GetField("explorationDelay", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(director, 1f);
            director.AdvanceToBeat(Act3Beat.Phenomena);
            yield return null;
            if (door != null) { door.IsLocked = false; if (!door.IsClosed) door.Close(); }
            PlaceFacingDoor();
            Ev("Beat 5 (Phenomena): porta fechada, esperando a campainha insistente");
            yield return new WaitUntil(() => (src != null && src.isPlaying && src.loop) || Time.time - t0 > 70f);
            Ev($"CAMPAINHA INSISTENTE (loop={src?.loop}, clip={src?.clip?.name})");
            yield return new WaitForSeconds(12f);
            if (door != null) { door.Interact(null); Ev("a Clear abre a porta: a campainha para"); }
            yield return new WaitForSeconds(1.5f);
            Ev($"depois de abrir: tocando={src?.isPlaying}, loop={src?.loop}");
            yield return new WaitUntil(() => Time.time - t0 > 79f || player == null);
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "apartmentphenomenavideo" (2026-11-24, etapa 4): o Beat 5 inteiro com as pausas encurtadas — campainha
        /// insistente, batidas, telefone fixo + ligação muda, luz piscando e o vizinho com a corrente.
        /// Marcador: "200 Assets/Scenes/Boot.unity apartmentphenomenavideo".
        /// </summary>
        private static IEnumerator PhenomenaScript(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            float t0 = 0f;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[PhenomenaDemo] {Time.time - t0:0.0}s {s}"); }

            yield return new WaitUntil(() => Object.FindFirstObjectByType<MainMenuController>() != null);
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.Act3, GameScene.Apartment);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Apartment" && Object.FindFirstObjectByType<Act3Director>() != null);
            yield return new WaitForSeconds(2f);

            var director = Object.FindFirstObjectByType<Act3Director>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            var cc = player.GetComponent<CharacterController>();
            var src = GetPrivate<AudioSource>(director, "audioSource");
            var door = GetPrivate<TheDelivery.Interaction.Door>(director, "apartmentDoor");
            var nDoor = GetPrivate<TheDelivery.Interaction.Door>(director, "neighborDoor");
            var nZone = GetPrivate<Transform>(director, "neighborDoorZone");
            var phoneObj = GetPrivate<GameObject>(director, "ringingPhoneObject");
            const BindingFlags I = BindingFlags.NonPublic | BindingFlags.Instance;
            void SetF(string f, object v) => typeof(Act3Director).GetField(f, I)?.SetValue(director, v);
            SetF("explorationDelay", 1f); SetF("phenomenaGapMin", 2.5f); SetF("phenomenaGapMax", 3.5f);
            void Put(Vector3 p, Vector3 look, float pitch = 4f)
            {
                if (NavMesh.SamplePosition(p, out var h, 2.5f, NavMesh.AllAreas)) p = h.position;
                cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(Flat(look - p)); cc.enabled = true;
                player.SyncCameraState(pitch, player.EyeHeight);
            }
            Vector3 inside = new Vector3(1.0f, player.transform.position.y, 2.6f);
            void FaceDoor()
            {
                if (door == null) return;
                Put(Vector3.MoveTowards(new Vector3(door.transform.position.x, inside.y, door.transform.position.z), inside, 2.6f), door.transform.position);
            }

            director.AdvanceToBeat(Act3Beat.Phenomena);
            yield return null;
            if (door != null) { door.IsLocked = false; if (!door.IsClosed) door.Close(); }
            FaceDoor();
            rec.Begin(120f, 24, true);
            t0 = Time.time;
            Ev("Beat 5 (Phenomena), pausas encurtadas");

            yield return new WaitUntil(() => (src != null && src.isPlaying && src.loop) || Time.time - t0 > 20f);
            Ev("1. campainha insistente"); yield return new WaitForSeconds(6f);
            door?.Interact(null); Ev("abre a porta: ninguém");
            yield return new WaitForSeconds(4f);
            yield return new WaitUntil(() => player.CanMove || Time.time - t0 > 40f);
            if (door != null && door.IsOpen) { door.Interact(null); Ev("fecha a porta"); }
            yield return new WaitForSeconds(1.2f);
            FaceDoor();
            yield return new WaitUntil(() => (src != null && src.isPlaying && !src.loop) || Time.time - t0 > 45f);
            Ev("2. batidas na porta"); yield return new WaitForSeconds(9f);

            if (phoneObj != null) Put(Vector3.MoveTowards(new Vector3(phoneObj.transform.position.x, inside.y, phoneObj.transform.position.z), inside, 1.4f), phoneObj.transform.position, 25f);
            yield return new WaitUntil(() => (src != null && src.isPlaying && src.loop) || Time.time - t0 > 65f);
            Ev($"3. telefone tocando ({src?.clip?.name})"); yield return new WaitForSeconds(6f);
            typeof(Act3Director).GetMethod("OnPhoneAnswered", I)?.Invoke(director, null);
            Ev("atende: do outro lado, só respiração");
            yield return new WaitForSeconds(2f);
            yield return new WaitUntil(() => player.CanMove || Time.time - t0 > 85f);
            Ev("solta o telefone; vem a luz");
            Put(inside, door != null ? door.transform.position : inside + Vector3.forward, 0f);
            yield return new WaitForSeconds(6f);
            Ev("4. luz piscou (log por tempo)");

            if (nZone != null)
            {
                yield return new WaitForSeconds(4f);
                Put(nZone.position, nDoor != null ? nDoor.transform.position : nZone.position + Vector3.forward, 2f);
                Ev("5. na porta do vizinho"); yield return new WaitForSeconds(2.5f);
                // bate até ele vir (a primeira batida pode cair antes de o roteiro começar a escutar)
                while (nDoor != null && nDoor.IsClosed && Time.time - t0 < 100f)
                {
                    nDoor.Interact(null); Ev("bate na porta do vizinho");
                    float w = Time.time; yield return new WaitUntil(() => !nDoor.IsClosed || Time.time - w > 3.5f);
                }
                yield return new WaitUntil(() => (nDoor != null && nDoor.IsOpen) || Time.time - t0 > 110f);
                Ev("o vizinho abre (corrente)");
                yield return new WaitUntil(() => (nDoor != null && nDoor.IsClosed) || Time.time - t0 > 118f);
                Ev("o vizinho fecha a porta");
            }
            yield return new WaitUntil(() => Time.time - t0 > 119f || player == null);
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "act3momentsvideo" (2026-11-24, etapa 6): os momentos do Ato 3 que ganharam som — chegada do elevador e
        /// corredor (Beat 1) com o sting do "boa noite", o drone do "eu sei onde você mora" (Beat 4), o resmungo do
        /// vizinho (Beat 5) e a rua na sacada (Beat 6). Marcador: "480 Assets/Scenes/Boot.unity act3momentsvideo".
        /// </summary>
        private static IEnumerator MomentsScript(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            float t0 = 0f;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[Act3MomentsDemo] {Time.time - t0:0.0}s {s}"); }

            yield return new WaitUntil(() => Object.FindFirstObjectByType<MainMenuController>() != null);
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.Act3, GameScene.Apartment);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Apartment" && Object.FindFirstObjectByType<Act3Director>() != null);
            yield return new WaitForSeconds(2f);

            var director = Object.FindFirstObjectByType<Act3Director>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            var cc = player.GetComponent<CharacterController>();
            var src = GetPrivate<AudioSource>(director, "audioSource");
            var door = GetPrivate<TheDelivery.Interaction.Door>(director, "apartmentDoor");
            var nDoor = GetPrivate<TheDelivery.Interaction.Door>(director, "neighborDoor");
            var nZone = GetPrivate<Transform>(director, "neighborDoorZone");
            var cross = GetPrivate<Transform>(director, "deliverymanCrossPoint");
            var balcony = GetPrivate<Transform>(director, "balconyPoint");
            var street = GetPrivate<Transform>(director, "deliverymanBalconyPoint");
            const BindingFlags I = BindingFlags.NonPublic | BindingFlags.Instance;
            void Put(Vector3 p, Vector3 look, float pitch = 4f)
            {
                if (NavMesh.SamplePosition(p, out var h, 2.5f, NavMesh.AllAreas)) p = h.position;
                cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(Flat(look - p)); cc.enabled = true;
                player.SyncCameraState(pitch, player.EyeHeight);
            }
            Gamepad pad = InputSystem.AddDevice<Gamepad>("Act3MomentsGamepad");

            // BEAT 1: sai do elevador (sininho + portas), corredor com a luminária, anda até o encontro
            rec.Begin(150f, 24, true);
            t0 = Time.time;
            director.AdvanceToBeat(Act3Beat.CorridorMeeting);
            yield return null;
            Ev("Beat 1: chegada no andar (sininho e portas do elevador)");
            yield return new WaitForSeconds(3f);
            if (cross != null)
            {
                var path = new NavMeshPath();
                Vector3 goal = cross.position;
                if (NavMesh.SamplePosition(goal, out var gh, 3f, NavMesh.AllAreas)) goal = gh.position;
                NavMesh.CalculatePath(player.transform.position, goal, NavMesh.AllAreas, path);
                Ev($"anda pelo corredor até o encontro ({path.corners.Length} cantos)");
                foreach (var c in path.corners) yield return WalkTo(pad, player, c, 0.5f, 10f);
                QueueStick(pad, Vector2.zero);
            }
            yield return new WaitUntil(() => !player.CanMove || Time.time - t0 > 30f);
            Ev("o entregador aparece: câmera trava; \"Boa noite\" ... sting em \"A noite é longa\"");
            yield return new WaitUntil(() => (player.CanMove && Time.time - t0 > 12f) || Time.time - t0 > 55f);
            Ev("o entregador some no corredor");
            yield return new WaitForSeconds(2f);

            // BEAT 4: a entrega; o drone entra em "Eu sei onde você mora agora"
            director.AdvanceToBeat(Act3Beat.Delivery);
            yield return null;
            if (door != null)
            {
                door.IsLocked = false; if (!door.IsClosed) door.SetOpenInstant(false);
                Vector3 inside = new Vector3(1.0f, player.transform.position.y, 2.6f);
                Put(Vector3.MoveTowards(new Vector3(door.transform.position.x, inside.y, door.transform.position.z), inside, 2.0f), door.transform.position);
            }
            float tb = Time.time;
            Ev("Beat 4: esperando a campainha da entrega");
            yield return new WaitUntil(() => (src != null && src.isPlaying) || Time.time - tb > 40f);
            yield return new WaitForSeconds(1.5f);
            if (door != null && door.IsClosed) { door.Interact(null); Ev("abre a porta para o entregador"); }
            yield return new WaitUntil(() => player.CanMove == false || Time.time - tb > 50f);
            Ev("diálogo da entrega (drone em \"Eu sei onde você mora agora\")");
            yield return new WaitUntil(() => player.CanMove || Time.time - tb > 75f);
            Ev("pegou a comida");
            yield return new WaitForSeconds(2f);

            // BEAT 5 (só o vizinho): bate até ele abrir; o resmungo vem com a porta abrindo
            if (nZone != null && nDoor != null)
            {
                SetupNeighbor();
                void SetupNeighbor()
                {
                    director.AdvanceToBeat(Act3Beat.EatAndSleep);   // tira o Beat 4 do caminho sem disparar a escalada
                }
                yield return null;
                director.StopAllCoroutines();
                director.StartCoroutine((IEnumerator)typeof(Act3Director).GetMethod("NeighborEncounter", I).Invoke(director, null));
                Put(nZone.position, nDoor.transform.position, 2f);
                float tn = Time.time;
                Ev("Beat 5: na porta do vizinho");
                yield return new WaitForSeconds(1.5f);
                while (nDoor.IsClosed && Time.time - tn < 20f)
                {
                    nDoor.Interact(null); Ev("bate na porta do vizinho");
                    float w = Time.time; yield return new WaitUntil(() => !nDoor.IsClosed || Time.time - w > 3.5f);
                }
                yield return new WaitUntil(() => nDoor.IsOpen || Time.time - tn > 25f);
                Ev("o vizinho abre: resmungo");
                yield return new WaitUntil(() => nDoor.IsClosed || Time.time - tn > 40f);
                Ev("o vizinho fecha");
                yield return new WaitForSeconds(1.5f);
            }

            // BEAT 6: a sacada — a cidade e o vento lá fora
            if (balcony != null)
            {
                director.StopAllCoroutines();
                Vector3 look = street != null ? street.position : balcony.position + balcony.forward * 5f;
                Put(balcony.position, look, 8f);
                Ev("Beat 6: na sacada (cidade e vento)");
                yield return new WaitForSeconds(10f);
            }
            InputSystem.RemoveDevice(pad);
            yield return new WaitUntil(() => Time.time - t0 > 149f || player == null);
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "act3itemsvideo" (2026-11-24, etapa 7): chaves na porta (Beat 2), toques no celular (Beat 3), saco da
        /// comida (Beat 4 e 6), mordidas no sofá e lençol na cama (Beat 6), seguindo o fluxo real do Act3Director.
        /// Marcador: "700 Assets/Scenes/Boot.unity act3itemsvideo".
        /// </summary>
        private static IEnumerator ItemsScript(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            float t0 = 0f;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[Act3ItemsDemo] {Time.time - t0:0.0}s {s}"); }

            yield return new WaitUntil(() => Object.FindFirstObjectByType<MainMenuController>() != null);
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.Act3, GameScene.Apartment);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Apartment" && Object.FindFirstObjectByType<Act3Director>() != null);
            yield return new WaitForSeconds(2f);

            var director = Object.FindFirstObjectByType<Act3Director>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            var cc = player.GetComponent<CharacterController>();
            var src = GetPrivate<AudioSource>(director, "audioSource");
            var door = GetPrivate<TheDelivery.Interaction.Door>(director, "apartmentDoor");
            var pi = GetPrivate<TheDelivery.Interaction.PlayerInteraction>(director, "playerInteraction");
            var fridge = GetPrivate<Transform>(director, "fridgeZone");
            var phone = GetPrivate<GameObject>(director, "phoneObject");
            var counter = GetPrivate<Component>(director, "counterDropPoint");
            var revealZone = GetPrivate<Transform>(director, "deliverymanBalconyRevealZone");
            var balcony = GetPrivate<Transform>(director, "balconyPoint");
            var sofa = GetPrivate<TheDelivery.Interaction.SittableChair>(director, "sofaChair");
            var tv = GetPrivate<TheDelivery.FX.TVScreen>(director, "livingRoomTV");
            var bed = GetPrivate<Transform>(director, "bedPoint");
            const BindingFlags I = BindingFlags.NonPublic | BindingFlags.Instance;
            void SetF(string f, object v) => typeof(Act3Director).GetField(f, I)?.SetValue(director, v);
            object GetF(string f) => typeof(Act3Director).GetField(f, I)?.GetValue(director);
            void Call(string m) => typeof(Act3Director).GetMethod(m, I)?.Invoke(director, null);
            void Put(Vector3 p, Vector3 look, float pitch = 4f)
            {
                if (NavMesh.SamplePosition(p, out var h, 2.5f, NavMesh.AllAreas)) p = h.position;
                cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(Flat(look - p)); cc.enabled = true;
                player.SyncCameraState(pitch, player.EyeHeight);
            }
            SetF("waitForDeliveryDelay", 2f);
            Gamepad pad = InputSystem.AddDevice<Gamepad>("Act3ItemsGamepad");
            Vector3 inside = new Vector3(1.0f, player.transform.position.y, 2.6f);

            // BEAT 2: chega à porta fechada -> chaves e trinco; abre e entra
            if (door != null) { door.IsLocked = false; if (!door.IsClosed) door.SetOpenInstant(false); }
            Vector3 doorP = door != null ? new Vector3(door.transform.position.x, inside.y, door.transform.position.z) : inside;
            Vector3 outside = doorP + Flat(doorP - inside) * 3.5f;
            Put(outside, doorP);
            rec.Begin(200f, 24, true);
            t0 = Time.time;
            director.AdvanceToBeat(Act3Beat.EnterApartment);
            yield return null;
            Ev("Beat 2: no corredor, indo até a porta do apê");
            yield return WalkTo(pad, player, doorP, 1.3f, 8f);
            QueueStick(pad, Vector2.zero);
            Ev("na porta: chaves e trinco");
            yield return new WaitForSeconds(4.5f);
            door?.Interact(null); Ev("abre a porta");
            yield return new WaitForSeconds(1.2f);
            yield return WalkTo(pad, player, inside, 0.6f, 8f);
            QueueStick(pad, Vector2.zero);

            // BEAT 3: geladeira vazia e o pedido no celular
            yield return new WaitUntil(() => director.CurrentBeat == Act3Beat.HungerAndOrder || Time.time - t0 > 30f);
            if (director.CurrentBeat != Act3Beat.HungerAndOrder) director.AdvanceToBeat(Act3Beat.HungerAndOrder);
            if (fridge != null) Put(fridge.position, fridge.position + Vector3.forward);
            Ev("Beat 3: na geladeira");
            yield return new WaitUntil(() => GetF("activeOrderPhone") != null || Time.time - t0 > 70f);
            if (phone != null) Put(Vector3.MoveTowards(new Vector3(phone.transform.position.x, inside.y, phone.transform.position.z), inside, 1.2f), phone.transform.position, 30f);
            yield return new WaitForSeconds(1f);
            Call("OnPhoneOrdered"); Ev("pega o celular e pede: toques na tela");
            yield return new WaitForSeconds(5f);

            // BEAT 4: entrega -> saco de papel ao receber e ao deixar na bancada
            yield return new WaitUntil(() => director.CurrentBeat == Act3Beat.Delivery || Time.time - t0 > 85f);
            if (door != null) Put(Vector3.MoveTowards(doorP, inside, 2.0f), doorP);
            Ev("Beat 4: esperando o entregador");
            float tb = Time.time;
            yield return new WaitUntil(() => (src != null && src.isPlaying) || Time.time - tb > 40f);
            yield return new WaitForSeconds(1.5f);
            if (door != null && door.IsClosed) { door.Interact(null); Ev("abre para o entregador"); }
            yield return new WaitUntil(() => !player.CanMove || Time.time - tb > 50f);
            yield return new WaitUntil(() => player.CanMove || Time.time - tb > 75f);
            Ev("recebe a comida: saco de papel");
            yield return new WaitUntil(() => GetF("activeDropPoint") != null || Time.time - tb > 90f);
            if (counter != null) Put(Vector3.MoveTowards(new Vector3(counter.transform.position.x, inside.y, counter.transform.position.z), inside, 1.0f), counter.transform.position, 30f);
            yield return new WaitForSeconds(1f);
            Call("OnFoodPlaced"); Ev("deixa na bancada: saco de papel");
            yield return new WaitForSeconds(4f);

            // BEAT 6: pega de volta (saco), sacada, sofá (mordidas), cama (lençol)
            director.AdvanceToBeat(Act3Beat.EatAndSleep);
            yield return null;
            yield return new WaitUntil(() => GetF("activeDropPoint") != null || Time.time - t0 > 130f);
            yield return new WaitForSeconds(0.5f);
            Call("OnFoodTaken"); Ev("Beat 6: pega a comida na bancada: saco de papel");
            yield return new WaitForSeconds(3f);
            yield return new WaitUntil(() => player.CanMove || Time.time - t0 > 140f);
            var dman = GetPrivate<GameObject>(director, "deliveryman");
            if (revealZone != null) { Put(revealZone.position, balcony != null ? balcony.position : revealZone.position + Vector3.forward); yield return new WaitUntil(() => (dman != null && dman.activeInHierarchy) || Time.time - t0 > 105f); Ev("entregador revelado lá embaixo"); }
            if (balcony != null) { cc.enabled = false; player.transform.position = new Vector3(balcony.position.x, player.transform.position.y, balcony.position.z); cc.enabled = true; player.SyncCameraState(8f, player.EyeHeight); }
            yield return new WaitUntil(() => !player.CanMove || Time.time - t0 > 115f);   // a câmera trava no entregador lá embaixo
            Ev("sacada: o entregador encarando");

            if (sofa != null)
            {
                yield return new WaitUntil(() => player.CanMove || Time.time - t0 > 160f);
                Put(Vector3.MoveTowards(new Vector3(sofa.transform.position.x, inside.y, sofa.transform.position.z), inside, 1.0f), sofa.transform.position);
                yield return new WaitForSeconds(2.5f);
                yield return new WaitUntil(() => sofa.CanInteract || Time.time - t0 > 165f);
                sofa.Interact(pi); Ev("senta no sofá");
                yield return new WaitUntil(() => sofa.IsSeated || Time.time - t0 > 168f);
                // come pela MESMA rotina do jogo (EatFoodAnimation: 3 subidas à boca, uma mordida em cada)
                yield return new WaitForSeconds(7f);   // o pensamento "Pressione [F] para comer"
                Ev("come: mordidas (rotina EatFoodAnimation do Act3Director)");
                yield return (IEnumerator)typeof(Act3Director).GetMethod("EatFoodAnimation", I).Invoke(director, null);
                yield return new WaitForSeconds(2f);
                tv?.TurnOff();
                director.StopAllCoroutines();
                if (sofa.IsSeated) { sofa.StandUp(); yield return new WaitUntil(() => !sofa.IsSeated || Time.time - t0 > 190f); }
                if (bed != null)
                {
                    cc.enabled = false; player.transform.position = new Vector3(bed.position.x, player.transform.position.y, bed.position.z); cc.enabled = true;
                    player.SyncCameraState(20f, player.EyeHeight);
                    yield return new WaitForSeconds(1.5f);
                    var sheets = GetPrivate<AudioClip>(director, "bedSheetsSound");
                    if (src != null && sheets != null) src.PlayOneShot(sheets);
                    Ev($"na cama: lençol ({sheets?.name})");
                    yield return new WaitForSeconds(6f);
                }
            }
            yield return new WaitUntil(() => player.CanMove || Time.time - t0 > 192f);
            InputSystem.RemoveDevice(pad);
            yield return new WaitUntil(() => Time.time - t0 > 199f || player == null);
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "act3foodphonevideo" (2026-11-24): o pedido no celular (cutscene da tela inteira), o lanche desembrulhado
        /// no papel da bancada, a Clear pegando o hambúrguer e comendo (inteiro -> 1ª -> 2ª -> 3ª mordida).
        /// Marcador: "520 Assets/Scenes/Boot.unity act3foodphonevideo".
        /// </summary>
        private static IEnumerator FoodPhoneScript(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            float t0 = 0f;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[Act3FoodPhoneDemo] {Time.time - t0:0.0}s {s}"); }

            yield return new WaitUntil(() => Object.FindFirstObjectByType<MainMenuController>() != null);
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.Act3, GameScene.Apartment);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Apartment" && Object.FindFirstObjectByType<Act3Director>() != null);
            yield return new WaitForSeconds(2f);

            var director = Object.FindFirstObjectByType<Act3Director>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            var cc = player.GetComponent<CharacterController>();
            var pi = GetPrivate<TheDelivery.Interaction.PlayerInteraction>(director, "playerInteraction");
            var fridge = GetPrivate<Transform>(director, "fridgeZone");
            var phone = GetPrivate<GameObject>(director, "phoneObject");
            var overlay = GetPrivate<GameObject>(director, "orderPhoneOverlay");
            var counter = GetPrivate<Component>(director, "counterDropPoint");
            var sofa = GetPrivate<TheDelivery.Interaction.SittableChair>(director, "sofaChair");
            const BindingFlags I = BindingFlags.NonPublic | BindingFlags.Instance;
            object GetF(string f) => typeof(Act3Director).GetField(f, I)?.GetValue(director);
            void Call(string m) => typeof(Act3Director).GetMethod(m, I)?.Invoke(director, null);
            IEnumerator Routine(string m) => (IEnumerator)typeof(Act3Director).GetMethod(m, I).Invoke(director, null);
            void CounterInteract() { if (counter is TheDelivery.Items.CounterDropPoint cdp && pi != null) cdp.Interact(pi); }
            void Put(Vector3 p, Vector3 look, float pitch = 4f)
            {
                if (NavMesh.SamplePosition(p, out var h, 2.5f, NavMesh.AllAreas)) p = h.position;
                cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(Flat(look - p)); cc.enabled = true;
                player.SyncCameraState(pitch, player.EyeHeight);
            }
            Vector3 inside = new Vector3(1.0f, player.transform.position.y, 2.6f);

            // BEAT 3: geladeira vazia -> celular -> a cutscene do pedido
            director.AdvanceToBeat(Act3Beat.HungerAndOrder);
            yield return null;
            if (fridge != null) Put(fridge.position, fridge.position + Vector3.forward);
            rec.Begin(120f, 24, true);
            t0 = Time.time;
            Ev("Beat 3: fome; esperando o celular armar");
            yield return new WaitUntil(() => GetF("activeOrderPhone") != null || Time.time - t0 > 40f);
            if (phone != null) Put(Vector3.MoveTowards(new Vector3(phone.transform.position.x, inside.y, phone.transform.position.z), inside, 1.2f), phone.transform.position, 30f);
            yield return new WaitForSeconds(1f);
            Call("OnPhoneOrdered"); Ev("pega o celular: cutscene do pedido");
            yield return new WaitUntil(() => (overlay != null && overlay.activeInHierarchy) || Time.time - t0 > 45f);
            yield return new WaitUntil(() => (overlay != null && !overlay.activeInHierarchy) || Time.time - t0 > 75f);
            Ev("pedido pago; o celular fecha");
            yield return new WaitForSeconds(1f);

            // BEAT 4 (encurtado): a comida já entregue -> bancada -> desembrulhar
            director.StopAllCoroutines();
            Call("GiveDeliveryFood");
            director.StartCoroutine(Routine("PlaceFoodOnCounter"));
            if (counter != null) Put(Vector3.MoveTowards(new Vector3(counter.transform.position.x, inside.y, counter.transform.position.z), inside, 0.9f), counter.transform.position, 38f);
            yield return new WaitUntil(() => GetF("activeDropPoint") != null || Time.time - t0 > 85f);
            yield return new WaitForSeconds(0.5f);
            CounterInteract(); Ev("deixa a sacola na bancada");
            yield return new WaitForSeconds(0.5f);
            yield return new WaitUntil(() => GetF("activeDropPoint") != null || Time.time - t0 > 92f);
            yield return new WaitForSeconds(1f);
            CounterInteract(); Ev("desembrulha: o hambúrguer no papel");
            yield return new WaitUntil(() => player.CanMove || Time.time - t0 > 98f);
            yield return new WaitForSeconds(2.5f);

            // BEAT 6 (encurtado): pega o hambúrguer e come no sofá
            director.StopAllCoroutines();
            director.StartCoroutine(Routine("TakeFoodFromCounter"));
            yield return new WaitUntil(() => GetF("activeDropPoint") != null || Time.time - t0 > 100f);
            yield return new WaitForSeconds(0.5f);
            CounterInteract(); Ev("pega o hambúrguer: na mão");
            player.SyncCameraState(10f, player.EyeHeight);
            yield return new WaitForSeconds(3f);
            if (sofa != null)
            {
                Put(Vector3.MoveTowards(new Vector3(sofa.transform.position.x, inside.y, sofa.transform.position.z), inside, 1.0f), sofa.transform.position);
                yield return new WaitForSeconds(0.5f);
                if (sofa.CanInteract) sofa.Interact(pi);
                yield return new WaitUntil(() => sofa.IsSeated || Time.time - t0 > 110f);
                Ev("senta no sofá");
                yield return new WaitForSeconds(1f);
            }
            director.StopAllCoroutines();
            Ev("come: inteiro -> 1ª -> 2ª -> 3ª mordida");
            yield return Routine("EatFoodAnimation");
            Ev("comeu");
            yield return new WaitUntil(() => Time.time - t0 > 119f || player == null);
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "newdoorsvideo" (2026-11-24): as portas novas — a 302 da Clear vista do corredor (de longe, de perto, abrindo e
        /// fechando), as dos vizinhos (303 e 301), o vizinho abrindo com a corrente e a 302 vista de dentro.
        /// Marcador: "260 Assets/Scenes/Boot.unity newdoorsvideo".
        /// </summary>
        private static IEnumerator NewDoorsScript(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            float t0 = 0f;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[NewDoorsDemo] {Time.time - t0:0.0}s {s}"); }

            yield return new WaitUntil(() => Object.FindFirstObjectByType<MainMenuController>() != null);
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.Act3, GameScene.Apartment);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Apartment" && Object.FindFirstObjectByType<Act3Director>() != null);
            yield return new WaitForSeconds(2f);

            var director = Object.FindFirstObjectByType<Act3Director>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            var cc = player.GetComponent<CharacterController>();
            var door = GetPrivate<TheDelivery.Interaction.Door>(director, "apartmentDoor");
            var nDoor = GetPrivate<TheDelivery.Interaction.Door>(director, "neighborDoor");
            var nZone = GetPrivate<Transform>(director, "neighborDoorZone");
            var dman = GetPrivate<GameObject>(director, "deliveryman");
            var n02 = GameObject.Find("NeighborDoor_02");
            const BindingFlags I = BindingFlags.NonPublic | BindingFlags.Instance;
            director.StopAllCoroutines();
            if (dman != null) dman.SetActive(false);
            void Put(Vector3 p, Vector3 look, float pitch = 3f)
            {
                if (NavMesh.SamplePosition(p, out var h, 1.5f, NavMesh.AllAreas)) p = h.position;
                cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(Flat(look - p)); cc.enabled = true;
                player.SyncCameraState(pitch, player.EyeHeight);
            }
            Vector3 Center(Transform t) { var r = t.GetComponentInChildren<Renderer>(); return r != null ? r.bounds.center : t.position; }
            float y = player.transform.position.y;
            if (door != null) { door.IsLocked = false; if (!door.IsClosed) door.SetOpenInstant(false); }

            rec.Begin(60f, 24, true);
            t0 = Time.time;
            if (door != null)
            {
                Vector3 dc = Center(door.transform);
                Put(new Vector3(dc.x - 0.25f, y, dc.z - 2.3f), dc, 2f);
                Ev("302 (Clear) vista do corredor"); yield return new WaitForSeconds(3.5f);
                Put(new Vector3(dc.x, y, dc.z - 1.0f), dc + Vector3.up * 0.3f, -6f);
                Ev("de perto: número, olho mágico, maçaneta"); yield return new WaitForSeconds(3.5f);
                Put(new Vector3(dc.x - 0.2f, y, dc.z - 1.6f), dc, 4f);
                door.Interact(null); Ev("abre"); yield return new WaitForSeconds(3f);
                door.Interact(null); Ev("fecha"); yield return new WaitForSeconds(2.5f);
            }
            if (nDoor != null)
            {
                Vector3 nc = Center(nDoor.transform);
                Put(new Vector3(nc.x - 1.9f, y, nc.z), nc, 2f);
                Ev("303 (vizinho) vista do corredor"); yield return new WaitForSeconds(3f);
            }
            if (n02 != null)
            {
                Vector3 oc = Center(n02.transform);
                Put(new Vector3(oc.x + 1.9f, y, oc.z), oc, 2f);
                Ev("301 (outro vizinho)"); yield return new WaitForSeconds(3f);
            }
            if (nDoor != null && nZone != null)
            {
                director.StartCoroutine((IEnumerator)typeof(Act3Director).GetMethod("NeighborEncounter", I).Invoke(director, null));
                Put(nZone.position, nDoor.transform.position, 2f);
                float tn = Time.time;
                Ev("bate na 303");
                yield return new WaitForSeconds(1f);
                while (nDoor.IsClosed && Time.time - tn < 15f)
                {
                    nDoor.Interact(null);
                    float w = Time.time; yield return new WaitUntil(() => !nDoor.IsClosed || Time.time - w > 3.5f);
                }
                Ev("o vizinho abre: a corrente por dentro");
                yield return new WaitUntil(() => nDoor.IsClosed || Time.time - tn > 35f);
                Ev("o vizinho fecha");
                yield return new WaitForSeconds(1f);
            }
            director.StopAllCoroutines();
            if (door != null)
            {
                Vector3 dc = Center(door.transform);
                Put(new Vector3(dc.x + 0.3f, y, dc.z + 2.2f), dc, 3f);
                Ev("302 vista de dentro"); yield return new WaitForSeconds(3.5f);
            }
            yield return new WaitUntil(() => Time.time - t0 > 59f || player == null);
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        /// <summary>
        /// "aptourvideo" (2026-11-24, a noite do acabamento): passeio por pontos fixos do prédio — cozinha, banheiro,
        /// sala, sacada, rua vista da sacada, quarto, hall e corredor do prédio — para conferir pisos, revestimentos,
        /// janelas, sacada, móveis e decoração. Marcador: "300 Assets/Scenes/Boot.unity aptourvideo".
        /// </summary>
        private static IEnumerator TourScript(DemoFrameRecorder rec)
        {
            var log = new StringBuilder();
            float t0 = 0f;
            void Ev(string s) { log.AppendLine($"{Time.time - t0:0.0}  {s}"); Debug.Log($"[AptTour] {Time.time - t0:0.0}s {s}"); }
            yield return new WaitUntil(() => Object.FindFirstObjectByType<MainMenuController>() != null);
            yield return new WaitForSeconds(2f);
            GameManager.Instance.GoToAct(GameAct.Act3, GameScene.Apartment);
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Apartment" && Object.FindFirstObjectByType<Act3Director>() != null);
            yield return new WaitForSeconds(2f);
            var director = Object.FindFirstObjectByType<Act3Director>();
            var player = Object.FindFirstObjectByType<PlayerController>();
            var cc = player.GetComponent<CharacterController>();
            director.StopAllCoroutines();
            var dman = GetPrivate<GameObject>(director, "deliveryman"); if (dman != null) dman.SetActive(false);
            const float FY = 5.31f;
            void Put(float x, float z, float lx, float lz, float pitch)
            {
                var p = new Vector3(x, FY + 0.05f, z);
                cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(Flat(new Vector3(lx, p.y, lz) - p)); cc.enabled = true;
                player.SyncCameraState(pitch, player.EyeHeight);
            }
            var shots = new (string label, float x, float z, float lx, float lz, float pitch, float secs)[]
            {
                ("Cozinha: porcelanato, azulejo, fogão", -7.35f, 7.3f, -9.9f, 6.2f, 16f, 3f),
                ("Cozinha: mesa, cadeiras e moletom", -9.3f, 6.3f, -7.9f, 8.4f, 20f, 3.5f),
                ("Banheiro: azulejo metrô", 1.0f, 8.4f, 3.0f, 11.6f, 10f, 3f),
                ("Banheiro: janela maxim-ar", 2.2f, 9.7f, 3.35f, 10.6f, 6f, 2.5f),
                ("Sala: laminado e janela de correr", 1.2f, 2.2f, -2.5f, 2.4f, 4f, 3f),
                ("Sala: estante e quadro", -1.2f, 5.3f, 0.0f, 0.0f, 6f, 3.5f),
                ("Sacada: porta de correr aberta", 0.6f, 5.2f, -3.5f, 5.3f, 6f, 3f),
                ("Sacada: piso e guarda-corpo", -2.8f, 5.3f, -4.6f, 4.9f, 24f, 3f),
                ("Rua vista da sacada", -3.6f, 5.3f, -14f, 2.0f, 26f, 3.5f),
                ("Quarto: janela", -0.8f, 8.5f, -2.0f, 11.9f, 4f, 3f),
                ("Quarto: quadro", -1.6f, 9.0f, 0.0f, 11.0f, 2f, 2.5f),
                ("Hall: quadro", -3.6f, 7.5f, -5.2f, 6.0f, 2f, 2.5f),
                ("Corredor do prédio: 302 e capacho", 0.0f, -2.6f, 0.0f, 0.0f, 22f, 3f),
                ("Corredor: 303 e capacho", -1.4f, -2.5f, 2.4f, -2.5f, 12f, 3f),
                ("Corredor: 301, extintor", 1.5f, -1.2f, -2.4f, -4.0f, 8f, 3.5f),
                ("Corredor: quadro de luz e elevador", -0.4f, -4.6f, 1.0f, -7.0f, 4f, 3f),
            };
            rec.Begin(shots.Sum(s => s.secs) + 1f, 24, true);
            t0 = Time.time;
            foreach (var s in shots)
            {
                Put(s.x, s.z, s.lx, s.lz, s.pitch);
                Ev(s.label);
                yield return new WaitForSeconds(s.secs);
            }
            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

        private static IEnumerator WalkTo(Gamepad pad, PlayerController player, Vector3 target, float stop, float timeout)
        {
            float start = Time.time;
            while (Time.time - start < timeout && player != null)
            {
                Vector3 to = target - player.transform.position; to.y = 0f;
                if (to.magnitude <= stop) break;
                if (!player.CanMove) { QueueStick(pad, Vector2.zero); yield return null; continue; }
                Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
                player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, want, 200f * Time.deltaTime);
                float facing = Vector3.Dot(player.transform.forward, to.normalized);
                QueueStick(pad, new Vector2(0f, facing > 0.7f ? 0.8f : 0.15f));
                yield return null;
            }
            QueueStick(pad, Vector2.zero);
        }

        private static void QueueStick(Gamepad pad, Vector2 move)
        {
            if (pad != null && pad.added) InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = move });
        }

        private static T GetPrivate<T>(object owner, string field) where T : class =>
            owner.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner) as T;
    }
}
