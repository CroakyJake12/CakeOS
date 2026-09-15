#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
target = root / "HUI" / "vendor" / "Haven.UI" / "Input" / "HavenInputRouter.cs"

if not target.is_file():
    raise SystemExit(f"Missing staged HUI input router: {target}")

source = target.read_text(encoding="utf-8")
old = """    public bool KeyUp(HavenKey key)
    {
        if (_focused is IHavenKeyboardInputTarget custom && custom.KeyUp(new HavenKeyInput(key, HavenKeyModifiers.None))) return true;
        if (key == HavenKey.Tab) return true;
"""
new = """    public bool KeyUp(HavenKey key)
    {
        if (_focused is IHavenKeyboardInputTarget custom)
        {
            var focusedBeforeKeyUp = _focused;
            var wasPressed = focusedBeforeKeyUp.State.HasFlag(HavenElementState.Pressed);
            if (custom.KeyUp(new HavenKeyInput(key, HavenKeyModifiers.None)))
            {
                // Canonical Button handles its own Enter/Space pressed state and Invoke(),
                // so the router's generic Activate() path is not reached. Preserve pointer
                // parity by executing the same declarative ClickActions after a successful
                // keyboard activation, without invoking the button a second time.
                if (focusedBeforeKeyUp is Button button
                    && key is HavenKey.Enter or HavenKey.Space
                    && wasPressed
                    && !button.State.HasFlag(HavenElementState.Disabled))
                    _actions.ExecuteClick(root, button);
                return true;
            }
        }
        if (key == HavenKey.Tab) return true;
"""

if old not in source:
    if new in source:
        print("CakeOS keyboard click-action parity patch already applied.")
        sys.exit(0)
    raise SystemExit("Pinned HUI input-router context changed; refusing to apply migration patch.")

target.write_text(source.replace(old, new, 1), encoding="utf-8")
print("Applied CakeOS HUI keyboard click-action parity patch.")