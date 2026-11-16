using System.Collections.Generic;
using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// ORÇAMENTO DE SOMBRAS EM TEMPO REAL. Luz pontual com sombra renderiza a cena SEIS vezes
    /// (uma por face do cubo) — com 16 delas o apartamento desenhava ~30 M triângulos por
    /// quadro, 80% só em sombra (auditoria de performance, Atos 3-4). As luzes continuam em
    /// tempo real (o Ato 4 corta a energia, há luzes piscando), mas só as
    /// <see cref="maxShadowedLights"/> mais relevantes para a câmera projetam sombra:
    /// <list type="bullet">
    /// <item>luz no MESMO cômodo da câmera vale muito mais (é a que o jogador vê a sombra);</item>
    /// <item>depois, a mais próxima, ponderada pelo alcance;</item>
    /// <item>luz apagada (desabilitada ou intensidade ~0) não entra na disputa;</item>
    /// <item>histerese: a luz que já tem sombra ganha um bônus, para não "piscar" a sombra
    /// quando o jogador anda na fronteira entre duas luzes.</item>
    /// </list>
    /// Cada luz guarda o tipo de sombra AUTORADO (Soft/Hard) e volta para ele quando eleita.
    /// Luzes autoradas SEM sombra são ignoradas.
    /// </summary>
    public sealed class ShadowBudget : MonoBehaviour
    {
        [Tooltip("Quantas luzes adicionais (pontuais/spot) podem projetar sombra ao mesmo tempo.")]
        [Range(0, 8)] [SerializeField] private int maxShadowedLights = 3;
        [Tooltip("Raízes dos cômodos (ex.: _Environment/LivingRoom). A luz filha de um cômodo pertence a ele; " +
                 "a câmera está no cômodo cujos limites a contêm.")]
        [SerializeField] private Transform[] rooms;
        [Tooltip("Reavaliações por segundo (não precisa ser todo quadro).")]
        [Min(1f)] [SerializeField] private float updatesPerSecond = 8f;
        [Tooltip("Bônus (m) para a luz que já tem sombra — evita troca nervosa na fronteira.")]
        [Min(0f)] [SerializeField] private float hysteresis = 1.5f;
        [Tooltip("Alvo (vazio = Camera.main).")]
        [SerializeField] private Transform target;

        private struct Entry
        {
            public Light Light;
            public LightShadows Authored;
            public int Room; // índice em rooms, -1 = nenhum
            public bool Shadowed;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private Bounds[] roomBounds;
        private float nextUpdate;
        private readonly List<(float score, int index)> ranking = new List<(float, int)>();

        private void Start()
        {
            roomBounds = new Bounds[rooms != null ? rooms.Length : 0];
            for (int i = 0; i < roomBounds.Length; i++)
                roomBounds[i] = RoomBounds(rooms[i]);

            foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (l.type == LightType.Directional || l.shadows == LightShadows.None)
                    continue;
                if (l.gameObject.scene != gameObject.scene)
                    continue;
                entries.Add(new Entry { Light = l, Authored = l.shadows, Room = RoomOf(l.transform), Shadowed = true });
            }

            Evaluate();
        }

        private void OnDisable()
        {
            // Devolve a autoria (ex.: o componente desligado para comparação/debug).
            foreach (Entry e in entries)
                if (e.Light != null) e.Light.shadows = e.Authored;
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < nextUpdate)
                return;
            nextUpdate = Time.unscaledTime + 1f / updatesPerSecond;
            Evaluate();
        }

        private void Evaluate()
        {
            Transform t = target != null ? target : (Camera.main != null ? Camera.main.transform : null);
            if (t == null)
                return;

            Vector3 p = t.position;
            int camRoom = -1;
            for (int i = 0; i < roomBounds.Length; i++)
                if (roomBounds[i].Contains(p)) { camRoom = i; break; }

            ranking.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                Light l = entries[i].Light;
                if (l == null || !l.isActiveAndEnabled || l.intensity < 0.01f)
                    continue;
                float dist = Vector3.Distance(p, l.transform.position);
                if (dist > l.range * 1.5f + 2f)
                    continue; // longe demais para a sombra importar
                float score = dist / Mathf.Max(0.5f, l.range);
                if (camRoom >= 0 && entries[i].Room == camRoom) score -= 10f;
                if (entries[i].Shadowed) score -= hysteresis / Mathf.Max(0.5f, l.range);
                ranking.Add((score, i));
            }
            ranking.Sort((a, b) => a.score.CompareTo(b.score));

            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                e.Shadowed = false;
                entries[i] = e;
            }
            for (int r = 0; r < ranking.Count && r < maxShadowedLights; r++)
            {
                Entry e = entries[ranking[r].index];
                e.Shadowed = true;
                entries[ranking[r].index] = e;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e.Light == null) continue;
                LightShadows want = e.Shadowed ? e.Authored : LightShadows.None;
                if (e.Light.shadows != want)
                    e.Light.shadows = want;
            }
        }

        private int RoomOf(Transform light)
        {
            if (rooms == null) return -1;
            for (int i = 0; i < rooms.Length; i++)
                if (rooms[i] != null && light.IsChildOf(rooms[i]))
                    return i;
            return -1;
        }

        private static Bounds RoomBounds(Transform room)
        {
            if (room == null) return new Bounds();
            // Cômodo = planta do(s) teto(s) do cômodo, do piso ao teto. As raízes dos cômodos
            // incluem a massa do prédio acima; o teto dá a planta interna exata.
            bool any = false;
            Bounds b = new Bounds();
            foreach (Collider c in room.GetComponentsInChildren<Collider>(true))
            {
                if (c.name != "Ceiling") continue;
                Bounds cb = c.bounds;
                var floorToCeiling = new Bounds();
                floorToCeiling.SetMinMax(new Vector3(cb.min.x, cb.min.y - 3.5f, cb.min.z), new Vector3(cb.max.x, cb.min.y + 0.2f, cb.max.z));
                if (!any) { b = floorToCeiling; any = true; } else b.Encapsulate(floorToCeiling);
            }
            if (!any)
                foreach (Renderer r in room.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            return b;
        }
    }
}
