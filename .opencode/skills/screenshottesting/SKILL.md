---
name: screenshottesting
version: "1.0"
tags: [testing, visual, graphics, screenshot, headless]
description: |
  Skill for running graphical programs headlessly inside a dev container,
  sending inputs via xdotool, and capturing screenshots to visually test
  program output. The program has a built-in F12 screenshot key.

  Activate this skill when the user asks:
  - "Test if X looks correct / works visually"
  - "Check what the screen looks like when I do X"
  - "Run the game and take a screenshot"
  - "Verify the UI / layout / rendering of X"
  - "Does X appear on screen after doing Y?"
  - Any request to observe program behavior that requires seeing the output

  Do NOT activate for pure logic tests, compilation checks, or anything
  that does not require visual output.

  Think step by step before writing a test script: identify what inputs
  are needed, what state the program should be in before taking the
  screenshot, and what to look for in the result.
language: bash
---

# Skill: Visual Screenshot Testing in sbx

---

## Environment

- **Host**: Bazzite (Wayland)
- **Distrobox**: Ubuntu container with display access from host
- **sbx**: Container inside distrobox used by opencode. Has `xvfb` and `xdotool` installed.
- **Display**: Must use a virtual framebuffer (Xvfb) — no real display is available inside sbx.
- **OpenGL**: Must use Mesa software rendering — no GPU inside sbx.

---

## How to Run the Program Headlessly

Always set these two environment variables before launching:

```bash
export DISPLAY=:99
export LIBGL_ALWAYS_SOFTWARE=1
```

Start Xvfb before launching the program (resolution must match target window size):

```bash
Xvfb :99 -screen 0 <window-resolution>x24 &
XVFB_PID=$!
sleep 1
```

Check `AGENTS.md` in the project root for correct launch instructions (build task, run command, arguments). Launch the program in the background:

```bash
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 <launch-command-from-AGENTS.md> &
APP_PID=$!
```

**Java proxy noise**: Inside sbx, `JAVA_TOOL_OPTIONS` may contain Docker proxy settings that print "Picked up" warnings to stderr. These are harmless but noisy — ignore them unless they cause actual failures.

Wait for the program to finish starting before sending input. If the program prints a ready marker (documented in AGENTS.md), poll for it instead of using a fixed sleep:

```bash
# Capture stdout to a file and wait for ready signal
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 <launch-command> 2>&1 | tee /tmp/app.log &
APP_PID=$!

# Wait until the program prints its ready marker (max 30 seconds)
TIMEOUT=30
ELAPSED=0
while ! grep -q "READY" /tmp/app.log 2>/dev/null && [ $ELAPSED -lt $TIMEOUT ]; do
    sleep 0.5
    ELAPSED=$((ELAPSED + 1))
done

if [ $ELAPSED -ge $TIMEOUT ]; then
    echo "ERROR: Program did not become ready within ${TIMEOUT}s" >&2
    kill $APP_PID 2>/dev/null
    exit 1
fi
echo "Program is ready after ${ELAPSED}s"
```

If no ready marker exists, use a fixed `sleep` based on how long the program typically takes to start.

---

## How to Send Inputs

Use `xdotool` with `DISPLAY=:99`. Common commands:

```bash
# Press a key
DISPLAY=:99 xdotool key space
DISPLAY=:99 xdotool key Return
DISPLAY=:99 xdotool key Escape

# Type text
DISPLAY=:99 xdotool type "hello"

# Move mouse and click (x y coordinates — adjust to target window size)
DISPLAY=:99 xdotool mousemove <half-width> <half-height>
DISPLAY=:99 xdotool click 1   # left click
DISPLAY=:99 xdotool click 3   # right click

# Combined: move and click
DISPLAY=:99 xdotool mousemove <half-width> <half-height> click 1

# Press and hold, then release
DISPLAY=:99 xdotool keydown w
sleep 1
DISPLAY=:99 xdotool keyup w
```

Add short `sleep` calls between inputs to give the game time to react:

```bash
DISPLAY=:99 xdotool key space
sleep 0.5
DISPLAY=:99 xdotool mousemove 400 300 click 1
sleep 0.5
```

