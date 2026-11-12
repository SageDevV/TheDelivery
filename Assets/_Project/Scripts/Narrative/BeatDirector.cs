using System;
using System.Collections;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace TheDelivery.Narrative
{
    /// <summary>
    /// A MÁQUINA DE BEATS que todos os diretores de ato usam: guarda o beat atual, troca de
    /// beat cancelando a coroutine anterior e liga as teclas numéricas de debug.
    ///
    /// Antes cada diretor tinha a própria cópia disto (AdvanceToBeat, SwitchBeatRoutine,
    /// HandleDebugKeys) — seis versões que já tinham começado a divergir. Aqui fica a única;
    /// o diretor só diz QUAL coroutine roda em cada beat (<see cref="CreateBeatRoutine"/>) e,
    /// se precisar, o que limpar na troca (<see cref="OnBeatExit"/>).
    ///
    /// O enum de beats segue a convenção do projeto: o valor 0 é <c>None</c> e os beats
    /// vêm na ordem cronológica. As teclas 1-9 pulam para o 1º-9º beat nessa ordem.
    ///
    /// CADA BEAT GARANTE SOZINHO O ESTADO DE QUE PRECISA (player travado ou livre, mira,
    /// objetos ativos). É isso que torna o salto de debug seguro em vez de um jeito de
    /// deixar a cena num estado impossível: entrar direto num beat do meio tem que dar no
    /// mesmo que chegar nele pelo caminho natural.
    /// </summary>
    /// <typeparam name="TBeat">O enum de beats do ato.</typeparam>
    public abstract class BeatDirector<TBeat> : MonoBehaviour where TBeat : struct, Enum
    {
        // Beats na ordem do enum, sem o None (índice 0): a tecla N pula para o N-ésimo.
        private static readonly TBeat[] DebugBeats = BuildDebugBeats();

        private Coroutine beatRoutine;
        // A troca adiada (ver DeferBeatSwitch). Guardada para que dois pedidos no mesmo
        // quadro (dois saltos de debug, por exemplo) fiquem com o último.
        private Coroutine switchRoutine;

        /// <summary>Beat atual da sequência (<c>None</c> antes de o ato começar).</summary>
        public TBeat CurrentBeat { get; private set; }

        /// <summary>
        /// TROCA ADIADA (padrão): o pedido de troca espera um quadro antes de parar o beat
        /// atual. É o que deixa um beat chamar <see cref="AdvanceToBeat"/> no fim da própria
        /// coroutine sem se interromper no meio da linha — a coroutine que pediu a troca
        /// termina naturalmente e só então a próxima começa.
        ///
        /// Diretores que dependem de ver o <see cref="CurrentBeat"/> novo NO MESMO QUADRO
        /// (vigias no Update que não podem disparar duas vezes) desligam isto.
        /// </summary>
        protected virtual bool DeferBeatSwitch => true;

        /// <summary>A coroutine do beat, ou null se o beat não tem rotina.</summary>
        protected abstract IEnumerator CreateBeatRoutine(TBeat beat);

        /// <summary>
        /// Chamado na troca, DEPOIS de parar a coroutine do beat anterior e ANTES de
        /// <see cref="CurrentBeat"/> mudar — ou seja, aqui <see cref="CurrentBeat"/> ainda é
        /// o beat que está saindo e <paramref name="beat"/> é o que vai entrar. É onde o
        /// diretor desliga o que roda em paralelo ao beat (loops de som, foco de câmera,
        /// objetos armados); sem isso um salto de debug deixa esses restos vivos no beat
        /// seguinte.
        /// </summary>
        protected virtual void OnBeatExit(TBeat beat) { }

        /// <summary>
        /// Chamado quando o beat pedido não tem rotina. O beat anterior já foi cancelado,
        /// então o ato fica parado: é sempre uma chamada errada.
        /// </summary>
        protected virtual void OnMissingBeatRoutine(TBeat beat) =>
            Debug.LogWarning($"[{GetType().Name}] AdvanceToBeat chamado com beat sem rotina: {beat}", this);

        /// <summary>
        /// Troca para <paramref name="beat"/>: cancela a coroutine do beat atual e inicia a
        /// do novo. Ponto único de transição entre beats.
        /// </summary>
        public void AdvanceToBeat(TBeat beat)
        {
            if (!DeferBeatSwitch)
            {
                SwitchBeat(beat);
                return;
            }

            if (switchRoutine != null)
                StopCoroutine(switchRoutine);
            switchRoutine = StartCoroutine(DeferredSwitch(beat));
        }

        /// <summary>
        /// Roda <paramref name="routine"/> no lugar da coroutine do beat atual, para que a
        /// próxima troca de beat a cancele como cancelaria o próprio beat.
        /// </summary>
        protected void RunInBeatSlot(IEnumerator routine)
        {
            StopBeatRoutine();
            beatRoutine = StartCoroutine(routine);
        }

        /// <summary>
        /// Marca a coroutine do beat como encerrada. Usado no último beat do ato, que
        /// termina entregando o controle (troca de cena, handoff) em vez de avançar.
        /// </summary>
        protected void ReleaseBeatRoutine() => beatRoutine = null;

        private IEnumerator DeferredSwitch(TBeat beat)
        {
            yield return null;
            switchRoutine = null;
            SwitchBeat(beat);
        }

        private void SwitchBeat(TBeat beat)
        {
            StopBeatRoutine();
            OnBeatExit(beat);
            CurrentBeat = beat;

            IEnumerator routine = CreateBeatRoutine(beat);
            if (routine != null)
                beatRoutine = StartCoroutine(routine);
            else
                OnMissingBeatRoutine(beat);
        }

        private void StopBeatRoutine()
        {
            if (beatRoutine == null)
                return;

            StopCoroutine(beatRoutine);
            beatRoutine = null;
        }

        private static TBeat[] BuildDebugBeats()
        {
            var values = (TBeat[])Enum.GetValues(typeof(TBeat));
            var beats = new TBeat[Math.Max(0, values.Length - 1)];
            Array.Copy(values, 1, beats, 0, beats.Length);
            return beats;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Teclas 1-9 pulam direto para o beat correspondente, na ordem do enum. Só existe
        /// no Editor e em Development Build: no build de release as teclas somem na
        /// compilação, mesmo com o debugMode ligado na cena.
        /// </summary>
        protected void HandleDebugKeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return;

            int count = Mathf.Min(9, DebugBeats.Length);
            for (int i = 0; i < count; i++)
            {
                if (kb[Key.Digit1 + i].wasPressedThisFrame)
                {
                    AdvanceToBeat(DebugBeats[i]);
                    return;
                }
            }
        }
#endif
    }
}
