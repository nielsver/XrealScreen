---
name: git-branch-pr
description: XrealScreen git workflow (Feature Branch Workflow) - never commit to main; each feature gets its own branch from origin/main, commits are pushed to origin, and the user opens the pull request on the GitHub web page. Use whenever you commit, push, or the user asks for a branch, a PR/MR, or to "commit and push".
---

# Git: Feature Branch Workflow

The project uses the **Feature Branch Workflow**:
- `main` is always releasable and only changes by merging a pull request on GitHub (`git@github.com:nikryden/Screen-XREAL-1S.git`).
- Every feature, fix or doc change is developed on **its own branch** created from the latest `origin/main`.
- The branch is pushed to origin (after every commit), reviewed in a pull request, merged into `main`, then deleted.

**Never commit to `main` and never push to `origin/main`**, even when CLAUDE.md says "commit after each step" -
those commits go on the feature branch. Merge into `main` only in GitHub, or when the user explicitly asks for it in this conversation.

## 1. Start of a task: the right feature branch
```powershell
git status -sb                      # which branch, anything uncommitted?
git fetch --prune origin
```
- **One branch per feature.** Unrelated work never goes on an existing feature branch.
- On `main` (or detached): create the branch from the latest `origin/main`:
  `git switch -c <type>/<short-kebab-topic> origin/main`
  Uncommitted changes come along with `git switch -c` - that is fine.
- On the feature branch of **this** task (e.g. follow-up fixes the user asks for on the same feature): keep using it;
  new commits update the open pull request.
- On a feature branch of a **different** feature: commit and push its work first, then start a new branch from `origin/main`
  (not from the other feature branch). Mention the switch in your reply.
- Local `main` ahead of `origin/main` (commits made there by mistake): create the branch from local `main`
  (`git switch -c <branch>`), then `git branch -f main origin/main` so `main` matches origin again. Say so in your reply.

Branch name: `<type>/<topic>`, lower case, e.g. `feature/nav-pane-resize`, `fix/imu-reconnect`, `docs/sdk-readme`, `chore/ci-cache`.
Types: `feature`, `fix`, `docs`, `refactor`, `test`, `chore`.

## 2. Commit (on the feature branch)
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

## 3. Push after every commit
```powershell
git push -u origin HEAD             # first push sets upstream; later pushes: git push
```
- Pushed branches are shared: never rewrite them (no rebase, amend or `--force` after pushing);
  `--force-with-lease` only when the user explicitly asked for a history rewrite.

## 4. Keep the branch up to date with `main`
When `origin/main` moved on (`git log --oneline HEAD..origin/main` is not empty) and the PR would conflict, or before
handing over a PR:
```powershell
git fetch origin
git merge origin/main               # merge, don't rebase a pushed branch
```
Resolve conflicts, build and test, commit the merge, push.

## 5. Pull request (the user opens it on the GitHub web page)
Do not create the PR yourself (no `gh pr create`, no API calls). Give the user:
- the link `https://github.com/nikryden/Screen-XREAL-1S/pull/new/<branch>` (git also prints it after the first push),
- a ready-to-paste **title** and **description** (what changed, why, build/test result).

When a PR for the branch is already open, further pushes update it - just say the PR got new commits.

## 6. Reply
State the branch name, the commits pushed (`git log --oneline origin/main..HEAD`), build/test result, and the PR link.

## 7. After the merge
The user merges the PR on GitHub and deletes the branch there ("Delete branch" button). Then clean up locally:
```powershell
git switch main
git pull --ff-only
git fetch --prune origin
git branch -d <branch>              # -d refuses unmerged work; never -D without asking
```
If the user asks you to delete the remote branch: `git push origin --delete <branch>`.
