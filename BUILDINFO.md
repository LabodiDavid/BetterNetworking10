# Build information

- Mod version: `1.3.0`
- Target game version reported by the supplied server assembly: `Valheim 1.0.12`
- Runtime patch audit also verified on `Valheim 1.0.15` from a complete user startup log (`15 applied, 4 skipped, 0 failed`).
- Target network version: `40`
- `assembly_valheim.dll` SHA-256: `1231fc2ffdbe6038ba622b8646c2084980d06962e5f8521ae5a0b886be0f1c61`
- `BepInEx.dll` SHA-256: `f09821b2a7b990c6f50c5ef23229635303ce675374b70edc6f5a9b960cb818e3`
- `0Harmony.dll` SHA-256: `1a21cc03424fc82c3dd1346905d16494536b9595ae4162228d99fb7c285c1031`
- Optional Conditional Config Sync adapter source: upstream commit `3b0adea29f265f65a5a0bb09af7767ac0c6ffc27`, upstream SHA-256 `7ed0501042d24340fbaf357b24b58ad8457bd1bdae908edf36e084cbc6432a14`; adapted only to disambiguate Valheim's global `Version` type on `net472`

The release DLL was compiled directly against these supplied assemblies. Game updates may change private methods or IL patterns; startup patch auditing is designed to fail closed and preserve vanilla behavior when a target no longer matches.
