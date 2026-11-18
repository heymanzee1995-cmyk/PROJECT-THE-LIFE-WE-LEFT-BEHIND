# Vision reference

Approved 2026-11-18. This document records the target look and feel, and the decisions that
**override** the concept mockups wherever they disagree. The mockups are a direction, not a
specification: where this file and a mockup differ, this file wins.

The mockup images themselves are **not** stored in the repository until their source and licence
are recorded in `Docs/AssetLicences.md` (see "Asset status" below).

---

## 1. Title

The one correct title is **THE LIFE WE LEFT BEHIND**.

- Ignore every other variant shown in early mockups, especially "The Life We Create".
- The trademark and store-listing check is still pending (see the compliance checklist). No final
  logo or brand assets are produced until it is cleared.

## 2. Visual direction

Taken from the approved mockups:

- **Style:** warm, painterly 2D illustration, soft daylight, gentle saturation. Cosy and premium,
  never childish. For an adult audience (18–55+).
- **Setting:** Willowbrook, a small coastal town with a lighthouse, flowering gardens, cottages and
  a weathered family house that visibly improves as it is restored.
- **UI:** large rounded buttons and panels with soft shadows and clear hierarchy. Readable text sizes
  with strong contrast against the art.
- **Colour roles** (to be confirmed in `Docs/ArtStyle.md`, M8):

| Colour | Role |
|---|---|
| Green | Play, Continue, Deliver, Go |
| Blue | Story and dialogue |
| Purple | Events and collections |
| Orange | Shop |
| Gold | Coins and rewards |

- **Board:** cream or wooden tray with clear cell outlines. Items are chunky, readable icons with a
  visible tier progression.
- **Feedback:** a light burst and sparkle on merge; a gentle bounce-back and subtle "invalid" cue on
  rejected drops; reward pop-ups for coins and stars.
- **Top bar:** player level, energy (current/max with a refill button), coins and gems.

**What M2 will look like:** M2 uses flat placeholder shapes with tier labels, to prove feel, touch
and performance first. The mockup quality is the target for the art milestone (M8). Characters,
environment and item art are locked in a style guide **before** content is mass-produced.

## 3. Board dimensions

**Default board: 5 columns × 8 rows.** It stays configurable through data; the engine has no
built-in size. **7 × 9** is kept as an alternative test configuration.

Why 5 × 8: larger touch targets, more readable item art, better portrait proportions, more
breathing room, and more comfortable play on smaller Android phones.

### Estimated cell size on representative screens

These are **estimates**, not measurements. The final layout and real on-device sizes are measured
in M2, before the Unity board layout is finalised, using an on-screen overlay that reports the cell
size in dp on your phone.

Layout assumptions (dp):

| Element | Size |
|---|---|
| Status bar | 24 |
| Navigation bar | 48 |
| Top currency bar | 64 |
| Order panel | 112 |
| Bottom bar | 80 |
| Padding | 16 |
| **Total screen furniture** | **344** |
| Side margin | 12 per side |

`cell = min((width − 24) / columns, (height − 344) / rows)`

| Screen (portrait, dp) | 5 × 8 cell | 7 × 9 cell |
|---|---|---|
| Compact 16:9, 360 × 640 (older budget phones) | **37 dp** (height-limited) | 33 dp |
| Budget 20:9, 360 × 800 | **57 dp** (height-limited) | 48 dp |
| Mid-range 19.5:9, 393 × 852 | **64 dp** (height-limited) | 53 dp |
| Large 20:9, 412 × 915 | **71 dp** (height-limited) | 55 dp |
| Small tablet, 600 × 960 | **77 dp** (height-limited) | 68 dp |

What this tells us:

- 5 × 8 beats 7 × 9 on every screen, by about 8–16 dp per cell on typical phones.
- 5 × 8 is limited by **height** everywhere. The vertical screen furniture around the board decides
  the cell size, so M2 must keep the top bar, order panel and bottom bar compact.
- **Risk: compact 16:9 phones.** At about 37 dp, the cell is below the usual 48 dp comfort target.
  M2 options: a compact layout for short screens (smaller order panel, slimmer bars), touch areas
  slightly larger than the drawn cell, or accepting that these older phones are below our minimum
  spec. Decided in M2 with on-device evidence.
- Targets for M2: drawn cells of at least 48 dp on 20:9 phones of 360 dp width and up, and touch
  areas of at least 48 dp everywhere.

## 4. Characters

Original characters only. No copying of any other game's characters, names, art or dialogue.
Full want/secret/arc entries go in `Docs/CharacterBible.md` when the narrative milestone (M7) begins.

