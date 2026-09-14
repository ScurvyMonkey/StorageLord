---
name: run
description: Launch and verify Storage Lord in the Unity Editor via the unity-mcp bridge — enters Play Mode, captures the platform view, and reads the Editor console for errors/warnings. Use this whenever asked to run, test, or verify a change "in the game", "in Unity", or "in Play Mode", or to screenshot the current platform. Always use this instead of asking the user to manually press Play and check for you.
---

# Run — Storage Lord (Unity MCP)

Storage Lord is a Unity 6 project verified through the `unity-mcp` bridge (`Unity_*` tools), not a build/CLI process. There is no separate "start the game" shell command — the Unity Editor for this project must already be open, and this skill drives it through Play Mode.

## Steps

1. **Confirm the bridge is live.** Call `Unity_ManageEditor` with `Action: GetState`. If this errors or times out, the Unity Editor for Storage Lord isn't running or isn't focused — tell the user to open/focus the project in Unity, then retry. Don't guess at scene state without this.

2. **Enter Play Mode.** Call `Unity_ManageEditor` with `Action: Play`, `WaitForCompletion: true`.

3. **Capture what's happening.** Storage Lord is a **3D** project — the exact camera style (top-down orthographic vs. angled isometric vs. free-orbit) is a GDD Open Question not yet resolved, so check what's actually wired up before assuming. Prefer `Unity_Camera_Capture` (captures via the active gameplay camera) as the default. Only use `Unity_SceneView_Capture2DScene` if the resolved camera turns out to be a strict top-down orthographic view — it won't produce a useful capture for an isometric or free-orbit camera. Frame the capture on the platform/grid area, not an arbitrary point — check `GridManager`/`PlatformController`-equivalent for the platform's actual world extents rather than guessing.

4. **Check the console.** Call `Unity_ReadConsole` with `Action: Get`, `Types: ["Error", "Warning"]`. Report any errors verbatim with file/line if present — don't paraphrase away stack traces the user might need.

5. **Report back**: what you did, the captured image, and a summary of console errors/warnings (or confirmation there were none). If the user asked you to verify a specific behavior — especially a placement interaction (snap, validity, anchor) — say explicitly whether you observed it or couldn't tell from a screenshot alone.

6. **Leave Play Mode running** unless the user asked for a one-shot check or explicitly wants it stopped — they may want to keep interacting. To stop, call `Unity_ManageEditor` with `Action: Stop`.

## Notes

- Prefer `Unity_ReadConsole` over `Unity_GetConsoleLogs` for error checks — it supports filtering by type and time, so you can scope to what happened during this run rather than the whole session's history.
- If you changed a script right before running, remember Unity recompiles on focus/external change — a stale error in the console may predate your fix. Check timestamps if unsure.
- This skill assumes one Unity Editor instance has Storage Lord open. If multiple Unity projects are open at once, `Unity_ManageEditor GetState` and friends operate on whichever editor the `unity-mcp` relay is currently bridged to — if project data looks wrong (e.g. wrong scene names), the wrong project may have focus.
- The `/ux` skill has its own, more structured visual review process built on top of this same capture mechanism — use `/ux #<issue>` instead of this skill when the task is a formal pre-ship design review, not just an ad-hoc check.
