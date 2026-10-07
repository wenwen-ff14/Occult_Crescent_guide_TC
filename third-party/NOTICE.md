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

## BOCCHI authored South Horn route and reference distances (0.10.19)

- Source commit: `400de1ca05a327f016978168560d511e74ff5eed`, retrieved 2026-10-07.
- [BOCCHI.Treasure/Data/SouthHorn/treasure_route.json](https://github.com/OhKannaDuh/BOCCHI/blob/400de1ca05a327f016978168560d511e74ff5eed/BOCCHI.Treasure/Data/SouthHorn/treasure_route.json)
- [BOCCHI.Treasure/Data/SouthHorn/precomputed_treasure_hunt_data.json](https://github.com/OhKannaDuh/BOCCHI/blob/400de1ca05a327f016978168560d511e74ff5eed/BOCCHI.Treasure/Data/SouthHorn/precomputed_treasure_hunt_data.json)
- Authors: OhKannaDuh / BOCCHI contributors. License: AGPL-3.0-or-later; see `BOCCHI-LICENSE.txt`.

These two JSON datasets are redistributed with unchanged data under `CrescentCompass/Data/SouthHorn/` and embedded in the API 13 build. The existing location dataset pin above is unchanged. The route has 68 unique game node IDs in seven segments; IDs are joined to the existing TC-verified catalog, not used as the user's chart labels. The distance table is directed and incomplete for node 1856; missing costs remain unknown. Only node-to-node reference costs are displayed. Aethernet distances and transition metadata are retained in the source dataset but do not trigger teleports or Return actions. No upstream routing implementation or plugin binary is bundled. Reference costs do not certify traversability or replace live mesh queries.

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

## BOCCHI one-shot Magic Pot time protocol (superseded in 0.10.16)

The public read-only pot-cycle protocol was inspected at BOCCHI commit `400de1ca05a327f016978168560d511e74ff5eed` on 2026-10-07 (AGPL-3.0-or-later):

- [PotCycleSyncService.cs](https://github.com/OhKannaDuh/BOCCHI/blob/400de1ca05a327f016978168560d511e74ff5eed/BOCCHI.Common/Services/PotCycleSyncService.cs)
- [PotCycleTracker.cs](https://github.com/OhKannaDuh/BOCCHI/blob/400de1ca05a327f016978168560d511e74ff5eed/BOCCHI.Common/Data/Zones/PotCycleTracker.cs)
- [FateRepository.cs](https://github.com/OhKannaDuh/BOCCHI/blob/400de1ca05a327f016978168560d511e74ff5eed/BOCCHI.World/Services/FateRepository.cs)
- [Public Worker API](https://github.com/OhKannaDuh/BOCCHI/blob/400de1ca05a327f016978168560d511e74ff5eed/cloudflare/coffer-api/src/index.ts)

CrescentCompass independently implements one GET to `/api/v1/pot-cycles?instanceKey=...` per island entry at most. The compatible fingerprint hashes three little-endian Int32 values: current datacenter, oldest active FATE ID, and its start epoch. No upstream source or binaries are bundled for this feature. The client does not implement uploads, installation IDs, polling, retries, or EurekaLinker IPC. The location dataset pins above are unchanged. Shared timestamps are untrusted estimates, validated against this entry and superseded by local observations; availability for the TC client remains unverified in game.

## Patrol and opening research (0.10.15)

The following BOCCHI sources at commit `400de1ca05a327f016978168560d511e74ff5eed` (AGPL-3.0-or-later) were inspected on 2026-10-07:

- `BOCCHI.Treasure/Hunt/HuntRoutePlanner.cs`, `AuthoredTreasureRoute.cs`, `EmptyPadConfirm.cs`, `HuntDistances.cs`, `WalkStuckWatch.cs`.
- `BOCCHI.Treasure/Services/TreasureHunterService.cs`, `TreasurePathing.cs` and `BOCCHI.Common/Config/TreasureConfig.cs`.
- `BOCCHI.Treasure/ChainRecipes/OpenTreasureCofferChain.cs` and `BOCCHI.Treasure/Services/TreasureCoffer.cs`.

CrescentCompass independently adapts the authored-order, local recovery, consecutive empty-pad confirmation, stop-before-interaction and loot-state ideas to its API 13 implementation. Its route order remains the user's 68-point chart. It uses one-leg lookahead and a bounded directed local mesh cache, not upstream route files or teleport actions. Early absence is more conservative: 20m near range or up to 60m with a nearby streamed coffer, verified ground distance, and three fresh scans over at least one second. It retains line-of-sight checks, three-dimensional interaction distance and excludes unverified off-mesh straight-line finishing. No upstream source code or binaries are bundled for these changes.

## Obstacle recovery research (0.10.18)

Inspected on 2026-10-07 at the same BOCCHI commit `400de1ca05a327f016978168560d511e74ff5eed` (AGPL-3.0-or-later):

- [StuckJumpAssist.cs](https://github.com/OhKannaDuh/BOCCHI/blob/400de1ca05a327f016978168560d511e74ff5eed/BOCCHI.Common/Services/StuckJumpAssist.cs) and `BOCCHI.Common/Config/MovementConfig.cs`: movement-progress checks, a default three-second jump delay, bounded jump attempts and cooldowns.
- `BOCCHI.Common/Data/Zones/PathfindingNudge.cs`, `BOCCHI.Treasure/Services/TreasureHunterService.cs`, `BOCCHI.Treasure/Services/TreasurePathing.cs`, `BOCCHI.Treasure/Hunt/TreasureHuntPathOverrides.cs`: lateral recovery, mesh projection, and location-specific via points.
- BOCCHI's Ocelot submodule at `ad3ab3093a72353b9109e56b759a6d4bf104cf24`: `Ocelot/Actions/Actions.General.cs`, `Action.cs`, `ActionCastScope.cs` and `Ocelot/Ipc/VNavmesh/VNavmeshIpc.cs` for the GeneralAction jump call and public navigation interface signatures.
- Official vnavmesh [IPCProvider.cs](https://github.com/awgil/ffxiv_navmesh/blob/master/vnavmesh/IPCProvider.cs) and [FollowPath.cs](https://github.com/awgil/ffxiv_navmesh/blob/master/vnavmesh/Movement/FollowPath.cs) for optional mesh queries, movement ownership limits and the native jump action.

CrescentCompass independently applies these ideas with its existing API 13 patrol state machine: native GeneralAction 2, a five-attempt jump budget, no stuck-deadline reset merely from jumping, and queried 8/10/12m lateral anchors. All actual movement remains mesh-query based. It does not copy North Horn via-point coordinates, upstream combat stopping, monster avoidance, unverified straight-line finishing, or Ocelot's internal action-hook scope. A separate cache fix removes the previous 1.5m unqueried join. No upstream source or binaries are bundled for these changes. Local TC GeneralAction data and SDK signatures were separately checked; in-game traversal remains unverified.

## Carrot patrol research (0.10.21)

Inspected on 2026-10-07 at BOCCHI commit `400de1ca05a327f016978168560d511e74ff5eed` (AGPL-3.0-or-later):

- [CarrotHunterService.cs](https://github.com/OhKannaDuh/BOCCHI/blob/400de1ca05a327f016978168560d511e74ff5eed/BOCCHI.Treasure/Services/CarrotHunterService.cs), `CarrotTracker.cs`, `BOCCHI.Treasure/Data/Carrot.cs`, `Hunt/HuntRoutePlanner.cs` and `BOCCHI.Common/Services/InventoryItemAssist.cs`.

The South Horn hunter builds a dynamic nearest-neighbor/local-cluster tour and can divert to loaded carrots. It is not a fixed 1-25 South Horn route dataset. This version follows the user's supplied numbered map using the already attributed carrot catalog. It independently implements item 48096 use, inventory-decrement confirmation, waiting for a new rabbit coffer 2012936, and handling distinct live carrots at the same pad. It does not copy upstream implementation, North Horn regional routing, teleport/Return actions or cloud carrot synchronization. No BOCCHI runtime dependency is added. The local 0/1/2 search-weight model comes from the user's specification, not BOCCHI or a guaranteed server probability model. TC item names and the SDK UseItem signature were independently checked against local game data; game assets are not bundled. See `docs/CARROT_PATROL.md` for limitations.

## Waymark format and native interop references (0.9.0)

Public preset field names and ContentFinderCondition mapping were checked against [sourpuh/WaymarkPresetPlugin](https://github.com/sourpuh/WaymarkPresetPlugin/tree/6219d87cb3ceb425efd90351191978aa9d64b87e). Ground-ray material flags and individual-marker flow were researched in [sourpuh/ffxiv_waymarkstudio](https://github.com/sourpuh/ffxiv_waymarkstudio/tree/c1a519419e87a766efc10805a38f02ebcf9fde7f) and FFXIVClientStructs. The implementation, storage format, UI, TC client audit and tests are written for this repository. No upstream plugin binaries or source files are bundled. TC addresses and layout were separately verified against the user's local executable; game files are not redistributed. See `docs/WAYMARKS.md`.

## Tower arena geometry references (0.9.5)

Static horizontal centers, dimensions and platform placements were checked against [awgil/ffxiv_bossmod at 40b0abdd35e81517409bdc81446910f943a75a49](https://github.com/awgil/ffxiv_bossmod/tree/40b0abdd35e81517409bdc81446910f943a75a49/BossMod.Modules/Dawntrail/Foray/ForkedTower). Only geometric facts are included; no upstream source code, textures or combat logic are bundled. Rendering and detection code is original. See `docs/WAYMARK_PREVIEW.md` for files and limitations.

## Build references

Phantom job names, row IDs and status icon mappings in 0.9.6 were verified against the user's local TC client. Status textures are loaded through Dalamud from the game installation; standalone game textures are not bundled. Documentation screenshots show the plugin UI using locally rendered icons. Native switching uses the bundled FFXIVClientStructs `AgentMKDSupportJobList.ChangeSupportJob` API. The command parser, state confirmation and UI were implemented for this project; no third-party plugin source was copied. See `docs/PHANTOM_JOBS.md`.

Dalamud, Lumina, ImGui bindings and FFXIVClientStructs are provided by the user's launcher. Copies in `.sdk/` are local compilation references only and must not be included in release or source archives.

## OccultOverlay / Eureka Linker read-only protocol (0.10.16)

Inspected on 2026-10-07:

- [OccultOverlay api.js](https://github.com/zhui-zi/OccultOverlay/blob/021161b44a41ec1aae6dd01bd407523e94070d32/js/api.js), plus data.js and pots.js at the same commit.
- [Eureka Linker TrackerHandler.cs](https://github.com/Infiziert90/EurekaTrackerAutoPopper/blob/3243b1346cd4cd6900b1028c04caf420c602c648/EurekaTrackerAutoPopper/TrackerHandler.cs).

Both clients use the public OccultTrackerV3 backend at https://infi.ovh/api/OccultTrackerV3. This implementation is independently written against its read-only GET protocol. It uses the upstream public anonymous client key, not a player credential; no original client implementation is copied. Fingerprints are SHA256 of DC/FATE/epoch as three little-endian 32-bit integers. Stored pot_history is normally a JSON-encoded string; the parser also accepts a JSON array, as the overlay does.

CrescentCompass makes at most one entry request, with bounded exact fingerprints plus territory/DC filters. It never creates/updates trackers, uploads observations, polls, or retries another provider. Multiple matching rows are rejected. Returned spawn time seeds only the existing local estimate after identity, age and pot ID validation. The backend sees the connection IP and scope/hash query; no player name, Content ID, position, server hook ID, installation ID or local pot observations are sent. Availability of TC/DC 151 records is not established by source inspection.