| Character | Direction |
|---|---|
| **Emma Williams** (29) | Protagonist. Warm, determined, a little guarded. Recognisable silhouette: brown hair in a loose bun or ponytail, practical clothes (denim, apron while restoring). Appears with consistent features in every scene |
| **Noah Bennett** | Childhood friend and carpenter. Grounded, wry, quietly hopeful about Emma's return |
| **Olivia Carter** | Emma's outspoken best friend; runs the flower shop |
| **Margaret Williams** | Emma's late grandmother (see section 5) |
| **Daniel Brooks** | Property developer with complicated motives |
| **Arthur Reed** | Retired teacher; keeper of the town's history |
| **Biscuit** | The dog companion (see section 6) |

Consistency rule: each character gets a locked reference sheet (front view, expressions, outfits)
before any scene art is produced (M8). The mockups show Emma in several different outfits; a fixed
set of outfits per chapter will be chosen deliberately, not left to vary.

## 5. Margaret Williams: timeline rule

**Margaret has died before the game begins. She is never alive in the present-day timeline.**

She may appear only through:

- flashbacks and memory sequences, including scenes from Emma's childhood;
- photographs;
- letters and handwritten notes;
- recorded messages (audio or video).

Rules for writing and art:

- Every scene featuring Margaret must be visibly framed as the past or as a record: a flashback
  colour grade or vignette, a photo frame, letter paper, or a playback device. She must never be
  standing in a present-day scene.
- Present-day dialogue speaks of her in the past tense.
- The mockup panel showing Emma beside an elderly woman in the present day is **not** to be used
  as-is for Margaret.
- Narrative data (M7) will tag Margaret scenes as `flashback`/`memory`/`record`. The content
  validator will reject a Margaret scene without one of those tags. *(Planned check; not built yet.)*

## 6. Biscuit, the dog companion

An original dog companion named **Biscuit**.

- **Role:** adds warmth and humour; a recognisable part of the game's identity.
- **Appears in:** selected story scenes, and reactions to successful merges and discoveries (for
  example a happy bark and a short line like "Great merge!" in a small portrait beside the board).
- **Personality:** to be defined in the character bible (M7). Direction: loyal, curious, comically
  enthusiastic, with a consistent look in every appearance.
- **Never on the board:** Biscuit lives outside the merge grid. Reactions use a small, non-blocking
  portrait or bubble outside the cells. They never cover cells, never capture touches and never
  delay input. Reactions are throttled, so they stay charming rather than noisy.
- **When:** added to `Docs/CharacterBible.md` when M7 begins. Merge reactions can be added once the
  board feedback exists (M2 hooks, M7 content).

## 7. Which mockup belongs to which milestone

| Mockup element | Milestone |
|---|---|
| Merge board, drag and drop, merge burst, bounce-back, generator tap, inventory and storage, sell/store, board-full feedback | **M2** |
| Top bar (energy, coins, gems, level), customer order card and Deliver (with consumed-items preview), rewards | **M3** |
| First-session guidance ("Merge to level up!" pointer), goal display | **M6** |
| Story dialogue and reply choices, character portraits, Biscuit in scenes, chapter titles | **M7** |
| Restoration before/after, town map with chapter stops | Restoration system, with or after M7 |
| Final art, animation polish, style guide | **M8** |
| Shop, bundles, prices | **M10** |
| Main menu (Continue, Events, Collections, Shop, Map, Inbox, Settings) | Built up across M3 to M10 as each feature exists |
| Collections, Summer Festival and other live events | After launch |

## 8. Explicitly excluded

| Element | Status |
|---|---|
| **Boosters** (hammer, glove, wand, backpack and similar tools under the board) | **Excluded** from M2 and until the core loop has been tested with real players. No booster code or UI. To be reconsidered then |
| **"More Games" button** | **Excluded** from the design |
| **Title variants** ("The Life We Create", etc.) | Excluded; see section 1 |
| **Margaret alive in the present** | Excluded; see section 5 |
| **Anything on the board other than items and generators** (mascots, decorations that take touches) | Excluded; see section 6 |
| **Copying another game's characters, art, UI layouts, names or story** | Always excluded |

## 9. Git workflow

The `feature/phase-N` branch convention stays, for example `feature/phase-1-ui-prototype`,
`feature/phase-2-merge-engine` and `feature/phase-3-economy-orders`. The branch names shown in the
mockup's workflow panel (core/content/polish/release) are not used. Nothing is merged into `main`
without approval.

## Asset status

The two concept mockup sheets are **reference only**. They are not stored in the repository and
must not ship as game art. Before they are committed (even as reference), record in
`Docs/AssetLicences.md` who made them, how (including whether AI tools were used, per current
Google Play policy) and the licence. Final art will also be checked against existing genre games'
UI layouts, so the game's presentation stays original.
