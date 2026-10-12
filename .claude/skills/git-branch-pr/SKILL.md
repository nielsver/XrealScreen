---
name: git-branch-pr
description: XrealScreen git workflow - never commit to main; put work on a feature branch, commit there, push it to origin and give the user the GitHub pull request link. Use whenever you commit, push, or the user asks for a branch, a PR/MR, or to "commit and push".
---

# Git: branch, commit, push, pull request

`main` only changes through a pull request on GitHub (`git@github.com:nikryden/Screen-XREAL-1S.git`).
**Never commit to `main` and never push to `origin/main`**, even when CLAUDE.md says "commit after each step" -
those commits go on the feature branch. Merging into `main` happens in GitHub, or only when the user explicitly asks for it in this conversation.

## 1. Start of a task: be on a feature branch
```powershell
git status -sb                      # which branch, anything uncommitted?
git fetch origin
```
- On `main` (or detached): create a branch from the latest `origin/main`:
  `git switch -c <type>/<short-kebab-topic> origin/main`
  Uncommitted changes come along with `git switch -c` - that is fine.
- Already on a feature branch for this task: keep using it.
- Local `main` ahead of `origin/main` (commits made there by mistake): create the branch from local `main` instead
  (`git switch -c <branch>`), then `git branch -f main origin/main` so `main` matches origin again. Say so in your reply.

Branch name: `<type>/<topic>`, lower case, e.g. `feature/nav-pane-resize`, `fix/imu-reconnect`, `docs/sdk-readme`, `chore/ci-cache`.
Types: `feature`, `fix`, `docs`, `refactor`, `test`, `chore`.

## 2. Commit (on the branch)
- One commit per completed step, descriptive subject (`App: resizable navigation pane`), body says why.
- End the message with the Co-Authored-By line from the current attribution instructions.
- Stage specific paths or `git add -A` after checking `git status` - no secrets, captures, `bin/`, `obj/`, `artifacts/`.
- Before committing code: `dotnet build XrealScreen.slnx -p:Platform=x64` and `dotnet test --solution XrealScreen.slnx` pass
  (do not build the App project while XrealScreen runs a workspace).
- Hooks failing: fix the cause; never `--no-verify`.

Guard - run right before `git commit` and `git push`:
```powershell
if ((git branch --show-current) -eq 'main') { throw 'On main: create a feature branch first (skill git-branch-pr).' }
```

## 3. Push
```powershell
git push -u origin HEAD             # first push sets upstream; later pushes: git push
```
Never `git push --force` to a shared branch; on your own feature branch use `--force-with-lease` only when the user asked for a rewrite.

## 4. Pull request
- With GitHub CLI installed (`gh --version`):
  `gh pr create --base main --head <branch> --title "<title>" --body "<summary, test results>"`
  End the body with the PR attribution line from the current attribution instructions.
- Without `gh` (current setup): give the user the link
  `https://github.com/nikryden/Screen-XREAL-1S/pull/new/<branch>`
  (git also prints it after the first push) plus a ready-to-paste title and description.

## 5. Reply
State the branch name, the commits pushed (`git log --oneline origin/main..HEAD`), build/test result, and the PR link.
After the user merged the PR: `git switch main; git pull --ff-only; git branch -d <branch>`; delete the remote branch only if the user asks
(`git push origin --delete <branch>`).
