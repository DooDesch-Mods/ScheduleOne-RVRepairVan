# Changelog

All notable changes to RVRepairVan are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/), and this
project adheres to Semantic Versioning.

## [3.5.4] - 2026-10-15

### Fixed
- Co-op: the second player sees the RV drive and gets "Drive" on its seat. On saves past the intro they saw the stock
  vehicle instead.

## [3.5.3] - 2026-10-15

### Fixed
- The save intercom and the hazmat suit inside the RV hang flat on the wall. They stood a hand's width off it.

## [3.5.2] - 2026-10-15

### Fixed
- Co-op: other players see the RV when you drive and park it, not a bare Veeper that glitches out and vanishes
  after you get out.

## [3.5.1] - 2026-10-15

### Fixed
- Workers you hire after building the crew quarters walk into the RV and work. They used to wait outside with
  nothing to do.
- The hazmat suit in the RV hangs flat on a wire hook like an empty suit, not like someone is standing in it.

## [3.5.0] - 2026-10-15

### Added
- Nods to a certain chemistry teacher's RV: new lines from Marco and Mrs. Ming, and props in and on the RV to find.
- The RV battery can go flat after 3 in-game days unused. Hold a battery at the wheel to swap it; `BatteryBreakdowns`
  turns this off.

### Changed
- The RV's save intercom sits next to the light switch by the door.

## [3.4.0] - 2026-10-15

### Added
- The steering wheel says "Missing engine" until Marco gets the engine running.

### Fixed
- The quest icon no longer turns into a white square after loading a save.
- With Schedule Maintenance installed, the RV door opens again: its Krystal Ship switches off once your RV is repaired.
- Driving the RV: the camera no longer jerks in and out past poles and power lines.
- Workshop floor: equipment placed before the upgrade no longer leaves a second raster that turns free spots red.

## [3.3.0] - 2026-10-15

### Added
- The RV's hazards blink while it stands on its home spot, so you can see where getting out puts it back home.

## [3.2.2] - 2026-10-15

### Fixed
- Parking the RV back on its spot works facing either way, so building and the crew come back after a drive.

## [3.2.1] - 2026-10-15

### Fixed
- Seeds no longer fall through pots in the RV while it is parked away from home.

## [3.2.0] - 2026-10-15

### Added
- The RV has a save intercom on the wall behind the door, like every other property.

## [3.1.1] - 2026-10-15

### Fixed
- The RV knocks people over when you drive into them, like any other car.

## [3.1.0] - 2026-10-15

### Added
- Marco sells a fifth build-out, **Get the engine running** ($10,000): drive the RV anywhere. `PriceEngine` sets the price.
- Parked away, the RV keeps its stations and the items you place in it. Crew and deliveries wait until it is back on its spot.
- Headlights (H), brake and reverse lights work while you drive. The light switch inside works wherever the RV stands.
- The RV rolls and steers on its own tyres, and the entry step folds in while you drive.

### Fixed
- A paid repair survives a restart again. On a slow load the RV came back wrecked and Marco asked for the money again.
- The repair sound follows the Master and Sound Effects sliders.
- Small gaps in the RV's walls no longer show the outside.

## [3.0.1] - 2026-10-14

### Changed
- Donna, Mrs. Ming and Marco got new lines. Every reply tells you where to go next, and Marco talks like he does in the game.
- Marco takes Mrs. Ming's name on your first visit. The extra "Fifty grand?" step is gone.
- "Can you go any lower?" is one choice now. Marco sends you for his package first, then takes packaged product from your hand.
- Marco texts you once the RV is repaired and mentions his build-outs.

## [3.0.0] - 2026-10-14

### Added
- Marco sells four build-outs once the RV stands again, each with its own cinematic. They build on each other.
  - **Gut the interior** ($2,500) clears out the bed, bench and cabinets.
  - **Build me a workshop floor** ($7,500) adds 24 tiles to the RV's 32.
  - **Make room for a crew** ($15,000): three employees can work in the RV. Place a locker inside.
  - **I need a loading dock** ($25,000): your supplier delivers to the RV.
