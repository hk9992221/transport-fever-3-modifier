# Project Notes

## Git remote

- Use the SSH key `C:\Users\chk\.ssh\gugegithub` for this repository.
- `origin` is `git@github.com:hk9992221/transport-fever-3-modifier.git`.
- Local configuration uses `core.sshCommand = ssh -i C:/Users/chk/.ssh/gugegithub -o IdentitiesOnly=yes`.
- The key currently authenticates as GitHub account `roobooo`; that account must be granted access to this repository. If SSH returns `Repository not found`, use the repo's HTTPS credential for the push.

## Verification

- C# compile: `powershell -ExecutionPolicy Bypass -File .\src\build.ps1 -Destination .\dist-local-fixed\build`
- Offline layout/logic check: run the built EXE with `--layout-check <report>` and `TF3_MODIFIER_BUILD_CHECK=1`.
- Lua adapter check (Lua 5.4 or fengari): `lua scripts/industry-lua-check.lua`
- Native syntax check (optional): MinGW gcc with `-std=gnu11` on `src/Runtime.c`.
