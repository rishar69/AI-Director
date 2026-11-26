# The Caretaker - museum layout v5 (two floors)

Approved and built 2026-11-16. `Assets/Editor/MuseumMapBuilder.cs` builds it into
`Assets/Scenes/Museum.unity` (Tools > AI Director > Museum - Build Map, or
`MuseumMapBuilder.Build(true)` from the CLI). The grids in the builder are the source
of truth; this file explains them.

Based on a real museum ground plan (central atrium with a corridor ring, galleries
north/west/east, lobby block south, staff wing west, cafe east), tightened for a chase:
3 m cells, 3 m corridors, ~12 m rooms, almost every room with 2-3 exits.

## Grid

28 x 18 cells of 3 m (84 x 54 m). Row 0 is north. Front doors on the south side of
cell (14, 17), which sits on the world origin. Floor to floor 5.6 m; roof line 10 m.

```
GROUND                              UPPER (5.6 m)
mmmAAASSSSSSSSSSSSSSSSSSTTTT        ......qqqqqqqqqqqqqqqqqq....
mmmAAASNNNNMMMMMMMMXXXXfTTTT        ......qiijjnnnnnnnnllllo....
mmmAAASNNNNMMMMMMMMXXXXfTTTT        ......qiijjnnnnnnnnllllo....
DDDDCCSNNNNMMMMMMMMXXXXfTTTT        ......qiijjnnnnnnnnllllo....
DDDDCCSNNNNMMMMMMMMXXXXfTTTT        ......qiijjnnnnnnnnllllo....
DDDDCCSrrrrrrrrrrrrrrrrrZZZZ        ......qbbbbbbbbbbbbbbbb.....
DDDDCCSssssrHHHHHHrIIIIrZZZZ        ......qaaaaboooooobcccc.....
RRRRCCSssssrHHHHHHrIIIIrZZZZ        ......qaaaaboooooobcccc.....
RRRRCCSssssrHHHHHHrIIIIrZZZV        ......qaaaaboooooobcccc.....
RRRRCCSssssrHHHHHHrIIIIrZZZV        ......qaaaaboooooobcccc.....
RRRRCCSPPQQrHHHHHHrBBEErFFFF        ......qaaaaboooooobcccc.....
UUUOOOSPPQQrHHHHHHrBBEErFFFF        .....qqaaaaboooooobcccc.....
UUUOOwSrrrrrrrrrrrrrrrrrFFFF        .....o.bbbbbbbbbbbbbbbb.....
UUUOOwSKKYYYLLLLLLGGGGGrFFFF        .....o......................
UUUOOwSKKYYYLLLLLLGGGGGrkkkk        .....o......................
UUUOOwSKKYYYLLLLLLGGGGGrkkkk        .....o......................
.....wSKKYYYLLLLLLGGGGGr....        .....o......................
............LLLLLL..........        ............................
```

Ground: H grand stair hall (double height), L lobby, r public corridors, M main
exhibition (diplodocus), N natural history, s sculpture, X antiquities, I discovery
room, T temporary exhibition, Z special collection, V strongroom, P/E toilets,
Q cloakroom, B first aid, K ticket office, Y security, G gift shop, F cafe, k kitchen;
staff: S corridors, m maintenance (caretaker starts), A artifact storage, D loading
dock, C archive, R restoration lab, U lounge, O offices; w service stairwell, f fire
stairwell. Upper: b balcony ring, n modern art, l lecture hall, a reading room,
c classroom, i director's office, j meeting room, q staff corridor, o open to below.

## Stairs (pack assets)

- **Grand stair** - `Museum_Big_Stair_02`, an imperial stair: central flight to a half
  landing, then two flights turning back to the balcony east and west. Scaled 0.8 in
  height (0.21 m risers) and given pale copies of its materials. Fills the north end
  of the stair hall, on axis behind the lobby.
