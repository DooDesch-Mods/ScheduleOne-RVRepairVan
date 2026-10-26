# RVRepairVan - Get Your RV Back on the Road

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/rvrepairvan](https://support.doodesch.de/rvrepairvan).

> Your RV got blown up in the intro? Now you can fix it. A tracked side quest sends you
> from Donna at the motel to Mrs. Ming to Marco the mechanic, with dead-drop errands, a
> haggled price and a repair cinematic. After that, Marco turns the RV into a workshop. Built on
> [S1API](https://github.com/ifBars/S1API).

![Version](https://img.shields.io/badge/version-3.6.4-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![S1API](https://img.shields.io/badge/S1API-required-orange)
![Status](https://img.shields.io/badge/status-working-brightgreen)
[![Polyfill](https://polyfill.doomods.com/badge/rvrepairvan.svg)](https://polyfill.doomods.com/mod/rvrepairvan)

[![MLVScan RVRepairVan.dll](https://mlvscan.com/attestations/att_aJgzQiXZ2BNt7nfGqQxxsLOg/badge.svg)](https://mlvscan.com/attestations/att_aJgzQiXZ2BNt7nfGqQxxsLOg)

![The RV driving through town at dusk](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-RVRepairVan/main/docs/img/header.jpg)

## Features

- **"Back on the Road" questline** (default). After the RV is wrecked in the intro, the
  quest starts on its own. Donna at the motel sends you to **Mrs. Ming**. She wants a crate
  from a **dead drop** first, then she calls **Marco** at the docks.
- **Native dialogue** via S1API: choices inside the normal conversation, plus journal
  entries and the on-screen quest tracker.
- **Haggling.** Marco normally charges **$50,000**. Tell him Mrs. Ming sent you and it is
  **$10,000**. Ask *"Can you go any lower?"* and he sends you for a package of his. After
  that, every packaged product you hold out to him takes up to $500 off, down to a floor.
- **Carried quest items.** The crate and the package are items with an icon and an in-hand
  model. Lose one and the owner wants **$500** before the quest goes on.
- **Repair cinematic.** You pay, the screen goes black, Marco works on the RV, and it fades
  back in repaired. Plays in both modes.
- **Marco's build-outs.** Once the RV stands again, he sells three upgrades in his dialogue,
  each with its own short cinematic:
  - **Get the engine running** ($10,000): take the wheel and drive the RV anywhere. Parked
    away, it keeps its stations; the crew and deliveries wait until it is back on its spot.
  - **Make room for a crew** ($15,000): three employees work inside. Put a locker in. Needs
    the workshop floor.
  - **Build a loading dock** ($25,000): your supplier delivers to the RV.
- **Marco's Bodyshop.** Drive the RV onto the lot in front of his garage and ask him
  to modify your vehicle:
  - **Interior, part by part**: bed, kitchen bench, upper and lower cabinets, both walls,
    bedside table and the decor. Each part is paid once ($150 to $450); after that, taking
    it out or putting it back is free. A part goes back only on an empty floor.
  - **Workshop floor** ($7,500): build on every bit of floor the furniture leaves free.
  - **Engine parts** from Chassis, as for every car.
- **Save intercom.** The repaired RV has the wall intercom every property has. Press E on it
  to save.
- **Save-bound progress.** Quest progress and the repaired RV live in the game save. Quit
  without saving and they go back with the world.
- **Simple mode** (`QuestMode = Simple`): skip the story, talk to Marco, pay the flat price.
- **Live settings.** Change a price and Marco's offer updates without a restart.
- **English and German, and your language.** German on a German system, English
  everywhere else. Add or fix a translation with a JSON file, see Configuration.

## Requirements

| Component | Version / Source |
|-----------|------------------|
| Schedule I | `0.4.7f12` (IL2CPP, current Steam public build) |
| MelonLoader | `0.7.3+` |
| S1API | [ifBars/S1API_Forked](https://thunderstore.io/c/schedule-i/p/ifBars/S1API_Forked/) **3.2.1+** (dialogue, items, dead drops, money, save system) |
| Chassis | [DooDesch/Chassis](https://thunderstore.io/c/schedule-i/p/DooDesch/Chassis/) **1.7.0+** (driver in the seat, first person, horn, indicators) |
| Marco's Bodyshop | [DooDesch/MarcosBodyshop](https://thunderstore.io/c/schedule-i/p/DooDesch/MarcosBodyshop/) **1.0.0+** (the garage menu for the interior and the workshop floor) |
| Sideload | [DooDesch/Sideload](https://thunderstore.io/c/schedule-i/p/DooDesch/Sideload/) **1.35.0+** (draws the garage menu) |

## Installation

### Recommended: a Thunderstore mod manager

Install with a mod manager (r2modman / Gale) from the Schedule I community; the
dependencies (MelonLoader, S1API, Chassis, Marco's Bodyshop, Sideload) are pulled in automatically.

### Manual

1. Install **MelonLoader 0.7.3** for Schedule I.
2. Install **S1API** (its DLLs go in `Mods/` and `Plugins/` per its own instructions).
3. Install **Chassis**, **Sideload** and **Marco's Bodyshop** per their own instructions.
4. Drop **`RVRepairVan.dll`** into your Schedule I `Mods/` folder.

## Configuration

Settings live in
`UserData/MelonPreferences.cfg` under `RVRepairVan_01_Main`.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Master on/off for the repair feature. |
| `QuestMode` | `Questline` | `Questline` = the full Donna -> Ming -> Marco story. `Simple` = just talk to Marco and pay. |
| `RepairPrice` | `1500` | Simple-mode price, and the price floor the questline can never go below. |
| `BasePriceNoReferral` | `50000` | Questline price if you walk straight to Marco. |
| `BasePriceWithReferral` | `10000` | Questline price after you drop Mrs. Ming's name. |
| `MinSampleDiscount` | `100` | Smallest price cut a single packaged sample can give. |
| `MaxSampleDiscount` | `500` | Largest price cut a single packaged sample can give. |
| `UpgradesEnabled` | `true` | Marco's build-outs. Off = the RV stays a plain shell. |
| `PriceInteriorBed` | `450` | Bodyshop: unlock the bed once. |
| `PriceInteriorKitchen` | `400` | Bodyshop: unlock the kitchen bench once. |
| `PriceInteriorUpperCabinets` | `300` | Bodyshop: unlock the upper cabinets once. |
| `PriceInteriorLowerCabinets` | `350` | Bodyshop: unlock the lower cabinets once. |
| `PriceInteriorBedroomWall` | `250` | Bodyshop: unlock the bedroom wall once. |
| `PriceInteriorCabWall` | `250` | Bodyshop: unlock the wall behind the driver once. |
| `PriceInteriorBedsideTable` | `150` | Bodyshop: unlock the bedside table once. |
| `PriceInteriorDecor` | `350` | Bodyshop: unlock the decor once. |
| `PriceWorkshopFloor` | `7500` | Bodyshop: the build grid covers the free floor. |
| `PriceCrewQuarters` | `15000` | Lets employees be stationed at the RV. Needs the workshop floor. |
| `PriceLoadingDock` | `25000` | Adds a delivery dock. |
| `PriceEngine` | `10000` | Makes the RV drivable. Away from its spot, the crew and deliveries wait. |
| `CrewSize` | `3` | How many employees the RV holds once the crew quarters are built. |
| `BuildGridExtraTiles` | `96` | Upper limit for the tiles the workshop floor lays on top of the RV's own 32. |
| `InstantRepair` | `false` | Repairs the RV free of charge as soon as it is wrecked. |
| `RepairTakesADay` | `false` | Marco keeps the RV for 24 in-game hours and texts you when it is done. |

The language lives in its own shared section (`DooDesch` - all DooDesch mods with
translations read the same value):

| Setting | Default | What it does |
|---|---|---|
| `Language` | `auto` | `auto` = follow the OS language (German -> German, otherwise English), or force a code like `en` / `de` / `fr`. Takes effect after a restart. |

### Add your own translation

Want the quest in a language the mod doesn't ship, or prefer different wording? After
one game start you'll find templates in `UserData/DooDesch/Localization/RVRepairVan/` -
`_template.en.json` with every translatable line, plus one per shipped language (e.g.
`_template.de.json` with the built-in German). Copy one, rename the copy to your
language code (e.g. `fr.json`), translate or tweak the values, restart. Your file wins
over the built-in texts.

Translations can also be installed as their own mods (a code-less package shipping
`Mods/Localization/RVRepairVan/<code>.json`) - and you can publish yours the same way.
Step-by-step guides: the [ScheduleOne-L10n wiki](https://docs.doodesch.de/mods/l10n/).

Debug-only toggles (Destroy RV, Add $10,000, Dump state, Play repair cinematic) exist only
in development builds and are not shipped in the release.

## Usage

1. Play through the intro until the RV is wrecked and the early quests have begun.
2. The **"Back on the Road"** quest appears and points you to the **motel manager**.
3. Follow it: Donna -> **Mrs. Ming** (fetch her crate from the dead drop) -> **Marco** at
   the docks. Tell him Mrs. Ming sent you.
4. Optional: ask him to go lower, fetch his package, then hold out packaged product to
   him for more off the bill.
5. **Pay**, watch the repair, then go **see your RV**. Marco texts you about his build-outs.
6. For the interior and the workshop floor: buy the engine, drive the RV onto the lot
   in front of Marco's garage, get out and ask him to modify your vehicle.

Prefer no story? Set `QuestMode = Simple` and just pay Marco directly.

## Screenshots

| Before | After |
|---|---|
| ![Wrecked RV](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-RVRepairVan/main/docs/img/wreck.jpg) | ![Repaired RV](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-RVRepairVan/main/docs/img/repaired.jpg) |
| ![Crew at work in the RV](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-RVRepairVan/main/docs/img/crew.jpg) | ![Gutted interior with the dash kept](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-RVRepairVan/main/docs/img/interior.jpg) |

## Compatibility

- Disable other RV-repair or Marco-dialogue mods to avoid duplicate or conflicting choices.
- IL2CPP build only (current Steam public branch). A Mono build for the alternate branch is
  planned.

## Credits

- **DooDesch** - mod author.
- **[ifBars/S1API](https://github.com/ifBars/S1API)** - the modding API this is built on.

## License

Provided as-is under the [MIT License](LICENSE.md).
