//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using UnityEngine;
using UnityEngine.UI;

namespace BoneCrackerGames.BuildingGen.Demo {

    /// <summary>
    /// Drives the demo's display-only overlay canvas: shows / fills the building info card from
    /// the inspector's selection event and toggles the controls legend on H / F1. Deliberately no
    /// Buttons and no EventSystem — every toggle is keyboard-driven (see the legend), so the demo
    /// works under either input handler without input-module wiring.
    /// </summary>
    public class BCG_DemoUI : MonoBehaviour {

        [Tooltip("The scene's building inspector (selection source).")]
        public BCG_DemoBuildingInspector inspector;

        [Tooltip("Info card root — hidden until a building is selected.")]
        public GameObject infoCard;

        [Tooltip("Info card title text.")]
        public Text infoTitle;

        [Tooltip("Info card body text.")]
        public Text infoBody;

        [Tooltip("Controls legend root — visible at start, toggled with H / F1.")]
        public GameObject helpPanel;

        void Awake() {

            if (inspector == null || infoCard == null || infoTitle == null || infoBody == null) {
                Debug.LogWarning("[BCG BuildingGen Demo] Demo UI is missing wiring (inspector / info card refs) — disabling.", this);
                enabled = false;
            }

        }

        void OnEnable() {

            if (inspector != null)
                inspector.SelectionChanged += OnSelectionChanged;

        }

        void OnDisable() {

            if (inspector != null)
                inspector.SelectionChanged -= OnSelectionChanged;

        }

        void Update() {

            if (BCG_DemoInput.HelpDown && helpPanel != null)
                helpPanel.SetActive(!helpPanel.activeSelf);

        }

        void OnSelectionChanged(BCG_DemoBuildingInfo info) {

            infoCard.SetActive(info != null);

            if (info != null) {
                infoTitle.text = info.HeaderLine;
                infoBody.text = info.BodyText();
            }

        }

    }

}
