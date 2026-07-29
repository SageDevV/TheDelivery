# BCG Building Generator — Changelog

All notable changes to Urban Building Generator are listed here, newest first.

---

## Version 2.5.0

### New Features

- **The generator window is now a City Pipeline** — the old **Build | Manage** split is replaced by
  four ordered stages that follow how a city is actually made: **1 Plan** (City Grid · Zones ·
  Paths) → **2 Build** (Single · Street · Districts · Greybox) → **3 Dress** (Mood · Furniture ·
  Probes) → **4 Ship** (Health · Finalize). A stage strip picks the stage, a sub-tab strip picks the
  pane, and both are remembered per-user. Every pane is built up front, so switching never loses
  what you had typed. Every band of the new chrome — title row, output row, City Ledger, identity
  strip, and both strips — holds its height, so when the window is squeezed to its 418 × 480 minimum
  the squeeze lands on the pane, which scrolls. Nothing overlaps and no control goes out of reach.
- **City Ledger** — one status band under the header, visible in *every* stage: scene stats
  (`N bldgs · X tris · ~N draws (pre-batch) · N zones`), the pipeline / material-health badge with an
  inline **[Fix]** button that appears exactly when the materials need rebuilding, and the populate
  job's progress bar with the window's only **Cancel**. Result readouts ("12 built · 0 skipped")
  arrive as a transient toast on the same band and clear themselves after five seconds.
- **Identity strip** — select a generated building and its recipe appears directly under the ledger:
  palette swatch, archetype, cells × floors (or footprint in metres for a renamed object), seed and
  triangle count. **Copy** puts the seed on the clipboard; **Edit in Building** loads the recipe into
  Build ▸ Single. The strip is hidden whenever the selection is not a generated building.
- **Build ▸ Districts is now card-based** — every selected district zone gets its own card with
  Preset, Seed, Edge Margin, Row Gap, **Select output (n)** and **Clear output**, each edit written
  straight to the `BCG_BuildingZone` component as one Undo step. The window-level defaults that seed
  *new* zones moved to **Plan ▸ Zones ▸ Defaults for new zones**, alongside a list of every zone in
  the scene and the District Presets row.
- **Build ▸ Greybox** — greybox replacement is now a browsable pane instead of a menu-only command,
  and from there it honours the pane's own **Where** and **Generation Settings** (detail, props,
  extras, lit signage, LODs, lightmap UVs, Save As Prefab Assets, obstacle layers) rather than the
  quick-blockout defaults. The standalone menu item is unchanged. The four rows that genuinely do not
  apply to a greybox run (Snap To Ground, Ground Layers, Mesh Variety, Reuse Existing Assets) now say
  so on the pane instead of silently doing nothing.
- **Ship ▸ Finalize — a pre-ship checklist** — six ordered rows, each with a live count read from
  the open scene: Bake Lightmap UVs, LOD coverage, Light Probes, Optimize City (marked *run last*),
  Clean Unused and Regenerate All. Rows 1–4 show ✓/○ and the pinned bar mirrors the tally as
  `Ship checks: k/4`. **Destroy All Generated…** is fenced off below in its own danger zone. The
  orphan scan behind Clean Unused reads every scene in the project, so it runs only when you press
  **[Scan]** — never on a timer and never on window open.
- **Command search (`⌕`)** — a floating palette listing every generator command. Type to filter
  (`fixmat` finds *Fix Materials*), Enter runs the top hit — and each row shows **which pane the
  command lives on**, so it doubles as a "where did that go?" reference.
- **Gear menu (`⚙`)** — window preferences and the two utilities with no pane of their own:
  Frame on generate, Select All Generated, Street Furniture As Separate Props, Open Manual, About.
- **Foundation-skirt health check + one-click fix** — the Ship ▸ Health dashboard now watches foundation
  skirts. A skirt that lost its mesh or material (scene-only skirt meshes can be stripped by
  certain undo/prefab round-trips) is always flagged; with **Snap To Ground** on, every skirtless
  building's footprint is ground-probed and flagged **Missing foundation skirt** where the terrain
  needs one — a hand-moved building on a slope, or a city generated before basement mode existed.
  The fix row gains **Skirts (n)**: each flagged building gets a fresh ground probe, a damaged
  shell is replaced, a missing skirt attached, and the base height re-derived exactly as Snap To
  Ground would place it there (basement mode on steep slopes). One Undo step; buildings renamed
  away from the generator's naming are skipped with a Console warning. Probing over collider-less
  display ground reuses one candidate scan for the whole pass, so the dashboard stays responsive
  in large cities.

### Changed