- **Service stair** - five `Stair_wood` modules in the west stairwell, from the staff
  corridor up to the upper staff corridor.
- **Fire stair** - the same modules in grey concrete, from the east corridor (fire door)
  up to the upper service corridor.
- The balcony is railed with `Museum_Ark_Fence`. Walls and pillars that a stair
  arrives over stop under the upper slab (5.3 m) so no wall top cuts the NavMesh.

## Objectives

| Section | Key | Clock |
|---|---|---|
| West galleries (`NaturalHistory`) | Director's desk, upstairs | Sculpture gallery |
| East galleries (`Egyptian`) | locked case, Antiquities (smash) | Lecture hall, upstairs |
| Special Collections (`FineArt`, locked) | strongroom (master key) | Temporary exhibition |

Both section keys open the Special Collections gate (23,7)E. Each wound clock strikes
one digit (1-9, new every night); the three open the strongroom (26,9)E. The master
key opens the front doors. The fire door (24,0)W is bolted from inside the wing.
Shut doors block the player and the caretaker (`LockedDoor`: collider + carving
NavMeshObstacle, saved disabled so the bake runs through).

## Hiding (desks only)

Hiding is under desks only, tuned in `Assets/Scenes/HidingTest.unity`
(`HidingTestBuilder`). The pose lives in `HidingSpot.ConfigureDesk`: eyes 0.48 m up,
tucked 0.36 m back against the far legs, facing out of one long side, look limited
to +-60 deg yaw and -8..25 deg pitch, 0.45 s to duck in or out, no peeking. Desks get
a double-sided copy of their material (the pack's tabletop is single-sided).

Nine desks, placed where they matter - a refuge within a room or two of every loud
task, at least one per section per floor, none in a dead end, each facing the way
the caretaker comes in:

| Floor | Desk | Faces |
|---|---|---|
| Ground | Stone-age table, Natural History | north, into the gallery |
| Ground | Hands-on table, Discovery Room | north, to its three doors |
| Ground | Lab bench, Restoration Lab | south, to the lab door |
| Ground | Security desk | north, to the corridor door |
| Ground | Cafe table | north, to the cafe door |
| Ground | Attendant's desk, Temporary Exhibition | north, to the clock |
| Upper | Director's desk | south, to the office door |
| Upper | Reading-room table | east, to the balcony door |
| Upper | Back classroom desk | north, over the room |

## Look (art bible)

1. **Style** - a night-time museum in grey and white: pale plaster over a dark grey
   wainscot with a silver dado rail, grey oak and marble floors, slate plaques.
2. **Palette** - plaster `#CCCCC7`, wainscot `#383D3D`, oak `#757570`, marble `#A8A8A8`,
   slate `#1A1C1F`, silver `#9EA1A8`. Colour only on what matters: amber keys and
   clocks, red gate locks and strongroom dial, green exits, yellow hazard.
3. **Mood** - cold moonlit fill, white wall sconces pooling along corridors and the
   balcony, white spots on exhibits, grey laylights over the stair hall; post grade
   desaturated (-28), slightly cool.

Lighting keeps under URP Forward+'s 256 visible lights: one fill per two cells
(checkerboard), sconces every fourth cell, exhibit accents on top.

**Performance (measured 2026-11-17):** 428 realtime lights, none shadowed; ~126 fps
(7.9 ms) in the Editor from the start view, 525 batches, 132k tris. Decision: keep
realtime while the layout changes; **bake the fills and sconces once the layout is
final** (needs lightmap UVs on the generated meshes, light probes for the player and
caretaker), keeping only clock faces, key glows, the exit sign and the caretaker's
lights realtime. Measure the worst views (balcony over the hall, long corridors) and
a standalone build first.

## Signs

One slate plaque per gallery on the corridor side, small caption cards for toilets,
cloakroom and first aid, white plates above staff doors, green EXIT / FIRE EXIT, a
directory board in the stair hall.
