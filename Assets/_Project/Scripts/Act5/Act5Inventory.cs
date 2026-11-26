using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheDelivery.Act5
{
    /// <summary>
    /// Inventário do Ato 5 (2026-11-26): só o que a Clear carrega para os puzzles (barra, arame, gancho, chave).
    /// Estático e simples; zerado ao começar o ato (<see cref="Clear"/>). A lista aparece no canto da tela.
    /// </summary>
    public static class Act5Inventory
    {
        private static readonly List<(string id, string label)> items = new List<(string, string)>();
        public static event Action Changed;
        public static IReadOnlyList<(string id, string label)> Items => items;

        public static bool Has(string id) => items.Exists(i => i.id == id);
        public static void Add(string id, string label)
        {
            if (string.IsNullOrEmpty(id) || Has(id)) return;
            items.Add((id, string.IsNullOrEmpty(label) ? id : label));
            Changed?.Invoke();
        }
        public static void Remove(string id)
        {
            if (items.RemoveAll(i => i.id == id) > 0) Changed?.Invoke();
        }
        public static void Clear() { items.Clear(); Changed?.Invoke(); }
    }

    /// <summary>A lista do inventário no canto superior esquerdo (monta o próprio Canvas).</summary>
    public sealed class Act5InventoryUI : MonoBehaviour
    {
        private TextMeshProUGUI label;
        private CanvasGroup group;

        private void Awake()
        {
            var canvasGo = new GameObject("Act5InventoryCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var c = canvasGo.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 50;
            var sc = canvasGo.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080);
            group = canvasGo.GetComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
            var t = new GameObject("Items", typeof(RectTransform), typeof(TextMeshProUGUI));
            t.transform.SetParent(canvasGo.transform, false);
            label = t.GetComponent<TextMeshProUGUI>();
            var rt = label.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(40f, -36f); rt.sizeDelta = new Vector2(600f, 300f);
            label.fontSize = 26; label.color = new Color(0.86f, 0.84f, 0.78f, 0.9f); label.raycastTarget = false;
            Refresh();
        }
        private void OnEnable() => Act5Inventory.Changed += Refresh;
        private void OnDisable() => Act5Inventory.Changed -= Refresh;
        private void Refresh()
        {
            if (label == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (var (_, l) in Act5Inventory.Items) sb.AppendLine("• " + l);
            label.text = sb.ToString();
            group.alpha = Act5Inventory.Items.Count > 0 ? 1f : 0f;
        }
    }
}