---

## How to Take a Screenshot

The game has a built-in screenshot key: **F12**. Press it via xdotool:

```bash
DISPLAY=:99 xdotool key F12
sleep 0.5  # wait for the file to be written
```

Screenshots are saved automatically to a `screenshots/` directory. The exact location varies by project — check `AGENTS.md` or search for where screenshots are written.

---

## Where to Find Screenshots

Screenshots may be saved in various locations depending on the project. Use these strategies to locate them:

1. **Check AGENTS.md** first — it often documents output directories
2. **Search recent files** modified in the last minute:

```bash
find <project-root> -name "*.png" -mmin -1 2>/dev/null | sort
```

3. **Look for common screenshot directory names**:

```bash
find <project-root> -type d \( -iname "screenshots" -o -iname "captures" -o -iname "output" \) 2>/dev/null
```

4. **Check the program's working directory** — screenshots are often saved relative to where the program runs:

```bash
ls -lt <project-root>/*/screenshots/ 2>/dev/null || ls -lt ./screenshots/ 2>/dev/null
```

The most recent file is the one just captured. Read it directly — opencode and lm-studio are configured to display images.

---

## How to Clean Up

Always kill the app and Xvfb when done:

```bash
kill $APP_PID 2>/dev/null
kill $XVFB_PID 2>/dev/null
wait $APP_PID 2>/dev/null || true
wait $XVFB_PID 2>/dev/null || true
```

**Note**: `wait` may timeout in this environment — use `|| true` to avoid errors. If processes linger, find and kill them explicitly:

```bash
pgrep -af <program-name> | grep -v pgrep | xargs kill 2>/dev/null || true
```

---

## Full Test Script Template

```bash
#!/bin/bash
set -e

PROJECT_ROOT="$(pwd)"
DISPLAY_NUM=:99

# Start virtual display (resolution must match target window size)
Xvfb $DISPLAY_NUM -screen 0 <window-resolution>x24 &
XVFB_PID=$!
sleep 1

# Launch program and capture stdout (replace with actual launch command from AGENTS.md)
DISPLAY=$DISPLAY_NUM LIBGL_ALWAYS_SOFTWARE=1 <launch-command> 2>&1 | tee /tmp/app.log &
APP_PID=$!

# Wait for ready signal or use fixed sleep
TIMEOUT=30
ELAPSED=0
while ! grep -q "READY" /tmp/app.log 2>/dev/null && [ $ELAPSED -lt $TIMEOUT ]; do
    sleep 0.5
    ELAPSED=$((ELAPSED + 1))
done

if [ $ELAPSED -ge $TIMEOUT ]; then
    echo "ERROR: Program did not become ready within ${TIMEOUT}s" >&2
    kill $APP_PID 2>/dev/null
    kill $XVFB_PID 2>/dev/null
    exit 1
fi
echo "Program is ready after ${ELAPSED}s"

# --- Inputs go here (adjust coordinates to target window size) ---
DISPLAY=$DISPLAY_NUM xdotool key space
sleep 0.5
DISPLAY=$DISPLAY_NUM xdotool mousemove <half-width> <half-height> click 1
sleep 0.5
# -----------------------------------------------------------------

# Take screenshot via F12 (if supported)
DISPLAY=$DISPLAY_NUM xdotool key F12
sleep 0.5

# Kill app and display
kill $APP_PID 2>/dev/null
kill $XVFB_PID 2>/dev/null
wait $APP_PID 2>/dev/null || true
wait $XVFB_PID 2>/dev/null || true

# Locate screenshot
echo "Screenshot saved to:"
find "$PROJECT_ROOT" -name "*.png" -mmin -1 2>/dev/null | sort | tail -1 | xargs echo
```

---

## After the Screenshot Is Taken

1. Locate the latest screenshot using the strategies in "Where to Find Screenshots" above
2. Display or pass the image to the model
3. Describe what is visible and whether it matches the expected behavior
4. Report pass or fail based on visual inspection

No image diffing is used. Assessment is done by looking at the screenshot directly.

## Clean up

When running separate clean up tasks after other steps, check first if processes are still 
running before attempting to clean them.