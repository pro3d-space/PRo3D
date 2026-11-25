# Next release notes

Release-note entries for changes merged since the last release, collected here until the next
release folds them into `PRODUCT_RELEASE_NOTES.md`.

- **Add** an entry here with every user-visible change, in the same change (PR) as the change
  itself, in the style of `PRODUCT_RELEASE_NOTES.md`: one bullet, area first, the effect for
  the user in bold, the issue/PR and the doc page in parentheses at the end.
- **Do not** add a section to `PRODUCT_RELEASE_NOTES.md` for unreleased work. The build parses
  every `##` heading there as a version (`Build.fs`, `ReleaseNotes.load`); an `## Unreleased`
  heading fails every build.
- **When releasing**, move the bullets below into the new version's section of
  `PRODUCT_RELEASE_NOTES.md`, write its one-line summary, and leave this file with the header
  and an empty list (docs/Build-Deploy-System.md).

## Pending

- Surfaces: **OPCs can be shipped packed, one stored zip per OPC.** In a surface folder, `<Opc>.opc` (or `.opcz`, `.zip`) holding the OPC's `Images` and `Patches` loads in place of the OPC folder, read-only and without unpacking; the `.opcx` and `metadata` stay beside it. Import the surface folder as usual, through *Import OPCs* or by dropping it onto the 3D view. The hierarchy cache must be packed with each OPC, and KdTrees should be, or picking is off (#322, docs/PackedSurfaces.md)