- **Fake Interiors and the Night Lights dial moved to Dress ▸ Mood.** Both are *global material*
  states, not per-building options, so they no longer repeat inside every pane's Generation Settings
  foldout (which is now Geometry + Saving only; its header summary drops the Interiors fragment).
  Flipping Fake Interiors on that pane rebuilds the shared facade materials immediately — the extra
  "now click Fix Materials" step is gone. Each Generation Settings foldout ends with a quiet
  `Interiors & Night Lights → Dress ▸ Mood` pointer.
- **The Tools ▾ catch-all menu is gone.** Every command it held has a browsable home, and the four
  with no natural pane are mirrored as native menu items so they work with the window closed.
  Nothing was dropped, and the job-gating rule is unchanged (Fix Materials and Select All Generated
  stay live during a populate job; everything else is greyed).

  | Old **Tools ▾** entry | Where it lives now |
  |---|---|
  | Fix Materials (Active Pipeline) | City Ledger **[Fix]** · **Dress ▸ Mood** primary ("Apply Materials") · menu item |
  | Regenerate All | **Ship ▸ Finalize** row 6 · menu item **Regenerate All…** |
  | Regenerate Roads | **Plan ▸ City Grid ▸ Regenerate Roads** |
  | Bake Lightmap UVs… | **Ship ▸ Finalize** row 1 |
  | Select All Generated | Gear menu `⚙` · menu item |
  | Clean Unused… | **Ship ▸ Finalize** row 5 · menu item **Clean Unused…** |
  | Frame on generate | Gear menu `⚙` |
  | Destroy All Generated… | **Ship ▸ Finalize** ▸ danger zone |
  | City Tools ▸ Generate / Remove Street Furniture | **Dress ▸ Furniture** · menu items |
  | City Tools ▸ Generate / Remove Light Probes | **Dress ▸ Probes** (Generate also on **Ship ▸ Finalize** row 3) · menu items |
  | City Tools ▸ Optimize City / De-Combine City | **Ship ▸ Finalize** row 4 · menu items |
  | City Tools ▸ Replace Selected Greyboxes | **Build ▸ Greybox** · menu item |

- **The Manage zone is now Ship ▸ Health** — the same scene inventory, filters, health flags, bulk
  actions and fix row, in the audit stage where it belongs. Its Materials panel moved to
  **Dress ▸ Mood**; the material-health badge moved to the City Ledger.
- **Fix Materials moved off the Materials panel.** It used to be a button inside that panel plus a
  **Tools ▾** entry. It is now the City Ledger's contextual **[Fix]** — right beside the badge that
  tells you to run it — and the **Dress ▸ Mood** primary **Apply Materials**, and it gained a native
  menu item so it also works with the window closed. Same handler behind all three, and it still
  stays live while a populate job runs.
- **Upgrade path.** The first time the pipeline window opens on an install that used the two-zone
  layout, the retired `BCG.BuildingGen.WindowZone` key is read **once** to pick a starting stage
  (Manage → 4 Ship, Build → 2 Build) and the result is stored under the new `BCG.BuildingGen.Stage`.
  The old key is never written and never deleted, so rolling back to an older build still finds the
  zone you last used. A fresh install starts at **1 Plan**. Every other preference keeps its existing
  key and value.

### Fixed

- **The scene-inventory list sizes itself properly now.** Its height used to be measured by hand
  from layout events and floored at 200 px, which on a short window pushed the fix row, the
  bulk-action row and the Add-ons panel below the fold — reachable only by scrolling the whole
  window body. The list is plain flexbox now: it fills a tall dock and, on a short one, yields all
  the way, because it is the one thing on that pane that can give space back. So at the 418 × 480
  minimum the fixers, **Select flagged / Select shown / Isolate / Delete shown** and the Add-ons
  **Import** button are all still there, with a shorter (scrollable) list above them.
- **The scene-inventory filter row no longer wraps onto a second line.** The search box asked for its
  full natural width before flexing, which at narrow widths pushed the **Type** and **Palette**
  filters and **Refresh** down a row and cost the list about a row of its own height. All four now
  share one line, the search box taking whatever is left.
- **No more second Cancel button.** The zone pane used to grow its own "Populating zones… [Cancel]"
  row while a job ran. Job status is now one thing in one place — the City Ledger's progress bar and
  its single Cancel, visible from every stage.

