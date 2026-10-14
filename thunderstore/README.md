# RVRepairVan - Get Your RV Back on the Road

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/rvrepairvan](https://support.doodesch.de/rvrepairvan).

> Your RV got blown up in the intro? Now you can actually fix it. A proper tracked side
> quest sends you from the motel manager to Mrs. Ming to Marco the mechanic, with real
> errands, referral haggling, and a little repair cinematic at the end.

![Version](https://img.shields.io/badge/version-3.0.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![S1API](https://img.shields.io/badge/S1API-required-orange)

[![MLVScan RVRepairVan.dll](https://mlvscan.com/attestations/att_RhcyMUaP3ywqKoI8_vN9ayEg/badge.svg)](https://mlvscan.com/attestations/att_RhcyMUaP3ywqKoI8_vN9ayEg)

## Features

- **"Back on the Road" questline.** After the RV is wrecked in the intro, the quest starts
  on its own: motel manager (Donna) -> Mrs. Ming (a dead-drop crate errand) -> Marco the
  mechanic at the docks. Native S1API dialogue, journal and HUD tracker.
- **Referral pricing.** Marco quotes $50,000; say "Mrs. Ming sent me" and it drops to
  $10,000. Earn his trust to hand over packaged samples for further discounts, down to the
  floor.
- **Real carried items** in dead drops (a crate, a package). Lose one and the NPC charges a
  $500 recovery fee.
- **Repair cinematic** when you pay: fade to black, repair sounds, Marco grumbling, fade
  back, done. Plays in both modes.
- **Save-bound persistence** - progress lives in the game save and stays in sync with the
  world.
- **Simple mode** (`QuestMode = Simple`): skip the story, just pay Marco the flat price.
- Configurable prices and discounts, updated live.

## Requirements

- **Schedule I** `0.4.6f11` (IL2CPP) with **MelonLoader 0.7.3+**.
- **S1API 3.2.1+** (pulled in as a dependency).

## Settings

`Enabled`, `QuestMode` (Questline / Simple), `RepairPrice` (1500, Simple + floor),
`BasePriceNoReferral` (50000), `BasePriceWithReferral` (10000), `MinSampleDiscount` (100),
`MaxSampleDiscount` (500). Editable in
`UserData/MelonPreferences.cfg`.

## License

MIT. See the included LICENSE.md.
