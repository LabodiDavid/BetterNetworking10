# Changelog

## 1.3.0

- Apply a forced PlayFab or Steamworks backend before `ZNet.SetServer` initializes the server, matching Valheim's native startup order.
- Removed the late manual `ZPlayFabMatchmaking.LookupPublicIP()` call that could leave dedicated servers in a partially initialized backend state.
- `VanillaSafe` now leaves dedicated-server backend selection on the vanilla command-line setting.
- Added optional Conditional Config Sync 1.0.10+ integration without making it a required dependency.
- Server-controlled networking settings can now be synchronized and locked for clients that also have Conditional Config Sync installed.
- Added Conditional Config Sync policy support so administrators can release individual conditional settings or require Better Networking on connecting clients.
- Made compression negotiation, queue budget, update rate and Steam send-rate handling respond safely to synchronized runtime changes.
- Kept the experimental early-connection ZDO buffer client-controlled because it runs before server configuration synchronization is available.

## 1.2.0

- Added an optional, server-side portal ghost fix workaround based on Sfantul's observer refresh approach and automatically disables it if it's already installed.
- Portal Ghost Fix: Refreshes networked status-effect objects linked to a teleported player, preventing effects such as the magic barrier from remaining at the source portal.
- Recognizes Valheim 1.0.12 through 1.0.15 with network version 40 as the currently audited compatibility range.

## 1.1.0

- Added an explicit compression frame header instead of detecting compressed packets by trial decompression.
- Prevented a packet from being framed or compressed more than once.
- Skips compression when it would make a packet larger.
- Bumped the compression protocol to version 8; older builds remain connected but do not negotiate compression.
- Added a startup round-trip self-test which leaves compression disabled if framing or the codec fails.


## 1.0.0

- Targeted Valheim 1.0 build.
- Replaced queue-rewriting Steamworks compression with compress-once package handling.
- Updated PlayFab worker locking and direct compression/recovery paths for Valheim 1.0.
- Replaced transport-specific queue-size spoofing with a guarded `ZDOMan.SendZDOs` budget patch.
- Added Balanced and VanillaSafe compatibility modes.
- Added independent feature switches and startup patch auditing.
- Disabled the new-connection ZDO buffer by default and made its storage connection-scoped.
- Added a one-time import from `CW_Jesse.BetterNetworking.cfg` on first launch.
- Legacy connection buffering is intentionally not imported and remains disabled.