- **Baked lighting did nothing on generated buildings** — buildings stayed flat grey with no charts
  in the Scene view's **Baked Lightmap** mode and picked up no baked light at all, no matter how
  often **Bake Lightmap UVs** was run. The unwrap itself was never the problem. Version 2.3.0 added
  a vertex-buffer compression pass that ran on every generated mesh, and Unity's lightmapper
  **refuses** any mesh whose vertex data is not in the standard full-precision layout — it drops the
  building from the bake with *"Invalid Mesh was removed from light baking input due to failure
  code: 'Invalid format for channel(s) in Mesh'"*. Every building with lightmap UVs was therefore
  thrown out of every bake. (If you watched closely, the correct charts appeared for a single frame
  between the unwrap and the compression.) A mesh that carries lightmap UVs is now left uncompressed
  so the lightmapper accepts it. Meshes **without** lightmap UVs — the default, and how city filler
  is normally generated — are still compressed exactly as before, so the download-size win is
  unchanged for them; only buildings you explicitly bake for GI give it up, which is the correct
  trade for a bake that actually works.
- **Buildings generated by 2.3.0 repair themselves** — their meshes carry a lightmap UV set the
  lightmapper cannot use, which previously counted as "already unwrapped" and was skipped forever.
  Such a mesh is now reported as *missing* its lightmap UVs, so the everyday **Bake Missing** button
  re-solves it — no need to reach for **Renew All** or regenerate the library. Buildings generated
  before 2.3.0 were never affected.
- **LOD1 and LOD2 now receive lightmap UVs** — both generation and the post-hoc action previously
  touched only the building root, leaving the lower LOD meshes UV2-less and outside baked GI. The
  complete LOD chain is now unwrapped and marked Contribute GI when baking is requested; the prefab
  reuse gate rejects an old root-only/partial bake so it self-heals instead of being reused forever.
- **Foundation skirts and basement walls are baked with their building** — a skirt was always
  generated with Contribute GI cleared, so on a baked city built over terrain every foundation ring
  and basement wall stayed outside the lightmap and read as a flat, unlit band where the building
  above it was fully lit. A skirt now follows its building's baked-GI intent: it gets its own
  lightmap UV set and contributes to the bake whenever the building does, and stays out of it
  whenever the building does.
- **The post-hoc bake now covers the complete generated hierarchy** — foundation skirts and
  optimized-city chunks are included along with LOD0/1/2. Selecting an LOD/skirt/chunk resolves its
  owning generated root, and a scene-wide pass no longer double-processes the disabled sources
  behind an optimized city.
- **Failed or oversized unwraps no longer produce false GI state** — Contribute GI is enabled only
  after that renderer's mesh has a lightmapper-safe UV2. The unwrap path temporarily widens UInt16
  meshes because chart splitting can push them beyond 65,535 vertices, then narrows them again when
  possible; an actual failure leaves GI off and reports the mesh in the Console. Generated roads use
  the same guarded path, so a road surface whose unwrap failed is no longer flagged for a bake it
  cannot take part in.
- **Bake Lightmap UVs no longer saves unrelated dirty assets** — persistent UV changes are flushed
  one targeted mesh at a time instead of calling the project-wide `AssetDatabase.SaveAssets`.
- **Optimize City produces valid combined lightmap charts** — concatenating source UV2 made charts
  overlap, and a skirt could accidentally clear GI for a whole chunk. GI-enabled combined chunks now
  receive a fresh unwrap over the final mesh, while same-material GI-on/GI-off sources are split so
  both states are preserved. Existing combined cities can also be repaired by Bake Lightmap UVs.

### Improved

- **The documentation is rewritten around tasks instead of features.** The single 1,800-line user
  guide is replaced by a set of short, task-first documents you can actually read end to end:
  Getting Started, How It Works, Creating a Single Building, Creating Many Buildings, Cities and
  Districts, Paths and Roads, Customising Buildings, Materials and Lighting, Optimising, Finishing
  and Shipping, Troubleshooting and FAQ, and a Reference for lookups. `BuildingGen_UserGuide.md`
  is now a landing page with an **"I want to…"** table that routes straight to the right page, so
  existing links and the Welcome window's *User Guide* button keep working. The engine internals
  that used to sit in the reader's path — the seed contract, mesh name tags, the runtime API,
  package contents — moved into the Reference document rather than being deleted. Nine editor
  screenshots and two diagrams were added, and every default, range and menu path was re-verified
  against the shipping source.
- **Documentation accuracy fixes found during that rewrite** — the LOD0 transition is documented as
  **0.10** screen height (it was changed from 0.60 to stop visible popping, but the guide still
  said 60%); the Detailed-tier vertex budgets now match the regression pins (Tower 7×5 F9 is
  **5,764**, not 6,044; House 4×3 F2 Standard is **86**, not ~470); the `BCG_BuildingZone` field
  table gained the four fields it was missing (`snapToGround`, `groundLayers`, `detail`,
  `facadeExtras`); road markings are described as crossings, dashed centreline and edge lines
  (there are no stop lines); and Getting Started now says the large playable City demo arrives via
  **Ship ▸ Health ▸ Add-ons** rather than being present on import.
