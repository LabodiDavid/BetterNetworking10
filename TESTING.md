# Portal ghost A/B test

1. Remove/disable the old `BetterNetworking.dll` on the server and every client.
2. Install `DIT.BetterNetworking10.dll` everywhere and restart all processes.
3. Start with the default `Balanced` mode. Leave `New Connection ZDO Buffer = false`.
4. With two players, have one player watch the departure portal while the other crosses it 20 times in both directions.
5. Check the BepInEx log for `Patch audit complete` and any `PATCH FAILED` lines.

If a ghost remains, change the server and every client to:

```ini
[00 - Compatibility]
Mode = VanillaSafe
```

Restart everything and repeat. This keeps only negotiated compression.

If it still happens, set:

```ini
[01 - Features]
Compression = false
```

After another restart the mod makes no gameplay-network changes unless dedicated-server player limit/crossplay settings were changed. If the ghost still occurs, the cause is outside Better Networking.

For feature isolation after a clean VanillaSafe test, return to `Balanced` and enable one change at a time, restarting server and clients between tests.

