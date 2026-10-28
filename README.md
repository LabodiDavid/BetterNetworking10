# Better Networking - Valheim 1.0

An updated networking mod for **Valheim 1.0**, based on
[CW-Jesse's Better Networking](https://github.com/CW-Jesse/valheim-betternetworking).

This is not an official continuation of the original project. Version **1.3.1** is compiled against **Valheim 1.0.21** (`network version 41`).

## Do you need this on Valheim 1.0?

Not always. If you have network issues give it a try. It can compress network traffic maybe it help a bit.
Install on both on the clients and on the server too.
The mod has some improvements in the but you can configure to only compress the network data with the VanillaSafe setting.

Valheim 1.0 can work perfectly well without a networking mod, especially with one or two players and stable connections. Start with vanilla networking if your server has no noticeable lag or desync.

This mod can still be useful when:

- several players are online at the same time;
- players explore or use portals together;
- large bases or heavily modified worlds generate many ZDO updates;
- a player with limited upload bandwidth becomes the simulation owner of an area;
- the server or clients benefit measurably from network compression.

It cannot fix every multiplayer problem. Valheim distributes parts of world simulation between peers, so a slow or overloaded player can still cause lag for the area they own.

## Why was a new build needed?

The commonly available tibijczyk release describes itself as a rebuild of the old mod for Valheim `0.221.10`. 
Valheim 1.0 changed relevant networking code, while that rebuild kept the original patch logic.

Simply recompiling the old source is not enough because:

- Steamworks packages left in the send queue could be compressed again after a failed send attempt;
- PlayFab now reports its queue using a scaled `0.25 × inFlightBytes` value, which the old queue patch treated as raw bytes;
- PlayFab's direct compression and reconnect paths changed;
- the old connection buffer used one shared package list instead of keeping buffers separated per connection;
- private Harmony targets can still exist after a game update while their surrounding logic has changed.

This fork updates those paths instead of only rebuilding the old DLL.

## What this version changes

- Compresses each Steamworks packet only once.
- Handles Valheim 1.0's PlayFab worker, direct compression and reconnect paths.
- Changes the ZDO send budget directly instead of spoofing PlayFab's queue measurement.
- Keeps the experimental new-connection ZDO buffer **disabled by default**.
- Uses a separate buffer for every connection when that feature is enabled.
- Verifies sensitive IL patterns before modifying them.
- Logs every patch as `PATCH OK`, `PATCH SKIP` or `PATCH FAILED`.
- Provides a `VanillaSafe` mode for troubleshooting/only compressing without any changes in the networking.
- Optionally synchronizes and locks server networking settings through Conditional Config Sync 1.0.10 or newer.
- Optionally refreshes teleported player and linked status-effect ZDOs to work around portal ghosts, including a magic barrier left at the source portal.

## Installation

1. Remove the old `BetterNetworking.dll` from the server and every client.
2. Copy `DIT.BetterNetworking10.dll` into:

   ```text
   BepInEx/plugins/BetterNetworking_Valheim/
   ```

3. Install it on the dedicated server and on every modded client.
4. Restart the server and all clients completely.

Compression is negotiated per connection and is used only when both peers run this compatible build. Do not keep the old and new Better Networking DLLs installed together.

The configuration file is created after the first launch:

```text
BepInEx/config/DIT.BetterNetworking10.cfg
```

If this is the first launch and `BepInEx/config/CW_Jesse.BetterNetworking.cfg`
exists, compatible settings are imported automatically into the new file. The
legacy file is not modified. The experimental connection buffer is deliberately
left disabled during migration.

The new plugin keeps its own GUID and configuration filename so BepInEx can
reliably detect an accidentally installed old Better Networking DLL. Delete the
new configuration file and restart if you intentionally want to repeat the
legacy import.

## Optional server configuration synchronization

Better Networking works normally without Conditional Config Sync. To let a server synchronize and enforce its networking settings, install
[Conditional Config Sync 1.0.10 or newer](https://thunderstore.io/c/valheim/p/shudnal/ConditionalConfigSync/) separately on the dedicated server and on each managed client.

When CCS is active:

- `Mode`, compression, queue, update-rate and Steam send-rate settings are server-controlled by default;
- `Portal Ghost Fix`, `Force Crossplay`, `Player Limit` and `Lock Configuration` are always server-controlled;
- `New Connection ZDO Buffer` stays client-controlled because it runs before server config synchronization is available;
- logging remains local to each installation;
- Better Networking remains optional on remote peers by default, preserving vanilla-client compatibility.

No CCS policy rule is required for the default setup: keep `Lock Configuration = true` on the server and its synchronized values are authoritative. Setting the lock to `false` deliberately allows ordinary clients to publish changes to server-controlled Better Networking settings.

The server's effective values are applied at runtime where possible. Compression is renegotiated, queue and update-rate getters change immediately, and Steam send-rate limits are refreshed live. `Force Crossplay` still requires a server restart because Valheim chooses the backend during startup.

CCS policy files are stored on the server under:

```text
BepInEx/config/shudnal.ConditionalConfigSync/
```

For example, `ConditionalConfigSync.SyncPolicy.cfg` can release one conditional setting back to clients:

```text
- DIT.BetterNetworking10.03 - Steamworks.Minimum Send Rate
```

Or explicitly enforce a conditional setting from the server:

```text
+ DIT.BetterNetworking10.01 - Features.Compression
```

To require Better Networking on connecting clients, add this to `ConditionalConfigSync.ModRequirements.cfg`:

```text
+ DIT.BetterNetworking10
```

Use `conditionalconfigsync_policy_dump` for copy-ready identifiers and `conditionalconfigsync_policy_validate` after editing policy files. A client without CCS cannot receive synchronized values; it continues using its local Better Networking configuration.

## Recommended setup

For the first test, use the default `Balanced` mode and keep the connection buffer disabled:

```ini
[00 - Compatibility]
Lock Configuration = true
Mode = Balanced

[01 - Features]
Compression = true
Queue Size = true
Update Rate = true
Steam Send Rate = true
New Connection ZDO Buffer = false
Portal Ghost Fix = false

[02 - Networking]
Queue Size = KB32
Update Rate = Percent100
```

`Steam Send Rate` affects Steamworks connections only. It does not affect PlayFab/crossplay networking. Without CCS, use a full server and client restart after networking changes; CCS applies the synchronized runtime-capable settings automatically.

For hosted dedicated servers, prefer `Force Crossplay = Vanilla` and select crossplay in the hosting provider's control panel. If the override is used, Better Networking applies it before Valheim initializes the server. `VanillaSafe` always leaves backend selection to the vanilla command line.

## Optional portal ghost workaround

Enable this on the dedicated server only if players remain visible at the source portal:

```ini
[01 - Features]
Portal Ghost Fix = true
```

The server checks connected player ZDO positions every 0.25 seconds. A movement of at least 64 metres queues the player record and any networked status-effect objects linked to that player for observers again. One delayed refresh is also sent so attached effects such as the magic barrier use their post-teleport position. Vanilla clients are supported.

Remove `Sfantul-PortalGhostFix` when enabling the built-in feature. If it is still installed, Better Networking detects the plugin GUID and leaves its own workaround disabled to prevent duplicate sends.

This feature does not change portal logic, player ownership, status-effect duration, combat state, or world saves.

Armor/item stand attachment delay and chest open-state visuals are not patched in this release. Their Valheim code uses ownership hand-off plus separate visual RPC/ZDO updates, and the current investigation did not show a Better Networking-specific fault. The mod avoids changing those ownership paths without a reproducible failing packet trace.

## Startup log

For Valheim 1.0.21, startup should include:

```text
Valheim target version matched: 1.0.21, compiled against network version 41
Patch audit complete
```

Review any `PATCH FAILED` message before continuing to use the mod after a Valheim update. A newer game version is not automatically guaranteed to be compatible.

## Building from source

The project defaults to:

```text
Valheim:    D:\SteamLibrary\steamapps\common\Valheim
Gale:       Default profile
Plugin:     %APPDATA%\com.kesomannen.gale\valheim\profiles\Default\BepInEx\plugins\BetterNetworking_Valheim
```

Change `ValheimPath` or `R2ProfileName` in `BetterNetworking10.csproj` if required.

A normal Windows build:

- generates the plugin version from `PluginVersion`;
- copies the DLL into the selected Gale profile (disable with `-p:DeployToR2Modman=false`);
- creates `dist\BetterNetworking_Valheim-1.3.1.zip`.

For a portable build, place the required game assemblies in `libs/` and run:

```powershell
dotnet build -c Release -p:UseLocalLibs=true
```

Required assemblies:

- `0Harmony.dll`
- `assembly_utils.dll`
- `assembly_valheim.dll`
- `BepInEx.dll`
- `com.rlabrecque.steamworks.net.dll`
- `PlayFab.dll`
- `PlayFabParty.dll`
- `UnityEngine.dll`
- `UnityEngine.CoreModule.dll`

## Credits and license

Based on CW-Jesse's MIT-licensed Better Networking project. See `LICENSE` and `THIRD-PARTY-NOTICES.md` for details.