- Every price is a setting, as are `CrewSize` and `BuildGridExtraTiles`. Marco's offer updates without a restart.
  `UpgradesEnabled` turns the shop off.
- `InstantRepair` fixes the RV for free the moment it is wrecked.
- `RepairTakesADay` leaves the RV with Marco for 24 in-game hours. He texts you when it is ready.

### Changed
- Works on game version 0.4.7 and needs S1API **3.2.1** or newer. With an older S1API the mod loads but does nothing.
- In co-op, whoever pays for the repair, a lost package or a build-out pays from their own cash. Without enough
  cash the deal is turned down.

### Fixed
- Donna, Mrs. Ming and Marco offered no option on game 0.4.7, so the questline could not start.
- Crew hired for the RV stood outside and never worked. They walk in through the door now and work inside.
- After loading, a repaired RV could show "Back on the Road" again with a marker on the RV.
- Loading a save from the main menu could clear your progress and restart the questline.

## [2.6.2] - 2026-08-04

### Fixed
- Talking to Mrs. Ming could show an option that did nothing when you clicked it, leaving
  the questline stuck. The same could happen with Donna and Marco.
- When the game is slow handing over an NPC's dialogue, the mod waits for it now. If it
  never shows up at all, the NPC just says their line out loud and the step still goes through.

## [2.6.1] - 2026-08-01

### Changed
- Runs on Schedule I 0.4.6f11.
- Needs S1API 3.1.1, up from 3.0.5. Update it along with the mod.

## [2.6.0] - 2026-07-08

### Added
- The built-in translations are now always visible on disk: next to `_template.en.json`
  the mod also writes one template per shipped language - e.g. `_template.de.json` with
  the complete German text - into `UserData/DooDesch/Localization/RVRepairVan/`. Want to
  tweak a line? Copy the template to `de.json`, keep the lines you change, done. The
  templates refresh on every game start, so they always match the installed version.

## [2.5.0] - 2026-07-08

### Added
- Translation mods: translations can now also be installed as their own code-less
  mod packages shipping `Mods/Localization/RVRepairVan/<language code>.json` - so a
  community translation is one mod-manager click away instead of manual file copying.
  Per line, your own file in `UserData/DooDesch/Localization/` still wins over an
  installed translation mod, which wins over the built-in texts. How to publish one:
  https://github.com/DooDesch-Mods/ScheduleOne-L10n/wiki

## [2.4.0] - 2026-07-08

### Added
- Bring your own translation: the mod now also loads translation files from
  `UserData/DooDesch/Localization/RVRepairVan/<language code>.json`. A few lines are
  enough to fix wording you don't like, and a full file adds a whole language the mod
  never shipped. Your entries always win over the built-in English/German.
- To make that easy, the mod writes `_template.en.json` into that folder on startup,
  listing every translatable string. Copy it, rename it to e.g. `fr.json`, translate
  the values, restart - done. Full guide:
  https://github.com/DooDesch-Mods/ScheduleOne-L10n/wiki
