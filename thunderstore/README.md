# RVRepairVan - Get Your RV Back on the Road

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/rvrepairvan](https://support.doodesch.de/rvrepairvan).

> Your RV got blown up in the intro? Now you can fix it. A tracked side quest sends you
> from Donna at the motel to Mrs. Ming to Marco the mechanic, with dead-drop errands, a
> haggled price and a repair cinematic. After that, Marco turns the RV into a workshop.

![Version](https://img.shields.io/badge/version-3.5.7-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![S1API](https://img.shields.io/badge/S1API-required-orange)
[![Polyfill](https://polyfill.doomods.com/badge/rvrepairvan.svg)](https://polyfill.doomods.com/mod/rvrepairvan)

[![MLVScan RVRepairVan.dll](https://mlvscan.com/attestations/att_c-S3YHzZHUZjPCe1WULpNSmT/badge.svg)](https://mlvscan.com/attestations/att_c-S3YHzZHUZjPCe1WULpNSmT)

![The RV driving through town at dusk](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-RVRepairVan/main/docs/img/header.jpg)

## Features

- **"Back on the Road" questline.** After the RV is wrecked in the intro, the quest starts
  on its own: Donna at the motel -> Mrs. Ming (a dead-drop crate) -> Marco at the docks.
  Native S1API dialogue, journal and quest tracker.
- **Haggling.** Marco normally charges $50,000. Mrs. Ming's name gets it to $10,000. Ask
  him to go lower, fetch his package, then hold out packaged product for up to $500 off each.
- **Carried quest items** in dead drops (a crate, a package). Lose one and the owner wants
  $500.
- **Repair cinematic** when you pay. Plays in both modes.
- **Marco's build-outs** once the RV stands again: clear out the inside ($2,500), a work
  floor with 24 more build tiles ($7,500), room for a crew of three ($15,000), a loading
  dock for your supplier ($25,000), and an engine to drive the RV anywhere ($10,000).
- **Save intercom** on the RV wall, like in every other property.
- **Save-bound progress** - quest and repair live in the game save.
- **Simple mode** (`QuestMode = Simple`): skip the story, just pay Marco the flat price.

![Wrecked RV](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-RVRepairVan/main/docs/img/wreck.jpg)
![Repaired RV](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-RVRepairVan/main/docs/img/repaired.jpg)
![Crew at work in the RV](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-RVRepairVan/main/docs/img/crew.jpg)

## Requirements

- **Schedule I** `0.4.7f12` (IL2CPP) with **MelonLoader 0.7.3+**.
- **S1API 3.2.1+** (pulled in as a dependency).
- **Chassis 1.0.0+** (pulled in as a dependency).

## Settings

`Enabled`, `QuestMode` (Questline / Simple), `RepairPrice` (1500, Simple + floor),
`BasePriceNoReferral` (50000), `BasePriceWithReferral` (10000), `MinSampleDiscount` (100),
`MaxSampleDiscount` (500), `UpgradesEnabled`, one price per build-out, `CrewSize` (3),
`BuildGridExtraTiles` (24), `InstantRepair`, `RepairTakesADay`. Editable in
`UserData/MelonPreferences.cfg`.

## License

MIT. See the included LICENSE.md.
