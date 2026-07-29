# Paths and Roads

Two related things live here. **Paths** decide where a line of buildings goes when it is not a
straight segment. **Roads** are actual drivable geometry — asphalt, kerbs, pavements and painted
markings — that the tool can build into the gaps between your buildings.

They are independent: you can line a curved path with buildings and no road, or generate roads with
no path in sight.

## Following a path

A **street path** is a polyline you draw in the scene. Buildings walk along it by distance, facing
the carriageway, so you can line a bend, a racing line, or a road that came from another tool.

### Creating and shaping one

1. Go to **2 Build ▸ Street** and switch the toolbar from **Straight** to **Along Path**. (The
   same **Create Street Path** button is the primary action on **1 Plan ▸ Paths**, which also lists
   every path in the scene with whether it has been populated.)
2. Press **Create Street Path**. A three-point path drops at your scene-view pivot.
3. Shape it — drag the position handles in the Scene view, or edit the point list in the
   Inspector, where `+` inserts a point, `−` deletes one, and **Add Point** extends the tail.
   **Road Width** and **Both Sides** live on the path component itself, not the window.
4. Press **Generate Along Path**.

Buildings land under a parent named `BCG_StreetPathRow_{pathName}_{seed}`, and the path's gizmo
turns from cyan to green once it has been populated.

### Things worth knowing

- **Regenerating replaces** the path's previous output, unlike straight rows which stack. The path
  remembers what it produced.
- **A straight two-point path produces exactly the same buildings as the straight layout** for the
  same seed. They share one placement grammar, so switching between them is not a re-roll.
- **At sharp corners**, buildings that would overlap on the inside of the bend are relocated by the
  usual placement guard. The Console reports how many moved and how many were skipped.
- **Buildings inherit the height of the path point they sit near**, so keep path points on the
  ground — or turn on Snap To Ground and let the terrain decide.
- Every other setting applies as normal: props, LODs, mesh variety, obstacle layers, ground
  snapping.

