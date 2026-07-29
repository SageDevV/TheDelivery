# Add-ons

Optional content packs for Urban Building Generator, shipped as nested `.unitypackage` files so the
base asset imports fast. Import a pack only if you want its content.

## BuildingGen_CityDemo.unitypackage

The full playable **City demo**: a ~900-building demo city scene with drivable roads and baked
lighting, plus the generated mesh/prefab library the scene references (~2,700 assets). Importing it
can take a couple of minutes — that is exactly why it is optional.

**To import:** double-click the package, or use *Import City Demo* in
`Tools > BoneCracker Games > Building Generator > Welcome Window` (Quick Start tab) or the generator
window's *Manage* zone (Add-ons section).

**To remove:** use the *Remove* button beside the import button in the Welcome window — it deletes
the demo scene, its baked lighting, and the generated meshes/prefabs no other scene of yours uses
(re-importing the pack restores everything with identical GUIDs).

The small showcase demo scene in `Demo/` works without this pack.
