# Build information

- Mod version: `1.3.2`
- Target game version read from the installed game assembly: `Valheim 1.0.31`
- Target network version: `42`
- Static checks (re-audited against the 1.0.31 assemblies): Harmony target signatures (including all three PlayFab socket constructors), reflected PlayFab fields and helper methods are present. `ZDOMan.SendZDOs` retains exactly two `10240` constants; `ZNet.RPC_PeerInfo` retains one player-count guard followed by `10`; `ZDOMan.ZDOPeer` remains public, which does not affect any target. The only relevant change in `assembly_valheim` from 1.0.30 is `Version.CurrentVersion` (`1.0.30` -> `1.0.31`); all patch targets and IL patterns are unchanged. No patch-logic changes were required.
- Build validation: Release build against the installed 1.0.31 assemblies, with no warnings or errors.
- Runtime validation: the 1.3.2 build was tested on a live Valheim 1.0.30 server with a Steam client (21/21 patches applied, compression negotiated in both directions). The installed 1.3.1 build confirms the same 21-patch set on 1.0.31; 1.0.31 keeps network version 42 and changes only `Version.CurrentVersion` in the mod-relevant assemblies.
- Historical runtime evidence: Valheim 1.0.15 startup log (`15 applied, 4 skipped, 0 failed`); this does not validate 1.0.31.
- `assembly_valheim.dll` SHA-256: `148a498217238e7a3cb7ae06cbca087bce79b82338204dde63777d9ce6adcd1b`
- `BepInEx.dll` SHA-256: `18caffd2c09dc4e6a51fc13b4af7fd2389251f2ec8ecdf8bf076b84a14fb991e`
- `0Harmony.dll` SHA-256: `1a21cc03424fc82c3dd1346905d16494536b9595ae4162228d99fb7c285c1031`
- Optional Conditional Config Sync adapter source: upstream commit `3b0adea29f265f65a5a0bb09af7767ac0c6ffc27`, upstream SHA-256 `7ed0501042d24340fbaf357b24b58ad8457bd1bdae908edf36e084cbc6432a14`; adapted only to disambiguate Valheim's global `Version` type on `net472`

The release DLL was compiled directly against these installed assemblies. Game updates may change private methods or IL patterns; startup patch auditing is designed to fail closed and preserve vanilla behavior when a target no longer matches. The game's network version is a compile-time constant, so startup output labels it as the compilation target rather than claiming to read it dynamically from the running game.