- **Bake Lightmap UVs now asks first, and can renew existing UVs** — the **Ship ▸ Finalize** row-1
  action (previously "Bake UVs (Existing)", a name that read as the opposite of what it did) now
  opens a dialog before
  writing anything. It reports the real workload — how many **unique meshes** lack a lightmap UV set
  versus already have one, which is what costs time, not the building count — and offers **Bake
  Missing** (unwraps only meshes without a UV set, the old behaviour), **Renew All** (also re-unwraps
  meshes that already have one, discarding the current set) or **Cancel**. Renewing is the missing
  path for a changed lightmap resolution or a hand-edited mesh, which previously meant regenerating
  the buildings outright. Workload counts include LODs, foundation skirts, and optimized chunks.
  When every targeted mesh is already unwrapped, only Renew and Cancel are offered. Mesh UV writes
  still cannot be undone — which is exactly why the action now confirms.

### Roadmap

Two ideas from the City Pipeline design are deliberately **not** in this release; both are additive
and neither changes the stage layout:

- **Draw a zone by dragging in the Scene view** — Plan ▸ Zones would gain a drag-rectangle tool that
  drops a correctly-sized `BCG_BuildingZone` where you drew it. It needs a custom `EditorTool` to do
  properly, so it was held back rather than bolted on.
- **A Scene-view overlay** — a small always-available panel with the current stage's primary action,
  for working without the window docked.

---

## Version 2.3.0

### New Features

- **Replace Greyboxes With Buildings** — block out a skyline with plain boxes, select them, one
  click swaps every box for a generated building matching its footprint, height, base and
  (90°-snapped) yaw. Seeds are a stable hash of each box's name, so a re-blocked scene reproduces
  the same buildings; placement is anti-clip-guarded; one Undo restores the boxes.
- **Generate Light Probes** — drops a capped `LightProbeGroup` grid over the generated city
  (street + mid-rise layers everywhere, rooftop probes only where buildings stand) so dynamic
  objects pick up baked GI. A density prompt asks for quality first — Low / Medium / High / Ultra
  presets or a custom spacing (floor 1 m), with a live probe-count estimate for your city. The
  spacing you ask for is now honoured **exactly** whenever it fits the **probe budget** (a new
  field, default 4096, up to 65536) — previously a fixed 4096 cap silently widened every request on
  a large city. A new **Coverage** option spends the budget only near roads and buildings instead
  of over the whole bounding area, which is what makes a tight spacing affordable at city scale;
  when the budget does force a widen, the prompt reports how many probes the requested spacing
  would actually have needed. Probes are footprint-aware: a position landing inside a building is pushed out to the
  nearest open air beside the facade (reported in the console) instead of baking black inside solid
  geometry. The new group is selected in the scene afterwards. Regenerate replaces only its own
  group — a probe group you authored is never touched.
- **Optimize City (Combine Meshes)** — a reversible finalize pass that merges generated buildings
  into a few material-bucketed, 65k-vertex-capped district meshes (renderer/GameObject count
  collapse for mobile). Sources are disabled, never destroyed; **De-Combine City** restores them
  exactly. Re-bake lighting after combining if the city uses baked GI.
- **Generate Street Furniture** — lamps, benches, bus shelters and trees along both sidewalks of
  every road network, with junction clearance, per-edge deterministic layout and static colliders,
  combined into a few draw calls per network. Lamp heads and shelter glass ride the facade atlas's
  window bands, so lamp heads glow at night through the existing day/night material swap — no real
  Lights. Trees appear only on sidewalks ≥ 2 m and use two new solid-colour pipeline-aware
  materials. To guarantee the glow on every palette, the night emission atlas gains a "beacon"
  pane — one always-lit window pane (cell 7 of the office-lit band) in all four variants that
  lamp heads and signage sample; night scenes show one extra lit pane per office-lit tile, the
  day look is untouched.
- **Street Furniture — Separate Props mode** — opt-in `Street Furniture As Separate Props` menu
  toggle: props become instances of 4 user-editable, Rigidbody-ready prefabs
  (`BCG_Furniture_{Lamp,Bench,Shelter,Tree}` with convex colliders, created once in the configured
  output folder) so developers can make lamps/benches/shelters/trees crashable. A prefab edit —
  e.g. adding a Rigidbody — survives every regenerate and applies to every instance; the layout is
  identical to combined mode, and combined chunks stay the default.
- **Lit signage (seed-contract step 7)** — optional `litSigns` generation layer: up to two vertical
  corner sign strips on the upper shaft of 10+-floor Towers and a lit fascia strip over Shop
  storefronts, UV-mapped into the lit-window band so they glow at night via the existing `_Night`
  emission path. Off by default — output stays byte-identical to previous versions; signs-on mesh
  assets carry a `_G` name tag and the asset-reuse gate rebuilds honestly on a toggle flip.

