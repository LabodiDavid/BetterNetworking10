# Build information

- Mod version: `1.0.1`
- Target game version reported by the supplied server assembly: `Valheim 1.0.12`
- Target network version: `40`
- `assembly_valheim.dll` SHA-256: `1231fc2ffdbe6038ba622b8646c2084980d06962e5f8521ae5a0b886be0f1c61`
- `BepInEx.dll` SHA-256: `f09821b2a7b990c6f50c5ef23229635303ce675374b70edc6f5a9b960cb818e3`
- `0Harmony.dll` SHA-256: `1a21cc03424fc82c3dd1346905d16494536b9595ae4162228d99fb7c285c1031`

The release DLL was compiled directly against these supplied assemblies. Game updates may change private methods or IL patterns; startup patch auditing is designed to fail closed and preserve vanilla behavior when a target no longer matches.
