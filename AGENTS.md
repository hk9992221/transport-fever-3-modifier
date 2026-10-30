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

## PR 审查反馈

- 用户约定：本项目每一轮 PR 审查和复审的意见，都必须主动提交到对应的 GitHub PR，无需用户再次提醒上传。
- 完成审查后发布 GitHub Review 或 PR 讨论评论，写明所审查的提交、结论、具体问题及代码位置、可复现用例、验证结果和验证边界。无问题或确认修复时也要同步结论。
- 在聊天回复中提供已发布 Review 或评论的链接，不能只在聊天中给出审查结果。
- GitHub 因当前账号也是 PR 作者而禁止提交 Request changes / Approve 时，用 PR 讨论评论记录真实结论，并说明平台限制，不冒称已提交对应 Review 状态。
- 提交审查意见本身不代表获得合并、关闭 PR 或更改草稿状态的授权；这些动作遵循用户针对该轮工作的指示。