All five tools live in the generator window's **Tools ▾ ▸ City Tools** submenu and under
**Tools ▸ BoneCracker Games ▸ Building Generator**. Lit signage has a **Lit Signage** toggle in
Generation Settings ▸ Geometry (new EditorPref `BCG.BuildingGen.LitSigns`), default ON for new
generations — existing scenes/assets are untouched, and with it on a given seed's tall-Tower/Shop
output differs from 2.1 (the rooftop-props release-note precedent). See the user guide's new §25.

### Improved

- **Ground snapping works without colliders, and no longer buries ground floors** — Snap To Ground
  had two sharp edges on real terrain. It now tries physics per probe point and falls back to
  raycasting the **visible meshes** on Ground Layers, so display-only ground (an imported city mesh,
  a landscape whose collision is added later) snaps like any collider surface. And where the ground
  under a footprint is steeper than 5°, the base now rises to the **highest** probe hit instead of
  the lowest — the foundation skirt grows into a solid **basement wall** filling the cut below, so
  ground-floor windows and doors clear the hillside instead of being swallowed by it. The
  skirt/basement is collided per ground-touching massing block, so the exposed downhill band is not
  drive-through scenery. Flat ground behaves exactly as before, and a zone fill that finds no ground
  under some plots now warns in the console instead of silently leaving those buildings flat.
- **Smaller builds — 16-bit mesh indices** — generated building and road meshes now use the
  narrowest index format their vertex count allows, instead of always reserving 32-bit indices.
  Ordinary city filler sits far below the 65,535-vertex threshold, so its index buffer halves:
  across a 1,758-mesh city library that is **2.3 MB less mesh data** (4.7 MB → 2.3 MB of index
  data) with **byte-identical geometry** — vertex data, vertex counts and the seed contract are
  all unchanged. Very large Detailed buildings that genuinely cross the threshold still switch to
  32-bit automatically. Existing assets keep their old format until you run **Tools ▾ ▸
  Regenerate All**.
- **Smaller builds — compressed window mask** — `BCG_Facade_WindowMask` was set to import
  uncompressed, which made it the single largest asset in a build at 8 MB. It is a pure
  black-and-white mask, so block compression reproduces it exactly (verified: zero error against
  the source image). It now imports compressed at **1.4 MB on desktop / 2.4 MB on mobile and
  WebGL**, saving roughly **5.6–6.7 MB per build** depending on platform, with no visual change
  to the fake-interiors glass.
- **Smaller builds — specular maps halved on mobile and web** — the four facade specular/
  smoothness atlases imported at full 1024×2048 on every platform (9.5 MB — the largest remaining
  texture cost after the window mask). Halving them visibly softens thin balcony railings, window
  frames and mullions, so **desktop keeps full resolution**; Android, iOS and WebGL now import them
  at 512×1024, saving **7.1 MB per build** on exactly the platforms where download size matters and
  the softening is not resolvable on screen.
- **Smaller builds — packed mesh vertex format** — generated building meshes now store normals and
  tangents as signed bytes and both UV sets as half-floats, halving the vertex stride (56 → 28 bytes
  with lightmap UVs, 48 → 24 without). Positions keep full 32-bit precision, so nothing shifts.
  Across a 1,758-mesh city library the mesh payload drops **44.5 MB → 23.4 MB (−50%)** with no change
  to vertex counts, geometry or the seed contract — the same seed still produces the same building.
  Existing assets keep the old format until you run **Tools ▾ ▸ Regenerate All**.

---

## Version 2.1.0

### New Features

- **Cinematic demo intro** — the demo city now opens with a skippable ~75 s guided feature
  tour: six authored camera shots with on-screen caption cards covering live runtime
  generation (buildings grow out of an empty plot via `BCG_RuntimeBuildingFactory`), the road
  network, baked lighting, night emission, and fake interiors. The tour opens and closes with
  a soft fade to black and hands control back under the fade — and fading is selectable per
  shot (Fade In / Fade Out on each shot), so any cut can become a dip-to-black. Any key skips straight to the
  explorable fly camera — except the very first run on a machine, which plays in full
  (**First Run Cant Skip**, default on, PlayerPrefs-tracked) — and the scene is always handed back exactly as authored (the night
  beats are mid-tour only — daylight is restored by the finale); **C** replays the tour. A
  **Playback Speed** multiplier on the director scales the whole tour (shots, captions, and
  the generation timelapse together). Built with zero new dependencies — no Cinemachine or
  Timeline required.
