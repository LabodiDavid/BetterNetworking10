# Changelog

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

