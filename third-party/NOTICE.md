# Third-party notices and data provenance

## User-supplied South Horn numbered chart (0.6.0)

Chart URL: https://media.discordapp.net/attachments/1530579815076204644/1530581321917792346/9c36647eec4c9eb3.jpg

Image credits: Map created by Spiral Lance (Coeurl); pathing created by Selene Amaris (Omega) and Spiral Lance (Coeurl). The user requested matching its numbered order. We read the 68 labels and matched them to the existing TC-verified coffer catalog; no original image is redistributed. `docs/audit/chest-chart-mapping.json` records the factual number-to-catalog correspondence. The existing BOCCHI location dataset and licensing remain unchanged. Ground paths are queried locally from vnavmesh; the image's green path artwork is not copied.

## BOCCHI location datasets

- Project: Better Occult Crescent & Chest Helper Interface (BOCCHI)
- Authors: OhKannaDuh / BOCCHI contributors (repository manifest credits Faye and Kage)
- Repository: https://github.com/OhKannaDuh/BOCCHI
- Pinned commit: `aa4f6efce52d78c3c3992e88d2bcd7bb218904f2`
- License: GNU Affero General Public License v3; original license text retained in `BOCCHI-LICENSE.txt`.
- Retrieved: 2026-09-29

The following JSON files are redistributed unmodified under their original license:

- `BOCCHI.Treasure/Data/SouthHorn/carrot_locations.json`
- `BOCCHI.Treasure/Data/SouthHorn/treasure_locations.json`
- `BOCCHI.Treasure/Data/NorthHorn/carrot_locations.json`
- `BOCCHI.Treasure/Data/NorthHorn/treasure_locations.json`

They are embedded in the plugin as candidate coordinates. They are not evidence of current spawns, nor a completeness guarantee for any client version.

## Magic Pot candidate data (0.3.0)

`Data/{SouthHorn,NorthHorn}/pot_candidates.json` is adapted from the same pinned BOCCHI commit:

- `BOCCHI.Common/Data/Zones/Implementations/SouthHorn/SouthHorn.cs`
- `BOCCHI.Common/Data/Zones/Implementations/NorthHorn/NorthHorn.cs`

The unmodified source files are retained in `third-party/BOCCHI-source/` in the source archive. `scripts/Import-PotCandidates.ps1` extracts `GetPotChestData()` and `GetRerollPotChestData()`, preserving primary/bonus membership and authored coordinates. South has 80 entries; North has 83 entries / 82 unique positions. Source license: AGPL-3.0-or-later.

Status 1531, pot event objects 2014741–2014743 and direction semantics were cross-referenced with BOCCHI's `PotTreasureTypes.cs`, `PotTreasureHintTracker.cs` and `PotTreasureFilter.cs` at that commit. The local TC client confirms status/object names and localized hint templates. Distance boundaries remain unknown; no distance exclusion is implemented.

## Local static-scene audit (0.3.0)

`Data/SouthHorn/tower_locations.json` contains coordinate/instance-ID facts extracted from the user's installed TC client `2026.09.14.0000.0000`, `planmap.lgb` / `BA_treasure`. No game textures, models, audio, executable code or sqpack archives are distributed. See `docs/COVERAGE_AUDIT.md` for scope and limitations.

## Local exploration records and personal count templates (0.4.0)

`Data/SouthHorn/exploration_locations.json` contains 13 location names, IDs and coordinate facts from TC client `2026.09.14.0000.0000`. `tools/GameDataAudit/SightseeingAudit.cs` joins `MKDLore.Unknown4` to island LGB `EventObject.InstanceId`; the library record 30 is annotated as requiring tower access. No outside-island Adventure entries or Phantom Village location are included. No game assets or general record descriptions are distributed.

Personal count text templates are read from local `LogMessage` rows 10965/10966 (system channel 57). Their silver/bronze count meanings were cross-checked against the pinned BOCCHI `BOCCHI.Treasure/Services/TreasureTracker.cs`; the parser was written for this project's API 13 text event. These counts do not provide remote spawn coordinates or ownership guarantees.

## Identification reference

Game data constants were cross-referenced with BOCCHI and the following technical reference:
https://github.com/Sansflaire/LimLoToolkit/blob/main/docs/occult-crescent.md

- Occult territories: 1252 (South Horn), 1346 (North Horn).
- Carrot event object BaseId: 2010139.
- Bronze and silver Treasure SGB row IDs: 1596 and 1597.

The implementation was written independently for this project. The plugin resolves Treasure models through the local Lumina sheet and obtains map metadata from the current client; no displayed Chinese object names are used for detection.

## Magic Pot FATE timing references (0.4.4)

The four FATE IDs and alternate-side pairings were cross-checked against the already retained BOCCHI zone sources. TC client `2026.09.14.0000.0000` confirms Fate rows 1976 and 1977; rows 2072 and 2073 are absent locally.

The estimated 30-minute alternating schedule is a factual reference from [Wintaru/octracker](https://github.com/Wintaru/octracker#the-fate-schedule), accessed 2026-09-30. No implementation or assets from that project are incorporated. Countdown and notification logic are original to this project and use Dalamud API 13 IFateTable observations. This community timing model is not an official timing guarantee; see `docs/POT_FATE_TIMERS.md`.

## Fixed FATE locations (0.4.5)

The fixed FATE locations added in 0.4.5 use TC `Fate.Location` -> `planevent.lgb` instance 11191083 / 11264027 for South Horn (Map 967). Only coordinate facts are included. North Horn coordinates come from `GetPotFateData()` in the retained BOCCHI zone source at the pinned commit above, under the same AGPL license; these are not locally validated.

## Waymark format and native interop references (0.9.0)

Public preset field names and ContentFinderCondition mapping were checked against [sourpuh/WaymarkPresetPlugin](https://github.com/sourpuh/WaymarkPresetPlugin/tree/6219d87cb3ceb425efd90351191978aa9d64b87e). Ground-ray material flags and individual-marker flow were researched in [sourpuh/ffxiv_waymarkstudio](https://github.com/sourpuh/ffxiv_waymarkstudio/tree/c1a519419e87a766efc10805a38f02ebcf9fde7f) and FFXIVClientStructs. The implementation, storage format, UI, TC client audit and tests are written for this repository. No upstream plugin binaries or source files are bundled. TC addresses and layout were separately verified against the user's local executable; game files are not redistributed. See `docs/WAYMARKS.md`.

## Tower arena geometry references (0.9.5)

Static horizontal centers, dimensions and platform placements were checked against [awgil/ffxiv_bossmod at 40b0abdd35e81517409bdc81446910f943a75a49](https://github.com/awgil/ffxiv_bossmod/tree/40b0abdd35e81517409bdc81446910f943a75a49/BossMod.Modules/Dawntrail/Foray/ForkedTower). Only geometric facts are included; no upstream source code, textures or combat logic are bundled. Rendering and detection code is original. See `docs/WAYMARK_PREVIEW.md` for files and limitations.

## Build references

Phantom job names, row IDs and status icon mappings in 0.9.6 were verified against the user's local TC client. Status textures are loaded through Dalamud from the game installation; standalone game textures are not bundled. Documentation screenshots show the plugin UI using locally rendered icons. Native switching uses the bundled FFXIVClientStructs `AgentMKDSupportJobList.ChangeSupportJob` API. The command parser, state confirmation and UI were implemented for this project; no third-party plugin source was copied. See `docs/PHANTOM_JOBS.md`.

Dalamud, Lumina, ImGui bindings and FFXIVClientStructs are provided by the user's launcher. Copies in `.sdk/` are local compilation references only and must not be included in release or source archives.
