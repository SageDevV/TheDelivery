using System.Collections.Generic;

namespace TheDelivery.Core
{
    /// <summary>
    /// CATÁLOGO DOS ATOS NA ORDEM DA HISTÓRIA: qual cena cada ato abre e como ele aparece
    /// para o jogador (Seleção de Atos). Os valores numéricos do <see cref="GameAct"/> NÃO são
    /// a ordem da história (são estáveis por causa da serialização) — a ordem vive aqui.
    ///
    /// Progressão linear: o primeiro ato está sempre liberado; cada um dos seguintes libera
    /// quando o ANTERIOR desta lista é concluído (ver SaveManager.IsActUnlocked).
    /// </summary>
    public static class GameActCatalog
    {
        public readonly struct ActInfo
        {
            public readonly GameAct Act;
            public readonly GameScene Scene;
            /// <summary>Rótulo curto da seleção ("Ato 1").</summary>
            public readonly string Number;
            /// <summary>Nome do ato ("A Cafeteria").</summary>
            public readonly string Title;

            public ActInfo(GameAct act, GameScene scene, string number, string title)
            {
                Act = act;
                Scene = scene;
                Number = number;
                Title = title;
            }
        }

        /// <summary>Atos jogáveis, na ordem em que a história acontece.</summary>
        public static readonly IReadOnlyList<ActInfo> Story = new[]
        {
            new ActInfo(GameAct.ActNightmare, GameScene.Nightmare, "Prólogo",    "O Pesadelo"),
            new ActInfo(GameAct.Act1,         GameScene.Cafeteria, "Ato 1",      "A Cafeteria"),
            new ActInfo(GameAct.ActJourney,   GameScene.Road,      "Interlúdio", "A Caminhada"),
            new ActInfo(GameAct.Act2,         GameScene.Reception, "Ato 2",      "A Recepção"),
            new ActInfo(GameAct.Act3,         GameScene.Apartment, "Ato 3",      "O Apartamento"),
            new ActInfo(GameAct.Act4,         GameScene.Apartment, "Ato 4",      "A Caçada"),
            new ActInfo(GameAct.ActEscape,    GameScene.Escape,    "Ato 5",      "A Fuga"),
        };

        /// <summary>Posição do ato na história (-1 se não for jogável).</summary>
        public static int IndexOf(GameAct act)
        {
            for (int i = 0; i < Story.Count; i++)
                if (Story[i].Act == act) return i;
            return -1;
        }

        public static bool TryGet(GameAct act, out ActInfo info)
        {
            int i = IndexOf(act);
            info = i >= 0 ? Story[i] : default;
            return i >= 0;
        }
    }
}
