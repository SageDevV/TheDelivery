# Troubleshooting and FAQ

Problems first, then the questions people ask most. Every message the tool prints is prefixed
`[BCG BuildingGen]`, so filtering the Console on that string shows you everything it has to say.

## Rendering problems

Anything to do with how buildings and roads look on screen — wrong shaders, missing
textures, night glow and z-fighting.

### Buildings are pink or magenta

**Cause:** the facade materials reference a shader that does not exist in the active render
pipeline. It happens when you import into a URP or HDRP project, or switch pipeline afterwards.

**Fix:** click **[Fix]** beside the material badge in the City Ledger, or press **Apply Materials**
on **3 Dress ▸ Mood**, or run `Tools ▸ BoneCracker Games ▸ Building Generator ▸ Fix Materials
(Active Pipeline)` with no window open. All three rebuild the materials in place, and every existing
reference survives because the GUIDs do not change.

Full detail: [Fix Materials](BuildingGen_08_MaterialsAndLighting.md#fix-materials-the-one-button-that-solves-pink).

### Console: "shader not found; falling back"

The tool could not resolve the expected shader for your pipeline, so it fell back to the other
pipeline's lit shader rather than leaving you with magenta. This usually means the pipeline package
is still importing. Wait for the import to finish, then run **Fix Materials** once.

### Console: "Facade albedo not found at …"

The texture atlases are missing from `Assets/BCG/BuildingGen/Textures/`. Reimport the package, or
restore that folder from your version control. The same message shape appears for the road atlas.

### Windows do not glow at night in my baked lighting

Emission is read from whatever materials are assigned **at bake time**. The `_Night` material
variants always glow; the base materials only glow when the Night Lights **Intensity** is above
zero. Set the look you want first, then bake.

### Road markings flicker at a distance

Markings sit 15 mm above the asphalt, which is enough for any sane setup — but a very low camera
near-clip plane starves the depth buffer at city scale. Set your camera's near plane to about 0.5
rather than 0.01.

## Placement problems

Buildings ending up in the wrong place, being skipped, or sitting at the wrong height.

### Buildings ended up somewhere I did not put them

They were relocated because the spot was taken. Every placement path tests a building's footprint
against existing generated buildings and moves it to the nearest free spot rather than letting two
intersect. In open ground it places exactly where you asked. The Console reports how many moved.

### Buildings are sitting on my roads / scenery

By default the tool only avoids **its own** buildings; your scenery is invisible to it. Point
**Obstacle Layers** (in the **Where** section) at the physics layers your scenery lives on.

Two things to check if that does not work:

- **The test is physics-based** — obstacles need a *collider*. A visual-only mesh is invisible to it.
- **Trigger colliders are ignored**, deliberately.

### Console: "N building(s) blocked by Obstacle Layers"

Working as intended: those spots had no clear alternative nearby, so the buildings were skipped
rather than forced. Skips never shift the rest of the layout — every other building stays exactly
where that seed put it. Widen the area, lower the Edge Margin, or narrow the obstacle mask.

### Console: "Zone … is too small to populate"

The district's box is smaller than the minimum plot the tool needs — roughly **7.8 m** in each
direction, before the Edge Margin is subtracted. Make the box bigger or reduce Edge Margin.

### Console: "Zone … has no BoxCollider — skipped"

A district needs a `BoxCollider` on the same GameObject. Adding a `BCG_BuildingZone` component
requires one, so this usually means the collider was removed afterwards.

### Console: "Snap To Ground found no ground"

The rays under those plots hit nothing on the **Ground Layers** mask. Check that your terrain or
ground mesh is on a layer included in the mask, and that it is actually beneath the district.
Colliders are not required — visible meshes work too — but the object has to be on the mask.

Those buildings keep their flat placement rather than failing.

### Buildings are buried in my hillside

That is the pre-2.3 behaviour, or a building moved onto a slope by hand. Ground snapping now raises
the base to the **highest** ground hit on slopes steeper than 5°, and fills the cut below with a
basement wall.

To repair existing buildings without regenerating: open **4 Ship ▸ Health** with **Snap To Ground**
on, and press **Skirts** in the fix row. It re-probes the ground and re-derives the base height for
every flagged building in one Undo-able step.

### Console: "building(s) with skirt damage were renamed away from the generator's naming"

The repair needs to parse a building's original recipe out of its name, and those objects were
renamed. Rename them back, or regenerate them.

## Generation problems

Problems while generating, and things that did not change when you expected them to.

### The Editor froze while filling a district

It should not — fills run one building per frame precisely to avoid that. If it happens, check
whether you are running a fill from a script or a menu path that bypasses the job runner. Normal
fills show progress in the City Ledger and can be cancelled there.

### Console: "A populate job is already running"

Only one fill runs at a time. Let it finish, or press **Cancel** in the City Ledger. Commands that
would fight a running job are greyed out while it runs; *Fix Materials* and *Select All Generated*
stay available because they cannot conflict.

### My buildings disappeared after a script recompile

You generated with **Save As Prefab Assets** off, and did not save the scene. Scene-only buildings
live in the scene file and nowhere else; a domain reload from a recompile or an Editor restart
destroys unsaved scene objects.

**Save the scene immediately** after generating scene-only buildings. There is no recovery
afterwards.

### Console: "Skipping unparseable prefab name"

Regenerate All works out what to rebuild from each prefab's name. A renamed prefab cannot be parsed,
so it is skipped rather than guessed at. Rename it back to the generator's pattern, or leave it —
being skipped does no harm.

### Flipping an option did not change my existing buildings

Nothing changes retroactively. Content options apply when a building is generated. Regenerate those
buildings, or refill the district, to pick up new options.

Note that **Regenerate All** deliberately does *not* apply your current toggles — it preserves what
each asset was authored with. To change content options you must regenerate from a pane with the new
settings.

## Lighting problems

Baked GI, lightmap UVs and light probes.

### My buildings were dropped from the lightmap bake

Unity's lightmapper rejects meshes whose vertex layout is not the standard float format, and reports
`Invalid Mesh was removed from light baking input`.

If you generated with **version 2.3.0**, its buildings carry a lightmap UV set the lightmapper cannot
read. They are counted as **missing** lightmap UVs, so the fix is: **4 Ship ▸ Finalize ▸ Bake
Lightmap UVs… ▸ Bake Missing**, once. You do not need Renew All and you do not need to regenerate
your library.

### Console: "Lightmap unwrap failed for …"

That mesh could not be unwrapped, so it was left without a UV set and Contribute GI stays off for
it — deliberately, since enabling GI on a mesh with no valid UV2 produces worse results than leaving
it dynamic. Usually the mesh is degenerate or unusually large; regenerating it often clears it.

### Everything went dark after I baked

Check the light probes. A probe buried inside a building bakes black and drags everything
interpolating against it dark. The probe generator pushes probes out of building volumes
automatically and reports how many it relocated — if you placed probes by hand, check for any
sitting inside geometry.

### Baked lighting vanished after Optimize City

Expected. Combining discards the existing lightmaps and re-unwraps the combined geometry. **Combine
first, then bake** — the other order wastes the bake.

## Road Constructor integration

*(Only relevant if you own Road Constructor by Pampel Games.)*

### Some road segments failed with a "HeightRange" cause

This was caused by the tool itself and is fixed. Every district carries an invisible collider that
sat on the same layer Road Constructor raycasts for ground height, so a ray could hit a district's
collider top instead of the ground and read a height delta outside Road Constructor's tolerance. The
grid backend now disables district colliders for the construction pass and re-enables them
immediately afterwards.

If you still see occasional HeightRange failures on genuinely non-flat terrain, that is Road
Constructor's own validation working correctly — widen its **Height Range** tolerance to match your
terrain's elevation spread.

### Generation stops saying a road name is missing

The grid backend looks up two exact names in your assigned RoadSet: **`Side Street Asphalt`** for
streets and **`Boulevard Asphalt`** for avenues. Road Constructor's shipped demo set has both. If
yours uses different names, rename or duplicate entries so those names exist — the tool reports the
missing name rather than silently building nothing.

### A Road Constructor update broke compilation

Compile errors will be confined to `Assets/BCG/BuildingGen/Editor/RC/`, the only folder allowed to
reference Road Constructor's types.

**Delete that `Editor/RC/` folder.** That is the procedure while Road Constructor is still installed.
Removing the `BCG_URBUGE_RC` scripting define instead does **not** work — the detector re-adds it
after every compile, because Road Constructor is genuinely present. With the folder gone there is no
assembly left for the define to gate.

### A fresh clone or CI checkout fails to compile

Different situation, opposite fix. If the project was cloned **without** Road Constructor but the
`BCG_URBUGE_RC` define is still baked into `ProjectSettings`, the bridge assembly tries to compile
against types that are not there — and the auto-detector cannot correct itself while the project is
stuck mid-compile-error.

**Remove `BCG_URBUGE_RC` from Player Settings manually.** Compilation recovers, and the detector will
add the define back automatically the next time Road Constructor is actually present.

## Frequently asked questions

The questions that come up most, about what the tool can and cannot do.

### Can I use this at runtime?

Yes. The geometry engine ships in the Runtime assembly, so you can generate buildings while your
game is playing — endless runners, streaming worlds, procedural cities. Same engine, same seeds, same
results. Runtime output has no static flags, lightmap UVs or LODGroup, since those are editor
concerns. See [Reference ▸ Runtime API](BuildingGen_12_Reference.md#runtime-api).

### Do I need the tool in my final build?

No. Generation happens in the Editor and produces ordinary meshes and prefabs. The only things that
ship are a few tiny data-only components — most importantly the marker that records each building's
recipe. You could delete the Editor folder entirely and your city would still work.

### Can I go inside the buildings?

No. These are exteriors — one mesh, one material, no interiors and no openable doors. The rooms you
can see through the windows are a shader effect with no geometry behind them. This is what keeps a
building at one draw call.

### Can I change the material on one wall?

No, for the same reason. A building is a single mesh with a single material. You can repaint the
shared texture atlas — see [Atlas Layout](BuildingGen_AtlasLayout.md) — or parent your own objects
to a generated building in the scene.

### Will updating the package change my existing city?

No. Existing scenes and assets are never modified retroactively. When you *want* to pick up
improvements, run **Regenerate All**, which rebuilds every prefab in place while preserving each
one's authored content options and GUIDs — so scene placements survive.

### Is the same seed guaranteed to give the same building?

Yes, and it is enforced by automated tests. The random sequence is a fixed contract; new features
append to the end of it rather than inserting in the middle, so upgrading does not silently change
what your existing seeds produce. The one exception is deliberate: turning on a content option that
did not exist before (props, extras, signage) adds geometry, and that is documented at each option.

### How many buildings can I generate?

There is no hard limit; the practical one is your scene. Generation is asynchronous at one building
per frame, so a very large city takes time but never freezes. For scale, keep **Mesh Variety**
finite and run **Optimize City** at the end.

### Does it work with terrain?

Yes — turn on **Snap To Ground**. It works against colliders *and* against visible meshes with no
collider, handles slopes with automatic basement walls, and reports plots where it found no ground.
Roads, however, are flat-ground only.

### Which render pipelines are supported?

Built-in, URP and HDRP. The only pipeline-coupled part is the facade materials, which **Fix
Materials** rebuilds for whichever pipeline is active. Fake Interiors is the one feature not
available on HDRP, where it falls back gracefully rather than breaking.

### Can I sell a game made with this?

Yes — that is what it is for. The usual Unity Asset Store licence applies: you ship the *output* in
your game, you do not redistribute the tool itself.

## Getting more help

If none of the above covers it:

1. **Filter the Console on `[BCG BuildingGen]`** — the tool reports relocations, skips, missing
   ground and failed unwraps rather than failing silently.
2. **Check the health dashboard** (**4 Ship ▸ Health**) — it will usually name the problem for you
   and offer a one-click fix.
3. **Note your versions** — the asset version is in the generator window's title row, and you will
   want your Unity version and active render pipeline too.
4. **Get in touch** at [bonecrackergames.com/contact](https://www.bonecrackergames.com/contact/).