Path layouts do **not** generate road surfaces — see [Limitations](#what-the-road-builder-does-not-do).

## Roads

Turn on **Create Roads** (on **1 Plan ▸ City Grid**, where it is on by default) or **Generate road
surface** (on **2 Build ▸ Street ▸ Straight**, off by default) and the gaps between your buildings
become real roads.

### What you get

- An **asphalt / gutter / kerb / pavement ribbon** down every street and avenue. The ribbon fills
  the whole gap; the carriageway is what remains after a pavement on each side, with a 0.3 m gutter
  band worked into each edge.
- **Junction pads** at every crossing, with dropped-kerb crossing sockets where each road meets the
  pad, so a pedestrian crossing reads correctly instead of stopping at a kerb.
- A **markings overlay** — zebra crossings at every junction, a fitted dashed centreline, and solid
  edge lines. Dashes are fitted to each segment's length so no half-dash ever abuts a junction. It
  is a separate renderer with shadows switched off, so the thin paint geometry never casts a shadow
  artefact.
- **One drivable `MeshCollider`** on the road surface, sharing the *exact same mesh instance* the
  renderer draws. Collision is bitwise identical to what you see; it is never a simplified proxy.

### Kerbs you can drive onto

The kerb face is **bevelled**, not a vertical wall: it climbs its 0.13 m rise over a 0.2 m lateral
run, which works out to roughly a 33° ramp, while still leaving a 0.1 m flat kerb top.

This is a deliberate vehicle-physics decision. Because the collider shares the render mesh, a
square 90° kerb would be a wall that wheel colliders cannot climb and that catches a car's body at
shallow approach angles. The ramp lets a vehicle mount the pavement the way it does in a real
driving game.

### Settings

| Setting | Where | Default | Notes |
|---|---|---|---|
| Create Roads | Plan ▸ City Grid | on | Builds the road network for the whole grid. Off leaves the gaps empty |
| Sidewalk Width (m) | Plan ▸ City Grid | 2.5 (range 1 – 4) | Per side, inside the ribbon. The carriageway is whatever is left between the kerbs |
| Generate road surface | Build ▸ Street ▸ Straight | off | Emits the same ribbon down a street row's carriageway |

**Road Width keeps its meaning** on the Street pane — it is still the clear carriageway. Turning
road generation on widens each row's setback by one pavement width per side so buildings do not end
up standing on the new pavement.

### Layering (why nothing z-fights)

City-scale scenes are where coplanar surfaces flicker, so the road builder separates everything
deliberately: the ground plane sits at −0.02 m, asphalt and junction pads at +0.02 m, kerb tops at
+0.15 m (asphalt plus the 0.13 m kerb rise), and painted markings float 15 mm above the asphalt on
their own renderer.

That 15 mm is generous on purpose. Below about 4 mm the offset sinks under 24-bit depth-buffer
resolution past roughly 140 m — and WebGL has no reversed-Z to help — so a smaller lift would
z-fight the asphalt across any city-sized view.

### Roads and seeds

Road generation draws **nothing** from any seeded random sequence. The network, ribbons, junction
pads and markings are pure functions of your grid settings.

The practical effect: the same seed gives you the same city whether or not Create Roads is on, and
toggling it never reshuffles a single building. This is verified by an automated test, not just
intended.

### Regenerating roads

Once built, the layout persists as a `BCG_RoadNetwork` component on the generation root — junction
nodes and the edges between them. That component is the source of truth, which is what makes
**Regenerate Roads** (on **1 Plan ▸ City Grid**) able to rebuild every road network in the open
scene exactly, in one Undo step, without having to reverse-engineer the layout from geometry.

Road objects carry their own hidden marker, so the placement guard, ground snapping and
*Select All* / *Destroy All* all handle them correctly — roads never block themselves, and
destroying generated content takes the roads with it.

The road material is pipeline-aware like the facade materials, so **Fix Materials** rebinds it if
you switch render pipeline. See
[Materials and Lighting](BuildingGen_08_MaterialsAndLighting.md).

> If a scene made with an older version ever ends up with an invisible road or an empty road
> container, the health dashboard offers a **Roads** repair button that rebuilds broken networks
> from their stored data. See [Finishing and Shipping](BuildingGen_10_Finishing.md#the-fix-row).

### What the road builder does not do

Be aware of the boundaries before you plan a level around it:

- **Flat ground only.** Roads assume a level plane. Sloped terrain and ground-following roads are
  not supported.
- **90° grid junctions only.** Every junction is a cross, T, corner or dead end at a right angle.
  There are no roundabouts, no rounded fillets, no off-grid intersections.
- **Straight streets only.** The Along Path layout lines buildings along a curve but does **not**
  generate a road surface under them.

For curved streets, ramps, elevation changes and proper multi-way junctions, see the Road
Constructor bridge below.

## Working with Road Constructor (optional)

**Road Constructor** by Pampel Games is a separate, separately-purchased Asset Store product that
builds spline-based road networks — curves, ramps, elevation, complex junctions. If you
own it, this package bridges to it automatically.

**If you do not own it, none of this exists.** No extra menus, no extra toggles, no references of
any kind — the bridge code lives in its own assembly that is not compiled at all unless Road
Constructor's assembly is detected in your project. Detection is automatic after every script
compile, and reverses itself if you later remove Road Constructor.

The bridge works in two directions:

**1. Build the city grid through Road Constructor.** With Road Constructor installed, a **Road
Backend** dropdown appears next to Create Roads on **1 Plan ▸ City Grid**. Switch it from
*Built-in (grid roads)* to *Road Constructor* and **Generate City** lays every street and avenue as
a real Road Constructor road, adds drivable colliders, and exports the generated meshes so they
persist in your project. Sidewalk Width applies only to the built-in geometry and is disabled while
this backend is selected. A Console report tells you how many segments were built and why any
failed.

**2. Line existing Road Constructor roads with buildings.** On **2 Build ▸ Street ▸ Along Path**, a
Road Constructor section lists every network it finds, each with a **Populate Along RC Roads**
button. That samples the road's spline into a polyline and lines both sides with buildings using
the pane's current seed and mix. This is how you get buildings along genuinely curved or elevated
roads: build them with Road Constructor's own tools first, then line them here.

### Requirements and gotchas

- A `RoadConstructor` component in the open scene with a **RoadSet** assigned.
- A **ground collider** under the city, included in Road Constructor's own Ground Layers mask —
  it raycasts down to find the height for every road point.
- The grid backend looks up two road names in your RoadSet: **`Side Street Asphalt`** for streets
  and **`Boulevard Asphalt`** for avenues. Road Constructor's shipped demo set defines both. If your
  set uses different names, generation stops with a Console message naming the missing road rather
  than silently building nothing — rename or duplicate entries in your RoadSet to match.
- Set Road Constructor's **Collider Layer** to a layer *outside* its own Ground Layers mask. If they
  overlap, a second city grid's ground raycasts can hit the first grid's road colliders and read the
  wrong height. When the tool detects that overlap it leaves the new colliders on their default
  layer and says so in its report.
- Road Constructor's meshes are pooled scratch objects until exported, so the grid backend exports
  them for you into `Assets/BCG_RCRoadExports/<city name>/`. That folder is **your project's**
  content, not part of this package — safe to delete or regenerate at any time.
- Buildings placed along a Road Constructor road use their own seed sequence. The same seed always
  reproduces the same roadside row, but it shares no sequence with the straight scatter or the city
  grid.

Full troubleshooting for the bridge — including what to do if a Road Constructor update breaks the
integration — is in
[Troubleshooting ▸ Road Constructor](BuildingGen_11_Troubleshooting.md#road-constructor-integration).

## Dressing the pavements

Empty pavements are what make a generated city read as a model rather than a place. **3 Dress ▸
Furniture** lines every road network's pavements with lamp posts, benches, bus shelters and trees.

Press **Generate Street Furniture** and props march both pavement centrelines, staggered from side
to side, with clearance kept at every junction. Lamps set the rhythm at roughly 18 m spacing;
benches, shelters and trees fill the gaps from a per-road-edge deterministic sequence — so the roads
themselves stay free of randomness, and regenerating an unchanged network reproduces identical
furniture.

Two props are gated on space, because a bench you cannot walk past looks worse than no bench:
**trees need at least 2 m of pavement**, and **shelters need at least 1.8 m**.

**Lamp heads glow at night for free.** They sample a guaranteed-lit pane in the atlas's night
emission map, so the same day/night material swap that lights your windows lights the lamps too —
with no real light sources at all. Trees use two small solid-colour materials
(`BCG_Furniture_Foliage` and `BCG_Furniture_Bark`), created pipeline-aware like everything else.

**Remove Street Furniture** clears it; regenerating replaces in place.

### Crashable props

By default all the furniture on a network is combined into a few chunk meshes. That is the cheapest
possible filler, but every prop is part of one immovable wall — no good if you want a lamp post that
falls over when a car hits it.

Turn on **Separate Props** (the toggle on **3 Dress ▸ Furniture**, mirrored in the gear menu and as
a menu item) and regenerate. Each planned prop becomes **one prefab instance** instead. The layout
is identical in both modes — same planner, same sequence.

The first run in separate mode creates four **editable prefabs** in your output folder:
`BCG_Furniture_Lamp`, `_Bench`, `_Shelter` and `_Tree`.

- Each ships with a **convex MeshCollider**, so it is Rigidbody-ready as it stands. Trees collide
  via their trunk child only — canopies stay collider-free so nothing invisible blocks the road.
- **Your edits to those prefabs are permanent.** Add a `Rigidbody` to the lamp prefab once and every
  lamp in every city becomes knock-overable. Regenerating never overwrites an existing prop prefab,
  and Clean Unused never offers furniture assets for deletion.
- Props are marked **Occludee Static only, never Batching Static** — a batched prop that starts
  moving renders incorrectly.
- Regenerating with the toggle back off re-combines the exact same layout.

**The trade-off:** separate mode swaps draw calls for interactivity — a demo-scale city becomes
roughly 1,500 small objects instead of a handful of chunks. They are small low-poly meshes sharing
three materials, so they batch well on desktop. For pure background filler, and especially on
mobile, keep the default combined mode.

## Common questions

**Can I drive on the generated roads?** Yes. The road surface has a `MeshCollider` sharing the
render mesh, and the kerbs are ramped so vehicles can mount pavements without catching.

**Why is my Along Path row not generating a road?** Road surfaces are straight-layout only. Use the
Road Constructor bridge, or lay your own road geometry along the path.

**My markings flicker at a distance.** Check your camera's near clip plane — city-scale scenes need
it no lower than about 0.5 for the depth buffer to resolve the marking offset cleanly.

**Buildings ended up on the pavement.** On the Street pane, check that **Generate road surface** was
on *before* you generated the row — the setback widens only when the tool knows a pavement is
coming.

## Where to go next

- **[Cities and Districts](BuildingGen_05_CitiesAndDistricts.md)** — the grid that roads fill.
- **[Materials and Lighting](BuildingGen_08_MaterialsAndLighting.md)** — night markings and baked
  lighting on roads.
- **[Optimising](BuildingGen_09_Optimizing.md)** — street furniture and combining the finished city.
