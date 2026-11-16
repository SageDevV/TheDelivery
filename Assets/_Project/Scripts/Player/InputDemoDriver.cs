#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace TheDelivery.Player
{
    /// <summary>
    /// SÓ EDITOR. "Joga" por um roteiro de input usando um CONTROLE VIRTUAL do Input System —
    /// para gravar vídeos de evidência de gameplay sem ninguém no teclado. Controle, e não
    /// teclado, porque no Editor o teclado respeita o foco da Game view e o controle não.
    /// Mapeamento (InputSystem_Actions): analógico esquerdo = Move, apertar o analógico
    /// esquerdo = Sprint (equivale a W + Shift esquerdo), analógico direito = Look.
    /// </summary>
    public sealed class InputDemoDriver : MonoBehaviour
    {
        public struct Key
        {
            public float Time;
            public Vector2 Move;
            public bool Sprint;
            public Vector2 Look;
            public string Label;
        }

        private readonly List<Key> keys = new List<Key>();
        private Gamepad pad;
        private float t0 = -1f;

        /// <summary>Rótulo do trecho atual do roteiro (para legenda/log).</summary>
        public string CurrentLabel { get; private set; } = "";

        public InputDemoDriver At(float time, Vector2 move, bool sprint, string label, Vector2 look = default)
        {
            keys.Add(new Key { Time = time, Move = move, Sprint = sprint, Look = look, Label = label });
            keys.Sort((a, b) => a.Time.CompareTo(b.Time));
            return this;
        }

        public void Begin()
        {
            pad = InputSystem.AddDevice<Gamepad>("DemoGamepad");
            t0 = Time.time;
        }

        private void Update()
        {
            if (pad == null || t0 < 0f)
                return;

            float t = Time.time - t0;
            Key cur = default;
            foreach (Key k in keys)
                if (k.Time <= t) cur = k;

            if (cur.Label != CurrentLabel && !string.IsNullOrEmpty(cur.Label))
            {
                CurrentLabel = cur.Label;
                Debug.Log($"[InputDemoDriver] {t:0.0}s — {cur.Label}");
            }

            var state = new GamepadState { leftStick = cur.Move, rightStick = cur.Look };
            if (cur.Sprint)
                state = state.WithButton(GamepadButton.LeftStick);
            InputSystem.QueueStateEvent(pad, state);
        }

        private void OnDestroy()
        {
            if (pad != null)
                InputSystem.RemoveDevice(pad);
        }
    }
}
#endif