- **Configurable output folder** — the `Output:` line in the window header is now interactive:
  click it (or the `…` button) to choose any folder inside `Assets` for generated mesh/prefab
  assets; `↺` resets to the default `Generated/` location. Stored per-user, per-project.
  Already-generated assets are never moved; **Clean Unused…** and **Regenerate All** scan both
  the configured and the default folder, and the unused-asset scan now only considers assets
  matching the generator's own naming (`BCG_Building*`), so your own assets in a chosen folder
  are never offered for deletion.

---

## Version 2.0.0

### New Features

- **Drivable grid road networks** — turn on **Create Roads** (Populate Zones ▸ City Blocks) or
  **Generate road surface** (Street ▸ Straight) and every street/avenue gap becomes real road
  geometry: an asphalt / gutter / curb / sidewalk ribbon, square-return junction pads with
  dropped-curb crosswalk sockets, and a separate markings layer (zebra crosswalks, dash
  centerline, edge lines) with shadows off. One drivable `MeshCollider` shares the exact mesh the
  renderer draws, and curb faces are beveled (~33° at the default curb height) so vehicles mount
  the sidewalk smoothly instead of striking a vertical wall. See user guide
  [§23, Roads](BuildingGen_UserGuide.md#23-roads).
- **Baked GI for roads** — with **Bake Lightmap UVs** on, generated road surfaces get a secondary
  UV set and contribute to baked lighting; off keeps roads dynamically lit as before.
- **`BCG_RoadNetwork` / `BCG_RoadMarker`** — new runtime components that make a road layout the
  source of truth for **Regenerate Roads** (Tools ▾ menu) and let the placement guard, ground
  snap, and Select All / Destroy All treat generated roads correctly.
- **Fix Materials (Active Pipeline)** now also rebinds the road surface material family
  (asphalt/markings atlas + a `_Night` variant) to the active pipeline.
- **Optional Road Constructor integration** — if Road Constructor (Pampel Games, sold separately)
  is imported, two extra UI rows appear automatically: routing City Blocks' grid through Road
  Constructor's spline roads, and **Populate Along RC Roads** to line any Road Constructor road
  already in the scene with a seeded row of buildings. Zero footprint if Road Constructor isn't
  installed. See [§24, Road Constructor integration](BuildingGen_UserGuide.md#24-road-constructor-integration).
- **Demo re-authored** — the demo city (also the WebGL build scene) now has 105 buildings across
  31 fully drivable road edges, baked GI covering both buildings and roads, and glowing lane
  markings in night mode.
- **Undo-safe road meshes + a Roads health fixer** — every road create/destroy path (generation,
  Regenerate Roads, Destroy All) tracks the scene-embedded road meshes through the same Undo step
  as the road objects, so undo/redo can never leave invisible roads or empty `BCG_Roads` shells
  behind, and a road mesh you have reused elsewhere is spared. The Manage dashboard's fix row
  gained **Roads (n)**: rebuilds networks whose road meshes went missing (from their
  `BCG_RoadNetwork` data) and deletes ownerless broken road objects.

### Determinism

- Road generation draws **zero values from any seeded stream** — the same seed produces the same
  city whether or not Create Roads is on, and toggling roads never reshuffles a single building
  (test-pinned byte-parity). Populate Along RC Roads is the one new seeded stream, since nothing
  before this release ever lined an RC-built road.

### Upgrade notes

- **City Blocks generates roads by default** (`Create Roads` defaults ON) — turn it off to restore
  the pre-2.0 empty-gap layout; building placement is unaffected either way.
- **Street mode's road surface toggle defaults OFF** and is byte-stable with 1.x until you turn it
  on; turning it on widens each row's setback by one sidewalk width per side so buildings clear the
  new sidewalk.
- New EditorPrefs: `BCG.BuildingGen.CreateRoads`, `BCG.BuildingGen.RoadSidewalkWidth`,
  `BCG.BuildingGen.StreetRoadSurface`, `BCG.BuildingGen.RoadBackend`.

### Limitations

- Flat ground only, 90°-grid square-return junctions only, straight streets only for the built-in
  road surface — free-form/curved/elevated roads are Road Constructor territory (see §24).

### Tests

- EditMode suite grew from 129 to 182 across the two road-network waves, the road undo-safety
  hardening, and the drivable curb bevel.

---

## Version 1.3.1

### New Features

- **Playable demo scene** — open `Demo/BuildingGen_Demo.unity` and press **Play** to explore the
  demo city in first person:
  - **Collision-aware fly camera** — WASD + mouse-look, Space/E and Ctrl/Q to fly up / down,
    Shift for speed. The camera slides along buildings and the ground instead of clipping
    through them, and stays inside the city bounds.
  - **Click to inspect** — clicking a building highlights it and opens an info card with its
    generated facts: archetype, variant, seed, footprint, height, and mesh vertex / triangle
    counts.
  - **Day / night toggle (N)** — swaps every facade to its `_Night` emission variant, dims the
    sun, and darkens the sky in one keypress.
  - **Controls overlay (H / F1)** — an on-screen legend so first-time users never guess.
  - Works out of the box under **both** the legacy Input Manager and the new Input System, in
    any render pipeline. The demo scripts ship in their own runtime-only assembly under
    `Demo/Scripts/` and never touch generation code. The same scene is the source of the
    playable WebGL demo on the store page / website.

### Fixed

- **Night emission now bakes into lightmaps** — facade materials silently opted out of baked
  global illumination, so glowing night windows lit nothing around them in a baked scene even
  though they rendered fine on screen. Facade materials are now flagged baked-emissive (run
  **Fix Materials** once in existing projects to upgrade them in place), and the Fake Interiors
  shader gained the Meta pass the lightmapper reads. Verified on Built-in and URP — baking with
  the `_Night` variants (or the Night Lights dial turned up) now pools warm window glow onto
  streets and neighbouring facades.
- **Detailed elaborations and Facade Extras stay on their walls** — on L-shaped and Setback
  massings, the Detailed tier's sills / mullions / balconies and the AC-unit / vent extras were
  laid out using the whole building's cell counts, so geometry could walk past the end of a
  narrower block's side wall and float in mid-air. Placement is now per massing block.
- **Disabled buildings no longer block new placement** — the anti-clip placement guard treated
  buildings on disabled GameObjects as obstacles, so new buildings would relocate around
  invisible scenery. Inactive buildings are now ignored when collecting footprints.
- **URP: facades now receive SSAO and appear in the Depth Normals prepass** — the fake-interiors
  facade shader gained a `DepthNormalsOnly` pass and the `_SCREEN_SPACE_OCCLUSION` variant. With
  a Screen Space Ambient Occlusion renderer feature, buildings previously never darkened and were
  missing from the camera normals texture.
- **URP: baked shadowmask / subtractive mixed lighting now respected** — lightmapped facades
  sampled no shadow mask, so in Shadowmask mode all baked shadows vanished beyond the realtime
  shadow distance. The forward pass now compiles the `SHADOWS_SHADOWMASK` /
  `LIGHTMAP_SHADOW_MIXING` variants and samples the mask like stock URP Lit.
- **Projects without URP no longer log shader errors** — Unity compiles every SubShader in a
  shader file regardless of the active pipeline, so importing into a project that doesn't have
  the URP package installed produced five "Couldn't open include file ... Core.hlsl" errors
  (one per pass of the fake-interiors shader's URP SubShader). That SubShader now declares a
  `PackageRequirements` dependency on URP, so Unity skips it entirely where URP is absent.
  Rendering was always correct (the Built-in SubShader was used either way) — the console
  errors are simply gone.

### Changed

- The unused per-vertex additional-lights variant (`_ADDITIONAL_LIGHTS_VERTEX`) is no longer
  compiled — the pass never consumed vertex lighting, so the variant only added build time.
  Per-pixel and Forward+ additional lights are unaffected.
- The facade shader's Forward+ keyword is now `_CLUSTER_LIGHT_LOOP` (the Unity 6000.1+ name)
  instead of the deprecated `_FORWARD_PLUS` alias — removes a shader-compile deprecation warning
  and lets URP's variant prefiltering strip unused Forward+ variants. Requires Unity 6000.1 or
  newer, which the package already exceeds.

---

## Version 1.3.0

### Improved

- **Full editor UI/UX polish pass** — a 24-point audit of the generator window was implemented
  end to end:
  - **Navigation reads at a glance** — the three switcher rows are now visually tiered
    (Build / Manage as a filled block, Single / Street / Zones as an underline, Straight /
    Along Path as a quiet pill), and foldouts join the theme (bold header, orange arrow — no
    more stock blue focus tint).
  - **The pinned Generate button explains itself** — when it is disabled, a note beside the
    status badge says why (`no zones selected`, `needs a Street Path with 2+ points`, or a
    running populate job). "Generate City" is now a distinct secondary button so each tab has
    exactly one primary action.
  - **Manage dashboard grew up** — the building list now fills the window height instead of a
    fixed box, group headers show clean names (`Zone Marker (1) · S83302` instead of raw
    container names), the filters are labeled (`Type:` / `Palette:`), a legend decodes the row
    columns, and only actionable fixer buttons are shown.
  - **Narrow-window fixes** — the Street tab no longer scrolls sideways (the Variant Mix row
    fits and wraps), hints wrap instead of clipping mid-sentence, and the Manage toolbar wraps
    at the 418 px minimum width.
  - **Clearer information** — the output folder is always visible under the window title, the
    Street tab shows the normalized archetype mix (`≈ Tower 28% · Shop 24% · …`), destructive
    buttons carry visible red weight, the Day / Dusk / Night presets read as presets rather
    than tabs, and the preset dropdown shows friendly names (**Downtown**, not
    `BCG_Preset_Downtown`).
  - **Welcome window matches the product theme** — the legacy blue header and nav highlight
    were retinted to the shared dark-and-orange look.
  - Per-tab "Reset to Defaults" rows were consolidated into one Reset on the tab strip; various
    alignment and readout polish (field-column grid, `·` separators).

### Fixed

- **Auto-Seed previews now update the Seed field** — previously the field could show a stale
  seed while the preview displayed the freshly rolled one, so the value about to be baked by
  Generate was ambiguous.

---

## Version 1.2.1

### Fixed

- **Generator window now fits 1920×1080 displays** — the window's minimum height was lowered so it
  fits on smaller and scaled (125–150% DPI) screens. The body scrolls and the pinned **Generate**
  bar stays reachable regardless of window height. No functional or generation changes.

---

## Version 1.2.0

### New Features

- **Fake Interiors** — windows now show parallax room interiors behind the glass, rendered
  entirely in-shader with no extra draw calls. Rooms sit *behind* the tinted glass with
  grazing-angle Fresnel reflections and per-window variety (some blinds-drawn, some clear),
  and light up at night. One global toggle in the Materials panel; works on Built-in and URP
  (HDRP gracefully falls back to stock glass).
- **Detailed detail tier** — a new "Detailed" quality level (alongside Simple / Standard) adds
  window sills, mullion bars, real balconies, cornices, corner pilasters, parapet coping,
  recessed storefronts, and House shutters / canopy / porch / chimney details. The same seed
  always produces the same building at every tier. Pairs with automatic LODs; keep Standard /
  Simple for mobile.
- **Facade Extras** — optional AC units and wall vents scattered across tower, shop, and
  apartment walls for extra rooftop-to-street realism. Toggleable; off by default on existing
  buildings.
- **Facade normal maps** — all facades now ship with normal maps bound automatically across
  Built-in, URP, and HDRP for richer surface lighting (no setup, no toggle).
- **Facade specular maps** — facades gained per-texel specular / smoothness maps, so glass and
  trim catch highlights realistically instead of reading flat. Applied automatically by
  *Fix Materials*.

### Improved

- **Rebuilt editor UI** — all three windows (generator, welcome, cleanup) were rebuilt on
  Unity's modern UI Toolkit with a shared dark theme. The generator now opens into two clear
  zones: **Build** (Single / Street / Zones) and **Manage** (scene dashboard + materials). A
  pinned action bar keeps the **Generate** button, a live material-health badge, and a
  **Tools ▾** menu (Regenerate All, Bake UVs, Clean Unused, and more) always in reach.
- **3-level LOD chain for Detailed buildings** — Detailed → Standard → Simple swaps by screen
  size so the heavy geometry only renders up close.
- **Detail-level labels clarified** — the middle tier is now labeled "Standard" (was "Full")
  for clearer guidance.

### Compatibility

- Fake Interiors, normal maps, and specular maps are all verified non-magenta on Built-in and
  URP Forward+; HDRP falls back cleanly to stock Lit materials.

### Note

- Enabling **Facade Extras** changes the generated clutter for a given seed compared with
  v1.1.0. Existing buildings deserialize with extras **off**, so nothing changes in your scenes
  unless you opt in.

---

## Version 1.1.0

- HDRP support (materials rebuild to `HDRP/Lit` via *Fix Materials*).
- Obstacle-aware placement, skyline height falloff, terrain snapping, and foundation skirts.
- District presets, city-block generator, street paths, and a click-to-stamp building brush.
- Scene inventory dashboard with per-building health flags and one-click fixers.
- Rooftop / storefront props, automatic LODs, mesh-library reuse, and async zone population.
- Runtime generation API (`BCG_BuildingMeshCore` + `BCG_RuntimeBuildingFactory`).
- Night-lights emission dial with Day / Night material variants.
- Orphaned-asset cleanup, post-hoc lightmap UV bake, and save-as-prefab / preview-in-scene.

---

## Version 1.0.0

- Initial release. Procedural city-filler building generator: four archetypes
  (Tower / Shop / Apartment / House), four massing models, seeded per-floor facade styles,
  one draw call per building via a shared strip-atlas.
- Pipeline-aware facade materials (Built-in and URP), EditMode test suite, demo city scene,
  user guide, and onboarding welcome window.
