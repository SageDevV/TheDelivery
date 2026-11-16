using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheDelivery.Persistence
{
    /// <summary>
    /// O QUE VAI NO ARQUIVO DE SAVE. Classe simples serializável pelo <see cref="JsonUtility"/>
    /// (campos públicos, listas de tipos serializáveis — sem Dictionary, que o JsonUtility não
    /// suporta). Quem preenche e quem aplica é o <see cref="SaveManager"/>.
    ///
    /// GRANULARIDADE: o jogo é roteirizado por beats, então o save guarda o ATO + a CENA + o
    /// BEAT do diretor ativo. Carregar recoloca o jogador nesse beat (o projeto garante que
    /// entrar direto num beat é seguro — "cada beat garante sozinho o estado de que precisa").
    /// A pose do jogador só é restaurada em checkpoints de exploração livre
    /// (<see cref="hasPlayerPose"/>); no início de ato quem posiciona é o diretor.
    ///
    /// VERSÃO: suba <see cref="CurrentVersion"/> ao mudar o formato e trate a migração no
    /// <see cref="SaveManager"/>. Save de versão MAIS NOVA que o jogo é recusado.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentVersion = 1;

        [Header("Metadados")]
        public int version = CurrentVersion;
        /// <summary>Quando foi salvo (UTC, ISO 8601).</summary>
        public string savedAtUtc;
        /// <summary>Tempo de jogo acumulado (s), para a UI de "Carregar".</summary>
        public float playTimeSeconds;
        /// <summary>Identificador do ponto de save ("act-start", ou o id de um Checkpoint).</summary>
        public string checkpointId;

        [Header("Progresso")]
        /// <summary>Nome da cena (= valor do enum GameScene = nome do arquivo .unity).</summary>
        public string scene;
        /// <summary>GameAct como número (os valores do enum são explícitos e estáveis).</summary>
        public int act;
        /// <summary>Tipo do diretor ativo (ex.: "Act4Director"); vazio = começar do início do ato.</summary>
        public string director;
        /// <summary>Beat do diretor como número (0 = None = início do ato).</summary>
        public int beat;

        [Header("Jogador")]
        public bool hasPlayerPose;
        public Vector3 playerPosition;
        public float playerYaw;
        public float cameraPitch;
        /// <summary>Estamina normalizada (0-1).</summary>
        public float stamina = 1f;
        /// <summary>Itens do inventário (nome do asset ItemData).</summary>
        public List<string> inventory = new List<string>();

        [Header("Progressão de atos")]
        /// <summary>
        /// Atos CONCLUÍDOS (GameAct como número). Cópia do perfil de progresso no momento do
        /// save — a fonte da verdade da Seleção de Atos é o perfil (SaveManager), que o Novo
        /// Jogo NÃO apaga; esta cópia serve para um save antigo reconstruir o perfil.
        /// </summary>
        public List<int> completedActs = new List<int>();

        [Header("Estado global")]
        /// <summary>Flags de história ("act3.deliveryAccepted"...). Presença = verdadeiro.</summary>
        public List<string> flags = new List<string>();
        /// <summary>Contadores/valores globais (chave-valor).</summary>
        public List<SaveNumber> numbers = new List<SaveNumber>();
    }

    [Serializable]
    public struct SaveNumber
    {
        public string key;
        public float value;
    }
}
