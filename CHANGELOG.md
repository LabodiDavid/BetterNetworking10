# Changelog

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
- Kept the new plugin GUID and clean configuration file to avoid collisions with legacy DLLs.
- Legacy connection buffering is intentionally not imported and remains disabled.