- The localization layer now lives in its own open-source project,
  [ScheduleOne-L10n](https://github.com/DooDesch-Mods/ScheduleOne-L10n), so other mods
  can use the same system (and share the same `Language` setting).

## [2.3.0] - 2026-07-08

### Added
- German translation. The whole questline - journal entries, objectives, all of
  Donna's/Mrs. Ming's/Marco's dialogue and the quest items - is now available in German.
- Automatic language pick: German systems get German, everything else gets English
  (the game itself has no language setting, so the OS language decides).
- A shared `Language` setting (under `DooDesch` in `MelonPreferences.cfg` / the Mod
  Manager UI) to force a language: `auto`, `en` or `de`. Other DooDesch mods will use
  the same setting as they pick up translations.

## [2.2.1] - 2026-07-01

### Fixed
- The quest marker now tracks Marco, Mrs. Ming and Donna as they move around town, instead of
  freezing at wherever the target happened to be standing when the objective updated. Following
  the marker now leads you straight to the right person even while they walk their daily route.

## [2.2.0] - 2026-06-24

### Fixed
- Multiplayer co-op now works end to end and has been verified in a real two-player session:
  both players can play through the entire repair questline together. The previous build shipped
  co-op as experimental; this release fixes the issues that kept it from working.
- The joining player now gets the repair quest from Donna and sees the same quest progress as
  the host, instead of having no dialogue option or a different quest state.
- Both players can see, pick up, carry and hand in the errand items (Ming's crate, Marco's
  package). The quest advances no matter which player makes the delivery, and the pay-instead
  fallback still applies if the item is lost.
- The dead-drop marker now points to a valid drop for the joining player; previously the marker
  could lead to nothing on the client.
- Giving Marco samples to lower the price works for the joining player too, and the discount
  stays in sync for both players.
- Quest items are now reliably removed from the joining player's inventory once they are
  handed in.

## [2.1.0] - 2026-06-24

### Fixed
- Giving Marco a packaged sample no longer fails with "That ain't packaged" while you are
  holding sealed product. The check now reads your inventory instead of the in-hand item,
  which the game empties while a conversation is open.
- Each sample's discount is now based on the value of the single package you hand over,
  rather than scaling with how many were left in the stack.
- The quest marker (the wrench) could end up pointing nowhere after returning to the main
  menu and reloading a save; it now re-points to the right target once the world finishes
  loading.

### Added
- You can now choose which packaged product to give Marco: the dialogue lists each packaged
  product you are carrying together with the discount it would give.
- Marco actually consumes the sample you hand him now (the matching smoke/snort/eat
  animation and effects), instead of just taking it.
- Marco pays more for cleaner product and less for junk: the per-sample discount is scaled
  by quality (Trash 0.6x up to Heavenly 2.0x).
- Experimental host-authoritative co-op support. Single-player is unaffected; co-op has
  only had limited testing, so please report any multiplayer issues.

## [2.0.1] - 2026-06-17

### Added
- Marco now shows a persistent "What can I bring to lower the price?" reminder once you
  have earned his trust (after the package favour). It explains that bringing packaged
  product lowers the bill, so players who skipped the dialogue still know how to keep
  progressing and afford the repair. It appears whenever you are not currently holding
  product to hand over (when you are, the "give a sample" action is shown instead).

## [2.0.0] - 2026-06-17

A ground-up rebuild on S1API with a full story questline, real items, save-bound
persistence and a repair cinematic.

### Added
- "Back on the Road" questline (default `QuestMode = Questline`): the RV is wrecked in
  the intro, then Donna points you to Mrs. Ming, who sends you on a dead-drop crate
  errand, who refers you to Marco the mechanic at the docks.
- Native S1API dialogue: real in-conversation choices and replies (no more floating
  worldspace-only bubbles).
- Referral pricing: Marco charges $50,000 up front, dropping to $10,000 once you tell
  him "Mrs. Ming sent me". Optional packaged-sample discounts cut the bill further,
  down to the repair-price floor.
- Real carried items in dead drops (Ming's Crate, Marco's Package) cloned from in-game
  items so they have a proper inventory icon and in-hand model. Lose one and the NPC
  charges a $500 recovery fee to continue.
- Repair cinematic when you pay: the screen fades to black, repair sounds play, Marco
  grunts as he works, then it fades back and he tells you it is done. Plays in both
  Questline and Simple mode.
- A wrench quest icon in the journal/HUD tracker.
- Settings: `QuestMode`, `BasePriceNoReferral`, `BasePriceWithReferral`,
  `MinSampleDiscount`, `MaxSampleDiscount` (plus the existing `Enabled`, `RepairPrice`).

### Changed
- Persistence is now bound to the game save (via the S1API save system): progress is
  written when the game saves and read on load, so it stays in sync with the world.
  Finishing the quest without saving (or after a crash) correctly reverts, instead of
  leaving the quest stuck or the RV unrepairable.
- "Simple" mode (`QuestMode = Simple`) repairs directly at Marco for the flat
  `RepairPrice` and now also plays the repair cinematic.
- Repair sounds route through the game audio mixer, so they respect the in-game volume
  settings.

### Fixed
- Delivered quest items now correctly leave the inventory.
- The quest reliably starts once the RV is actually wrecked and the intro has passed,
  and never flickers on an intact RV.
