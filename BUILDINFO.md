# Build information

- Mod version: `1.3.1`
- Target game version read from the installed game assembly: `Valheim 1.0.21`
- Target network version: `41`
- Static checks: Harmony target signatures (including all three PlayFab socket constructors), reflected PlayFab fields and helper methods are present. `ZDOMan.SendZDOs` retains exactly two `10240` constants; `ZNet.RPC_PeerInfo` retains one player-count guard followed by `10`.
- Build validation: Release build against the installed 1.0.21 assemblies, with no warnings or errors.
- Runtime validation on 1.0.21: pending. Before publishing, check startup patch auditing, Steamworks and PlayFab/crossplay connections, compression negotiation, dedicated backend selection and optional Conditional Config Sync.
- Historical runtime evidence: Valheim 1.0.15 startup log (`15 applied, 4 skipped, 0 failed`); this does not validate 1.0.21.
- `assembly_valheim.dll` SHA-256: `9f959c0396ec4240d47fcbe5d0d062c428cc627244506db5ea567f9d77b2b769`
- `BepInEx.dll` SHA-256: `36bdb9af2b8f2eda4c911d2a49c2a05a4f643637ad724b876d292a9a5fcd8ae6`
- `0Harmony.dll` SHA-256: `1a21cc03424fc82c3dd1346905d16494536b9595ae4162228d99fb7c285c1031`
- Optional Conditional Config Sync adapter source: upstream commit `3b0adea29f265f65a5a0bb09af7767ac0c6ffc27`, upstream SHA-256 `7ed0501042d24340fbaf357b24b58ad8457bd1bdae908edf36e084cbc6432a14`; adapted only to disambiguate Valheim's global `Version` type on `net472`

The release DLL was compiled directly against these installed assemblies. Game updates may change private methods or IL patterns; startup patch auditing is designed to fail closed and preserve vanilla behavior when a target no longer matches. The game's network version is a compile-time constant, so startup output labels it as the compilation target rather than claiming to read it dynamically from the running game.
