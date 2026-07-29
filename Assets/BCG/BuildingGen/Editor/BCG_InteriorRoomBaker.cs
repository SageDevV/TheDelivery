//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.IO;
using UnityEditor;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>Bakes the fake-interior room atlas: 8 parametric box-rooms rendered from the window
    /// plane (FOV 90, aspect 1, room depth = width, so the back wall fills the central half of each
    /// tile - the farFrac = 0.5 contract BCG_FacadeInterior.shader assumes). Deterministic per room
    /// index; purely algorithmic (no external imagery). Dev utility - no MenuItem; rerun via
    /// execute_code when the room look changes. Only the baked PNG ships as a dependency.</summary>
    public static class BCG_InteriorRoomBaker {

        const int kTile = 512;
        const int kCols = 4;
        const int kRows = 2;
        const string kOutPath = "Assets/BCG/BuildingGen/Textures/BCG_InteriorAtlas.png";

        //  Rooms bake far below the scene so an open scene never contaminates the capture.
        static readonly Vector3 kBakeOrigin = new Vector3(0f, -4000f, 0f);

        public static string BakeAtlas() {

            Texture2D atlas = new Texture2D(kCols * kTile, kRows * kTile, TextureFormat.RGB24, false);

            for (int i = 0; i < kCols * kRows; i++) {

                GameObject root = BuildRoom(i);
                Camera cam = new GameObject("BCG_BakeCam").AddComponent<Camera>();

                try {

                    cam.transform.position = kBakeOrigin;            //  window plane center
                    cam.transform.rotation = Quaternion.identity;    //  looking +Z into the room
                    cam.fieldOfView = 90f;
                    cam.aspect = 1f;
                    cam.nearClipPlane = 0.01f;
                    cam.farClipPlane = 50f;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = Color.black;

                    RenderTexture rt = new RenderTexture(kTile, kTile, 24);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;

                    Texture2D tile = new Texture2D(kTile, kTile, TextureFormat.RGB24, false);
                    tile.ReadPixels(new Rect(0, 0, kTile, kTile), 0, 0);
                    atlas.SetPixels((i % kCols) * kTile, (i / kCols) * kTile, kTile, kTile, tile.GetPixels());

                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    Object.DestroyImmediate(tile);
                    Object.DestroyImmediate(rt);

                } finally {

                    Object.DestroyImmediate(cam.gameObject);
                    Object.DestroyImmediate(root);

                }

            }

            File.WriteAllBytes(kOutPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(kOutPath, ImportAssetOptions.ForceUpdate);

            return kOutPath;

        }

        /// <summary>One parametric room: 3x3x3 box (walls/floor/ceiling/back) + seeded furniture
        /// blocks + a bright ceiling panel. Unlit materials; fake shading via darkened side tints.</summary>
        static GameObject BuildRoom(int index) {

            System.Random r = new System.Random(index * 7919 + 13);
            GameObject root = new GameObject("BCG_BakeRoom_" + index);
            root.transform.position = kBakeOrigin;

            //  Warm-ish seeded palette per room.
            Color wall = Color.HSVToRGB((float)r.NextDouble(), 0.08f + (float)r.NextDouble() * 0.15f, 0.55f + (float)r.NextDouble() * 0.3f);
            Color floor = wall * 0.45f;
            Color back = wall * 0.85f;
            Color side = wall * 0.62f;

            AddPanel(root, new Vector3(0f, 0f, 3f), new Vector3(3f, 3f, 0.01f), back, r);         //  back wall
            AddPanel(root, new Vector3(-1.5f, 0f, 1.5f), new Vector3(0.01f, 3f, 3f), side, r);    //  left
            AddPanel(root, new Vector3(1.5f, 0f, 1.5f), new Vector3(0.01f, 3f, 3f), side, r);     //  right
            AddPanel(root, new Vector3(0f, -1.5f, 1.5f), new Vector3(3f, 0.01f, 3f), floor, r);   //  floor
            AddPanel(root, new Vector3(0f, 1.5f, 1.5f), new Vector3(3f, 0.01f, 3f), wall * 0.9f, r);  //  ceiling

            if (index == 0) {
                // --- MODERN OFFICE ---
                floor = new Color(0.2f, 0.25f, 0.35f); // Dark blue carpet
                
                // Grid ceiling lines
                AddPanel(root, new Vector3(0f, 1.48f, 1.5f), new Vector3(3f, 0.01f, 0.01f), Color.black, r);
                AddPanel(root, new Vector3(0f, 1.48f, 1.0f), new Vector3(3f, 0.01f, 0.01f), Color.black, r);
                AddPanel(root, new Vector3(0f, 1.48f, 2.0f), new Vector3(3f, 0.01f, 0.01f), Color.black, r);
                AddPanel(root, new Vector3(-0.75f, 1.48f, 1.5f), new Vector3(0.01f, 0.01f, 3f), Color.black, r);
                AddPanel(root, new Vector3(0.75f, 1.48f, 1.5f), new Vector3(0.01f, 0.01f, 3f), Color.black, r);
                
                // Fluorescent lights
                AddPanel(root, new Vector3(-0.6f, 1.47f, 1.2f), new Vector3(0.4f, 0.02f, 0.8f), Color.white, r);
                AddPanel(root, new Vector3(0.6f, 1.47f, 2.2f), new Vector3(0.4f, 0.02f, 0.8f), Color.white, r);

                // Vertical blinds on sides
                for (int b = 0; b < 4; b++) {
                    AddPanel(root, new Vector3(-1.49f, 0.6f - b * 0.4f, 0.1f), new Vector3(0.02f, 0.32f, 0.15f), Color.white * 0.9f, r);
                    AddPanel(root, new Vector3(1.49f, 0.6f - b * 0.4f, 0.1f), new Vector3(0.02f, 0.32f, 0.15f), Color.white * 0.9f, r);
                }

                // Desk & Chair
                AddDesk(root, new Vector3(0f, -1.5f, 2.0f), 1.5f, 0.75f, 0.8f, new Color(0.85f, 0.85f, 0.85f), r);
                AddChair(root, new Vector3(0f, -1.5f, 2.45f), new Color(0.2f, 0.2f, 0.2f), new Color(0.3f, 0.3f, 0.3f), r);
                AddMonitor(root, new Vector3(0f, -0.75f, 2.0f), new Color(0.1f, 0.6f, 0.9f), r); // Glowing Cyan Screen

                // Whiteboard on back wall
                AddPanel(root, new Vector3(-0.7f, 0.3f, 2.98f), new Vector3(1.2f, 0.8f, 0.02f), Color.white, r);
                AddPanel(root, new Vector3(-0.7f, 0.3f, 2.97f), new Vector3(1.24f, 0.84f, 0.01f), new Color(0.2f, 0.2f, 0.2f), r); // border
            }
            else if (index == 1) {
                // --- EXECUTIVE / MANAGER'S OFFICE ---
                wall = new Color(0.35f, 0.2f, 0.12f); // Wood paneling
                back = wall * 0.85f;
                side = wall * 0.62f;
                floor = new Color(0.15f, 0.25f, 0.15f); // Green carpet

                // Ceiling light panel
                AddPanel(root, new Vector3(0f, 1.45f, 1.5f), new Vector3(1.2f, 0.02f, 0.8f), new Color(1f, 0.95f, 0.8f), r);

                // Bookcase on the back wall
                AddBookcase(root, new Vector3(-0.8f, -1.5f, 2.85f), 1.0f, 2.2f, 0.35f, new Color(0.25f, 0.15f, 0.08f), r);

                // Office desk (Wooden)
                AddDesk(root, new Vector3(0.3f, -1.5f, 1.8f), 1.3f, 0.75f, 0.7f, new Color(0.35f, 0.2f, 0.12f), r);
                AddChair(root, new Vector3(0.3f, -1.5f, 2.25f), new Color(0.15f, 0.15f, 0.15f), new Color(0.15f, 0.15f, 0.15f), r);
                AddMonitor(root, new Vector3(0.3f, -0.75f, 1.8f), new Color(0.9f, 0.5f, 0.1f), r); // Glowing Amber Screen

                // Door on the back wall
                AddPanel(root, new Vector3(0.9f, -0.5f, 2.98f), new Vector3(0.7f, 2.0f, 0.02f), new Color(0.2f, 0.1f, 0.05f), r); // door
                AddPanel(root, new Vector3(0.9f, -0.5f, 2.97f), new Vector3(0.76f, 2.06f, 0.01f), new Color(0.1f, 0.05f, 0.02f), r); // frame
                AddPanel(root, new Vector3(0.6f, -0.5f, 2.95f), new Vector3(0.05f, 0.1f, 0.05f), new Color(0.8f, 0.65f, 0.2f), r); // handle
            }
            else if (index == 2) {
                // --- TECH SERVER / SERVER ROOM ---
                wall = new Color(0.15f, 0.15f, 0.18f); // Dark metal walls
                back = wall * 0.85f;
                side = wall * 0.62f;
                floor = new Color(0.08f, 0.08f, 0.1f); // Dark tile floor

                // Dark ceiling spot lights
                AddPanel(root, new Vector3(-0.6f, 1.45f, 1.0f), new Vector3(0.15f, 0.05f, 0.15f), Color.white, r);
                AddPanel(root, new Vector3(0.6f, 1.45f, 1.0f), new Vector3(0.15f, 0.05f, 0.15f), Color.cyan, r);
                AddPanel(root, new Vector3(-0.6f, 1.45f, 2.0f), new Vector3(0.15f, 0.05f, 0.15f), Color.cyan, r);
                AddPanel(root, new Vector3(0.6f, 1.45f, 2.0f), new Vector3(0.15f, 0.05f, 0.15f), Color.white, r);

                // Server racks on walls
                AddServerRack(root, new Vector3(-1.0f, -1.5f, 1.2f), 2.5f, r);
                AddServerRack(root, new Vector3(-1.0f, -1.5f, 2.2f), 2.5f, r);
                AddServerRack(root, new Vector3(1.0f, -1.5f, 1.7f), 2.5f, r);

                // Huge monitor display on back wall
                AddPanel(root, new Vector3(0f, 0.2f, 2.98f), new Vector3(1.6f, 1.0f, 0.02f), new Color(0.1f, 0.1f, 0.1f), r); // bezel
                AddPanel(root, new Vector3(0f, 0.2f, 2.97f), new Vector3(1.5f, 0.9f, 0.01f), new Color(0.1f, 0.8f, 0.3f), r); // Glowing green matrix
            }
            else if (index == 3) {
                // --- MODERN RESIDENTIAL LIVING ROOM ---
                wall = new Color(0.85f, 0.82f, 0.78f); // Beige walls
                back = wall * 0.85f;
                side = wall * 0.62f;
                floor = new Color(0.5f, 0.35f, 0.22f); // Wood floor

                // Floor rug
                AddPanel(root, new Vector3(0f, -1.49f, 1.5f), new Vector3(1.8f, 0.01f, 1.4f), new Color(0.7f, 0.65f, 0.6f), r);

                // Ceiling fan light
                AddPanel(root, new Vector3(0f, 1.4f, 1.5f), new Vector3(0.2f, 0.1f, 0.2f), new Color(1f, 0.97f, 0.85f), r);
                AddPanel(root, new Vector3(0f, 1.42f, 1.5f), new Vector3(1.2f, 0.02f, 0.1f), new Color(0.2f, 0.15f, 0.1f), r);
                AddPanel(root, new Vector3(0f, 1.42f, 1.5f), new Vector3(0.1f, 0.02f, 1.2f), new Color(0.2f, 0.15f, 0.1f), r);

                // Sofa in the center
                AddSofa(root, new Vector3(0f, -1.5f, 1.9f), 1.8f, 0.75f, 0.8f, new Color(0.25f, 0.35f, 0.5f), r);

                // Side table and table lamp
                AddPanel(root, new Vector3(1.1f, -1.5f, 2.2f), new Vector3(0.4f, 0.5f, 0.4f), new Color(0.35f, 0.25f, 0.15f), r); // side table
                AddPanel(root, new Vector3(1.1f, -0.9f, 2.2f), new Vector3(0.15f, 0.3f, 0.15f), new Color(1f, 0.9f, 0.5f), r); // shade glow
                AddPanel(root, new Vector3(1.1f, -1.0f, 2.2f), new Vector3(0.04f, 0.2f, 0.04f), Color.black, r);

                // Hanging curtains
                AddPanel(root, new Vector3(-1.4f, 0f, 0.1f), new Vector3(0.18f, 3.0f, 0.1f), new Color(0.7f, 0.2f, 0.2f), r); // red curtains
                AddPanel(root, new Vector3(1.4f, 0f, 0.1f), new Vector3(0.18f, 3.0f, 0.1f), new Color(0.7f, 0.2f, 0.2f), r);

                // Painting on back wall
                AddPanel(root, new Vector3(0f, 0.3f, 2.98f), new Vector3(1.1f, 0.7f, 0.02f), new Color(0.15f, 0.15f, 0.15f), r); // frame
                AddPanel(root, new Vector3(0f, 0.3f, 2.97f), new Vector3(1.0f, 0.6f, 0.01f), new Color(0.8f, 0.4f, 0.3f), r); // canvas
            }
            else if (index == 4) {
                // --- RESIDENTIAL BEDROOM ---
                wall = new Color(0.78f, 0.78f, 0.85f); // Soft lavender walls
                back = wall * 0.85f;
                side = wall * 0.62f;
                floor = new Color(0.4f, 0.3f, 0.2f); // Wood floor

                // Floor rug
                AddPanel(root, new Vector3(0f, -1.49f, 1.5f), new Vector3(1.6f, 0.01f, 1.4f), new Color(0.95f, 0.95f, 0.95f), r);

                // Ceiling light bowl
                AddPanel(root, new Vector3(0f, 1.45f, 1.5f), new Vector3(0.3f, 0.08f, 0.3f), new Color(1f, 0.98f, 0.9f), r);

                // Bed
                AddBed(root, new Vector3(-0.4f, -1.5f, 1.8f), new Color(0.6f, 0.3f, 0.6f), r);

                // Bedside table
                AddPanel(root, new Vector3(0.6f, -1.5f, 1.3f), new Vector3(0.35f, 0.5f, 0.35f), new Color(0.45f, 0.35f, 0.25f), r);
                AddPanel(root, new Vector3(0.6f, -0.95f, 1.3f), new Vector3(0.12f, 0.12f, 0.12f), new Color(1f, 0.95f, 0.7f), r); // small lamp

                // Wardrobe / Closet
                AddPanel(root, new Vector3(1.25f, -0.4f, 2.2f), new Vector3(0.4f, 2.2f, 1.0f), new Color(0.35f, 0.25f, 0.15f), r);
                AddPanel(root, new Vector3(1.04f, -0.4f, 2.2f), new Vector3(0.01f, 2.2f, 0.02f), Color.black, r);

                // Hanging curtains
                AddPanel(root, new Vector3(-1.4f, 0f, 0.1f), new Vector3(0.18f, 3.0f, 0.1f), new Color(0.85f, 0.85f, 0.9f), r);
                AddPanel(root, new Vector3(1.4f, 0f, 0.1f), new Vector3(0.18f, 3.0f, 0.1f), new Color(0.85f, 0.85f, 0.9f), r);
            }
            else if (index == 5) {
                // --- RESIDENTIAL STUDY / LIBRARY ---
                wall = new Color(0.3f, 0.18f, 0.1f); // Dark wood paneling
                back = wall * 0.85f;
                side = wall * 0.62f;
                floor = new Color(0.25f, 0.1f, 0.1f); // Dark red carpet

                // Warm chandelier light
                AddPanel(root, new Vector3(0f, 1.45f, 1.5f), new Vector3(0.4f, 0.05f, 0.4f), new Color(1f, 0.9f, 0.6f), r);
                AddPanel(root, new Vector3(0f, 1.3f, 1.5f), new Vector3(0.02f, 0.3f, 0.02f), Color.black, r);

                // Bookcases
                AddBookcase(root, new Vector3(-0.75f, -1.5f, 2.85f), 1.1f, 2.4f, 0.3f, new Color(0.2f, 0.1f, 0.05f), r);
                AddBookcase(root, new Vector3(0.75f, -1.5f, 2.85f), 1.1f, 2.4f, 0.3f, new Color(0.2f, 0.1f, 0.05f), r);

                // Leather Armchair
                AddPanel(root, new Vector3(0f, -1.5f, 1.6f), new Vector3(0.7f, 0.4f, 0.7f), new Color(0.45f, 0.1f, 0.1f), r);
                AddPanel(root, new Vector3(0f, -0.9f, 1.95f), new Vector3(0.7f, 0.8f, 0.15f), new Color(0.4f, 0.08f, 0.08f), r);
                AddPanel(root, new Vector3(-0.38f, -1.2f, 1.6f), new Vector3(0.12f, 0.5f, 0.7f), new Color(0.35f, 0.08f, 0.08f), r);
                AddPanel(root, new Vector3(0.38f, -1.2f, 1.6f), new Vector3(0.12f, 0.5f, 0.7f), new Color(0.35f, 0.08f, 0.08f), r);

                // Small side table & glowing lamp
                AddPanel(root, new Vector3(0.6f, -1.5f, 1.5f), new Vector3(0.3f, 0.45f, 0.3f), new Color(0.25f, 0.12f, 0.06f), r);
                AddPanel(root, new Vector3(0.6f, -0.95f, 1.5f), new Vector3(0.15f, 0.2f, 0.15f), new Color(1f, 0.85f, 0.4f), r);
            }
            else if (index == 6) {
                // --- RETAIL STORE / BOUTIQUE ---
                wall = new Color(0.9f, 0.9f, 0.9f); // Clean white walls
                back = wall * 0.85f;
                side = wall * 0.62f;
                floor = new Color(0.75f, 0.75f, 0.75f); // Light grey tiles

                // Floor tile lines
                AddPanel(root, new Vector3(0f, -1.49f, 1.0f), new Vector3(3f, 0.01f, 0.01f), new Color(0.5f, 0.5f, 0.5f), r);
                AddPanel(root, new Vector3(0f, -1.49f, 2.0f), new Vector3(3f, 0.01f, 0.01f), new Color(0.5f, 0.5f, 0.5f), r);
                AddPanel(root, new Vector3(-0.75f, -1.49f, 1.5f), new Vector3(0.01f, 0.01f, 3f), new Color(0.5f, 0.5f, 0.5f), r);
                AddPanel(root, new Vector3(0.75f, -1.49f, 1.5f), new Vector3(0.01f, 0.01f, 3f), new Color(0.5f, 0.5f, 0.5f), r);

                // Ceiling spots
                for (int s = 0; s < 3; s++) {
                    AddPanel(root, new Vector3(-0.8f + s * 0.8f, 1.45f, 1.0f), new Vector3(0.12f, 0.05f, 0.12f), Color.white, r);
                    AddPanel(root, new Vector3(-0.8f + s * 0.8f, 1.45f, 2.0f), new Vector3(0.12f, 0.05f, 0.12f), Color.white, r);
                }

                // Retail display shelves
                AddBookcase(root, new Vector3(0f, -1.5f, 2.85f), 1.8f, 2.2f, 0.3f, new Color(0.85f, 0.75f, 0.65f), r);

                // Service Counter & register
                AddDesk(root, new Vector3(0.7f, -1.5f, 1.3f), 0.9f, 0.9f, 0.5f, new Color(0.2f, 0.2f, 0.2f), r);
                AddPanel(root, new Vector3(0.7f, -0.55f, 1.3f), new Vector3(0.2f, 0.15f, 0.2f), new Color(0.1f, 0.1f, 0.1f), r);
                AddPanel(root, new Vector3(0.7f, -0.45f, 1.25f), new Vector3(0.12f, 0.08f, 0.01f), new Color(0.3f, 0.8f, 0.9f), r);

                // Clothing hanger rack / display box on left wall
                AddPanel(root, new Vector3(-1.2f, -1.5f, 1.8f), new Vector3(0.4f, 1.4f, 1.2f), new Color(0.4f, 0.4f, 0.4f), r);
                for (int c = 0; c < 5; c++) {
                    Color itemColor = Color.HSVToRGB((float)c / 5.0f, 0.6f, 0.7f);
                    AddPanel(root, new Vector3(-1.2f, -0.6f, 1.3f + c * 0.22f), new Vector3(0.3f, 0.35f, 0.08f), itemColor, r);
                }
            }
            else if (index == 7) {
                // --- CAFE / DINER ---
                wall = new Color(0.55f, 0.25f, 0.18f); // Brick red
                back = wall * 0.85f;
                side = wall * 0.62f;
                floor = new Color(0.2f, 0.2f, 0.2f); // Dark tiles

                // Checkered floor pattern
                for (int fx = -1; fx <= 1; fx++) {
                    for (int fz = 0; fz <= 2; fz++) {
                        if ((fx + fz) % 2 == 0) {
                            AddPanel(root, new Vector3(fx * 1.0f, -1.49f, fz * 1.0f + 0.5f), new Vector3(1.0f, 0.01f, 1.0f), new Color(0.85f, 0.8f, 0.75f), r);
                        }
                    }
                }

                // Hanging pendant lights
                for (int pl = 0; pl < 2; pl++) {
                    float pZ = 1.0f + pl * 1.0f;
                    AddPanel(root, new Vector3(0f, 1.15f, pZ), new Vector3(0.18f, 0.1f, 0.18f), new Color(1f, 0.85f, 0.4f), r);
                    AddPanel(root, new Vector3(0f, 1.35f, pZ), new Vector3(0.01f, 0.3f, 0.01f), Color.black, r);
                }

                // Bar counter across the room
                AddPanel(root, new Vector3(-0.2f, -1.5f, 1.4f), new Vector3(2.0f, 0.95f, 0.5f), new Color(0.3f, 0.18f, 0.1f), r);
                AddPanel(root, new Vector3(-0.2f, -0.5f, 1.4f), new Vector3(2.1f, 0.05f, 0.55f), new Color(0.15f, 0.1f, 0.08f), r);

                // Stools in front of the counter
                for (int st = 0; st < 3; st++) {
                    float sX = -0.8f + st * 0.6f;
                    AddPanel(root, new Vector3(sX, -1.5f, 0.9f), new Vector3(0.05f, 0.55f, 0.05f), Color.black, r);
                    AddPanel(root, new Vector3(sX, -0.92f, 0.9f), new Vector3(0.28f, 0.06f, 0.28f), new Color(0.45f, 0.12f, 0.12f), r);
                }

                // Blackboard Menu on back wall
                AddPanel(root, new Vector3(0f, 0.3f, 2.98f), new Vector3(1.6f, 0.8f, 0.02f), new Color(0.1f, 0.1f, 0.1f), r);
                AddPanel(root, new Vector3(0f, 0.3f, 2.97f), new Vector3(1.66f, 0.86f, 0.01f), new Color(0.45f, 0.35f, 0.25f), r);
                // Chalk writing lines
                AddPanel(root, new Vector3(-0.5f, 0.45f, 2.96f), new Vector3(0.3f, 0.02f, 0.01f), Color.white, r);
                AddPanel(root, new Vector3(-0.4f, 0.35f, 2.96f), new Vector3(0.4f, 0.02f, 0.01f), Color.white, r);
                AddPanel(root, new Vector3(0.3f, 0.4f, 2.96f), new Vector3(0.25f, 0.02f, 0.01f), Color.white, r);
                AddPanel(root, new Vector3(0.4f, 0.3f, 2.96f), new Vector3(0.35f, 0.02f, 0.01f), Color.white, r);
            }
            else {
                // Fallback default simple room
                AddPanel(root, new Vector3(0f, 1.45f, 1.6f), new Vector3(1.1f, 0.02f, 0.5f), new Color(1f, 0.97f, 0.85f), r);  // light panel
                int furniture = 2 + r.Next(0, 3);
                for (int f = 0; f < furniture; f++) {
                    Vector3 size = new Vector3(0.5f + (float)r.NextDouble() * 1.1f, 0.4f + (float)r.NextDouble() * 1.0f, 0.4f + (float)r.NextDouble() * 0.6f);
                    Vector3 pos = new Vector3(-1.2f + (float)r.NextDouble() * 2.4f, -1.5f + size.y * .5f, 1.0f + (float)r.NextDouble() * 1.7f);
                    Color tint = Color.HSVToRGB((float)r.NextDouble(), 0.25f + (float)r.NextDouble() * 0.4f, 0.3f + (float)r.NextDouble() * 0.4f);
                    AddPanel(root, pos, size, tint, r);
                }
            }

            return root;

        }

        static void AddDesk(GameObject root, Vector3 pos, float w, float h, float d, Color color, System.Random r) {
            // Desk top
            AddPanel(root, new Vector3(pos.x, pos.y + h - 0.025f, pos.z), new Vector3(w, 0.05f, d), color, r);
            // Left leg/drawer
            AddPanel(root, new Vector3(pos.x - w * 0.4f, pos.y + h * 0.5f, pos.z), new Vector3(0.1f, h, d * 0.8f), color * 0.8f, r);
            // Right leg/drawer
            AddPanel(root, new Vector3(pos.x + w * 0.4f, pos.y + h * 0.5f, pos.z), new Vector3(0.1f, h, d * 0.8f), color * 0.8f, r);
        }

        static void AddChair(GameObject root, Vector3 pos, Color seatColor, Color backColor, System.Random r) {
            // Seat
            AddPanel(root, new Vector3(pos.x, pos.y + 0.45f, pos.z), new Vector3(0.4f, 0.05f, 0.4f), seatColor, r);
            // Backrest
            AddPanel(root, new Vector3(pos.x, pos.y + 0.75f, pos.z + 0.18f), new Vector3(0.4f, 0.5f, 0.05f), backColor, r);
            // Base stand
            AddPanel(root, new Vector3(pos.x, pos.y + 0.22f, pos.z), new Vector3(0.08f, 0.45f, 0.08f), Color.black, r);
        }

        static void AddMonitor(GameObject root, Vector3 pos, Color screenColor, System.Random r) {
            // Base stand
            AddPanel(root, new Vector3(pos.x, pos.y + 0.05f, pos.z), new Vector3(0.15f, 0.1f, 0.15f), Color.black, r);
            // Screen back
            AddPanel(root, new Vector3(pos.x, pos.y + 0.3f, pos.z), new Vector3(0.55f, 0.35f, 0.04f), new Color(0.15f, 0.15f, 0.15f), r);
            // Glowing screen face
            AddPanel(root, new Vector3(pos.x, pos.y + 0.3f, pos.z - 0.025f), new Vector3(0.5f, 0.3f, 0.01f), screenColor, r);
        }

        static void AddBookcase(GameObject root, Vector3 pos, float w, float h, float d, Color woodColor, System.Random r) {
            // Main frame back
            AddPanel(root, new Vector3(pos.x, pos.y + h * 0.5f, pos.z), new Vector3(w, h, 0.02f), woodColor * 0.7f, r);
            // Outer walls
            AddPanel(root, new Vector3(pos.x - w * 0.5f, pos.y + h * 0.5f, pos.z - d * 0.5f), new Vector3(0.02f, h, d), woodColor, r);
            AddPanel(root, new Vector3(pos.x + w * 0.5f, pos.y + h * 0.5f, pos.z - d * 0.5f), new Vector3(0.02f, h, d), woodColor, r);
            AddPanel(root, new Vector3(pos.x, pos.y + h - 0.01f, pos.z - d * 0.5f), new Vector3(w, 0.02f, d), woodColor, r);

            // Horizontal shelves
            int numShelves = 4;
            for (int s = 1; s < numShelves; s++) {
                float shY = h * ((float)s / numShelves);
                AddPanel(root, new Vector3(pos.x, pos.y + shY, pos.z - d * 0.5f), new Vector3(w - 0.04f, 0.02f, d - 0.02f), woodColor * 0.85f, r);

                // Add books
                int numBooks = 3 + r.Next(0, 5);
                for (int b = 0; b < numBooks; b++) {
                    float bx = pos.x - w * 0.4f + b * (w * 0.8f / numBooks);
                    float bw = 0.04f + (float)r.NextDouble() * 0.05f;
                    float bh = 0.2f + (float)r.NextDouble() * 0.15f;
                    float bd = d * 0.7f;
                    Color bookColor = Color.HSVToRGB((float)r.NextDouble(), 0.5f + (float)r.NextDouble() * 0.4f, 0.4f + (float)r.NextDouble() * 0.5f);
                    AddPanel(root, new Vector3(bx, pos.y + shY + bh * 0.5f, pos.z - d * 0.5f), new Vector3(bw, bh, bd), bookColor, r);
                }
            }
        }

        static void AddSofa(GameObject root, Vector3 pos, float w, float h, float d, Color sofaColor, System.Random r) {
            // Bottom cushion base
            AddPanel(root, new Vector3(pos.x, pos.y + 0.2f, pos.z), new Vector3(w, 0.3f, d), sofaColor, r);
            // Backrest
            AddPanel(root, new Vector3(pos.x, pos.y + 0.6f, pos.z + d * 0.4f), new Vector3(w, 0.8f, 0.18f), sofaColor * 0.9f, r);
            // Armrests
            AddPanel(root, new Vector3(pos.x - w * 0.46f, pos.y + 0.35f, pos.z), new Vector3(0.15f, 0.5f, d), sofaColor * 0.85f, r);
            AddPanel(root, new Vector3(pos.x + w * 0.46f, pos.y + 0.35f, pos.z), new Vector3(0.15f, 0.5f, d), sofaColor * 0.85f, r);

            // Add pillows
            Color pillowColor = Color.HSVToRGB((float)r.NextDouble(), 0.5f + (float)r.NextDouble() * 0.3f, 0.6f + (float)r.NextDouble() * 0.3f);
            AddPanel(root, new Vector3(pos.x - w * 0.25f, pos.y + 0.4f, pos.z + d * 0.25f), new Vector3(0.3f, 0.3f, 0.1f), pillowColor, r);
            AddPanel(root, new Vector3(pos.x + w * 0.25f, pos.y + 0.4f, pos.z + d * 0.25f), new Vector3(0.3f, 0.3f, 0.1f), pillowColor, r);
        }

        static void AddBed(GameObject root, Vector3 pos, Color bedColor, System.Random r) {
            // Bed frame
            AddPanel(root, new Vector3(pos.x, pos.y + 0.2f, pos.z), new Vector3(1.4f, 0.4f, 2.0f), new Color(0.3f, 0.2f, 0.15f), r);
            // Mattress
            AddPanel(root, new Vector3(pos.x, pos.y + 0.45f, pos.z), new Vector3(1.3f, 0.2f, 1.9f), Color.white, r);
            // Blanket
            AddPanel(root, new Vector3(pos.x, pos.y + 0.46f, pos.z + 0.2f), new Vector3(1.32f, 0.2f, 1.3f), bedColor, r);
            // Pillow
            AddPanel(root, new Vector3(pos.x, pos.y + 0.58f, pos.z - 0.7f), new Vector3(0.9f, 0.08f, 0.35f), Color.white * 0.95f, r);
        }

        static void AddServerRack(GameObject root, Vector3 pos, float h, System.Random r) {
            // Main rack frame
            AddPanel(root, pos + new Vector3(0, h * 0.5f, 0), new Vector3(0.6f, h, 0.6f), new Color(0.1f, 0.1f, 0.1f), r);
            // Slots
            int slots = 6;
            for (int s = 0; s < slots; s++) {
                float sy = h * ((float)s / slots) + (h / slots) * 0.5f;
                AddPanel(root, pos + new Vector3(0, sy, 0.01f), new Vector3(0.54f, h / slots * 0.7f, 0.58f), new Color(0.2f, 0.2f, 0.2f), r);
                // Glowing LEDs
                Color ledColor = (r.NextDouble() < 0.7) ? Color.green : ((r.NextDouble() < 0.5) ? Color.red : Color.cyan);
                if (r.NextDouble() < 0.15) ledColor = new Color(0.9f, 0.9f, 0.1f); // yellow
                AddPanel(root, pos + new Vector3(-0.2f + (float)r.NextDouble() * 0.1f, sy + 0.05f, -0.29f), new Vector3(0.04f, 0.04f, 0.01f), ledColor, r);
                if (r.NextDouble() < 0.7) {
                    Color ledColor2 = (r.NextDouble() < 0.5) ? Color.green : Color.red;
                    AddPanel(root, pos + new Vector3(0.1f + (float)r.NextDouble() * 0.1f, sy + 0.05f, -0.29f), new Vector3(0.04f, 0.04f, 0.01f), ledColor2, r);
                }
            }
        }

        static void AddPanel(GameObject root, Vector3 localPos, Vector3 size, Color color, System.Random r) {

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "panel";
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            Object.DestroyImmediate(go.GetComponent<Collider>());

            Shader unlit = Shader.Find("Unlit/Color");

            if (BCG_BuildingMeshBuilder.DetectPipeline() == BCG_Pipeline.URP)
                unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? unlit;

            Material mat = new Material(unlit);
            mat.color = color;

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);

            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

        }

    }

}
