//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Preview/confirm window for <see cref="BCG_AssetCleanup"/>. Scans for orphaned generated
    /// meshes/prefabs (those no scene references), lists them with per-item checkboxes and the total
    /// reclaimable size, and deletes the checked ones after a confirm dialog. Before each scan it asks to
    /// save any modified open scene so the on-disk dependency data is current. Built on UI Toolkit, themed
    /// via <see cref="BCG_UITheme"/>.
    /// </summary>
    public class BCG_AssetCleanupWindow : EditorWindow {

        BCG_AssetCleanup.ScanResult scan;
        bool hasScanned;
        bool scanCancelled;

        readonly List<bool> meshChecks = new List<bool>();
        readonly List<bool> prefabChecks = new List<bool>();

        ListView meshList;
        ListView prefabList;
        Label meshHeader;
        Label prefabHeader;
        Label statusLabel;
        Label totalLabel;
        Button deleteButton;

        public static void Open() {

            BCG_AssetCleanupWindow window = GetWindow<BCG_AssetCleanupWindow>(true, "Clean Unused Assets", true);
            window.minSize = new Vector2(460f, 380f);
            window.RunScan();
            if (window.rootVisualElement != null)
                window.RefreshLists();
            window.Show();
        }

        /// <summary>Saves modified scenes (so deps are current), then scans and resets the checkboxes.</summary>
        void RunScan() {

            //  Stale on-disk deps would flag a just-placed building's mesh as an orphan — save first.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                scanCancelled = true;
                hasScanned = false;
                return;
            }

            scan = BCG_AssetCleanup.ScanForOrphans();
            scanCancelled = false;
            hasScanned = true;

            meshChecks.Clear();
            for (int i = 0; i < scan.orphanMeshPaths.Count; i++)
                meshChecks.Add(true);

            prefabChecks.Clear();
            for (int i = 0; i < scan.orphanPrefabPaths.Count; i++)
                prefabChecks.Add(true);
        }

        void CreateGUI() {

            VisualElement root = rootVisualElement;
            BCG_UITheme.Apply(root);

            root.Add(BCG_UI.SectionHeader("Clean Unused Generated Assets"));
            root.Add(new Label(
                "Lists generated meshes & prefabs (BCG_Building* by name) that no scene in the project " +
                "references, from the configured output folder and the default Generated/. Closed scenes " +
                "are scanned too. Nothing is deleted until you confirm.") {
                style = { whiteSpace = WhiteSpace.Normal }
            });

            root.Add(new Button(() => { RunScan(); RefreshLists(); }) {
                text = "Rescan",
                tooltip = "Re-run the scan (saves any modified open scene first)."
            });

            statusLabel = new Label { style = { whiteSpace = WhiteSpace.Normal } };
            root.Add(statusLabel);

            //  Select-all / none — flips both check lists, then re-renders the checkboxes.
            VisualElement selectStrip = new VisualElement();
            selectStrip.AddToClassList("bcg-tab-strip");
            selectStrip.Add(new Button(() => SetAll(true)) {
                text = "Select All", tooltip = "Check every listed asset."
            });
            selectStrip.Add(new Button(() => SetAll(false)) {
                text = "Select None", tooltip = "Uncheck every listed asset."
            });
            root.Add(selectStrip);

            meshHeader = BCG_UI.SectionHeader("Meshes");
            meshList = MakeOrphanList(() => scan.orphanMeshPaths, meshChecks);
            root.Add(meshHeader);
            root.Add(meshList);

            prefabHeader = BCG_UI.SectionHeader("Prefabs");
            prefabList = MakeOrphanList(() => scan.orphanPrefabPaths, prefabChecks);
            root.Add(prefabHeader);
            root.Add(prefabList);

            totalLabel = new Label();
            root.Add(totalLabel);

            root.Add(BCG_UI.Separator());
            deleteButton = BCG_UI.DangerButton("Delete Checked (0)",
                "Deletes the checked orphans after a confirm dialog.", DeleteChecked);
            root.Add(deleteButton);

            RefreshLists();   // renders empty state until Open()/Rescan has scanned
        }

        /// <summary>Row = Toggle + filename Label + size Label; bindItem syncs the shared checked-state list.</summary>
        ListView MakeOrphanList(System.Func<List<string>> pathsGetter, List<bool> checks) {

            ListView list = new ListView {
                fixedItemHeight = 18,
                selectionType = SelectionType.None,
                makeItem = () => {

                    OrphanRow row = new OrphanRow();
                    row.toggle.RegisterValueChangedCallback(evt => {
                        if (row.toggle.userData is int index && index < checks.Count) {
                            checks[index] = evt.newValue;
                            UpdateDeleteButton();   // live count + disabled-at-zero.
                        }
                    });
                    return row;
                },
                bindItem = (element, index) => {

                    List<string> paths = pathsGetter();
                    if (paths == null || index >= paths.Count)
                        return;

                    OrphanRow row = (OrphanRow)element;
                    string path = paths[index];
                    row.pathLabel.text = System.IO.Path.GetFileName(path);
                    row.sizeLabel.text = BCG_AssetCleanup.FormatBytes(FileSizeOf(path));
                    row.toggle.userData = index;
                    row.toggle.SetValueWithoutNotify(index < checks.Count && checks[index]);
                }
            };
            list.AddToClassList("bcg-list");
            return list;
        }

        /// <summary>Rebuilds both lists from the current scan, updates the per-category header counts,
        /// hides empty sections, refreshes the delete button, and sets the status/total labels. Safe to
        /// call before any scan has run (renders the empty state).</summary>
        void RefreshLists() {

            List<string> meshPaths = hasScanned ? scan.orphanMeshPaths : new List<string>();
            List<string> prefabPaths = hasScanned ? scan.orphanPrefabPaths : new List<string>();

            meshList.itemsSource = meshPaths;
            prefabList.itemsSource = prefabPaths;
            meshList.Rebuild();
            prefabList.Rebuild();

            //  Per-category header counts + hide the whole section when that category is empty.
            meshHeader.text = "Meshes (" + meshPaths.Count + ")";
            prefabHeader.text = "Prefabs (" + prefabPaths.Count + ")";
            SetSectionVisible(meshHeader, meshList, meshPaths.Count > 0);
            SetSectionVisible(prefabHeader, prefabList, prefabPaths.Count > 0);

            UpdateDeleteButton();

            if (scanCancelled) {
                statusLabel.text = "Scan cancelled because open scenes were not saved. Click Rescan to try again.";
                totalLabel.text = string.Empty;
                return;
            }

            if (!hasScanned) {
                statusLabel.text = string.Empty;
                totalLabel.text = string.Empty;
                return;
            }

            int orphanCount = scan.orphanMeshPaths.Count + scan.orphanPrefabPaths.Count;

            if (orphanCount == 0) {
                statusLabel.text = "No unused generated assets found across " + scan.sceneCount +
                    " scene(s). The Generated folder is clean.";
                totalLabel.text = string.Empty;
                return;
            }

            statusLabel.text = string.Empty;
            totalLabel.text = orphanCount + " unused asset(s) — " +
                BCG_AssetCleanup.FormatBytes(scan.totalBytes) + " reclaimable (scanned " +
                scan.sceneCount + " scene(s)).";
        }

        /// <summary>Checks/unchecks every entry in both lists, then re-renders the rows and the delete button.</summary>
        void SetAll(bool value) {

            for (int i = 0; i < meshChecks.Count; i++) meshChecks[i] = value;
            for (int i = 0; i < prefabChecks.Count; i++) prefabChecks[i] = value;

            meshList.Rebuild();
            prefabList.Rebuild();
            UpdateDeleteButton();
        }

        /// <summary>Refreshes the delete button's live checked-count label and disables it at zero.</summary>
        void UpdateDeleteButton() {

            int count = CountChecked(meshChecks) + CountChecked(prefabChecks);
            deleteButton.text = "Delete Checked (" + count + ")";
            deleteButton.SetEnabled(count > 0);
        }

        static int CountChecked(List<bool> checks) {

            int n = 0;
            for (int i = 0; i < checks.Count; i++) if (checks[i]) n++;
            return n;
        }

        static void SetSectionVisible(VisualElement header, VisualElement list, bool visible) {

            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            header.style.display = display;
            list.style.display = display;
        }

        /// <summary>Single-path on-disk byte size (missing file = 0). Zero-alloc replacement for the
        /// array-taking engine overload on the per-item bindItem hot path (fires on every scroll/recycle).</summary>
        static long FileSizeOf(string path) {

            string full = System.IO.Path.GetFullPath(path);
            return System.IO.File.Exists(full) ? new System.IO.FileInfo(full).Length : 0L;
        }

        void DeleteChecked() {

            if (!hasScanned || scan.orphanMeshPaths == null)
                return;

            List<string> toDelete = new List<string>();
            for (int i = 0; i < scan.orphanMeshPaths.Count; i++)
                if (meshChecks[i]) toDelete.Add(scan.orphanMeshPaths[i]);
            for (int i = 0; i < scan.orphanPrefabPaths.Count; i++)
                if (prefabChecks[i]) toDelete.Add(scan.orphanPrefabPaths[i]);

            if (toDelete.Count == 0)
                return;

            long bytes = BCG_AssetCleanup.SumFileBytes(toDelete);

            bool ok = EditorUtility.DisplayDialog("Delete Unused Assets",
                "Permanently delete " + toDelete.Count + " asset(s) (" + BCG_AssetCleanup.FormatBytes(bytes) +
                ")?\n\nThis cannot be undone.",
                "Delete", "Cancel");

            if (!ok)
                return;

            int deleted = BCG_AssetCleanup.DeleteOrphans(toDelete);
            Debug.Log("[BCG BuildingGen] Cleaned " + deleted + " unused generated asset(s), " +
                BCG_AssetCleanup.FormatBytes(bytes) + " reclaimed.");

            RunScan();
            RefreshLists();
        }

        /// <summary>ListView row: a checkbox, the filename, and the file size.</summary>
        class OrphanRow : VisualElement {

            public readonly Toggle toggle;
            public readonly Label pathLabel;
            public readonly Label sizeLabel;

            public OrphanRow() {

                style.flexDirection = FlexDirection.Row;

                toggle = new Toggle();
                Add(toggle);

                pathLabel = new Label { style = { flexGrow = 1 } };
                Add(pathLabel);

                sizeLabel = new Label();
                Add(sizeLabel);
            }
        }

    }

}
