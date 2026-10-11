# Changelog

## 1.3.0

- Added optional support for [Conditional Config Sync](https://thunderstore.io/c/valheim/p/shudnal/ConditionalConfigSync) 1.0.10 and newer.
- Fixes [Issue #2](https://github.com/LabodiDavid/BetterNetworking10/issues/2), where dedicated servers could fail to start correctly when PlayFab or Steamworks was forced in the configuration.

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
