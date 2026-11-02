"""
The helper behind the in-game checks of RV Repair Van: drives a running test game over the ScheduleMCP bridge
(Workspace/tools/bridge/s1.py), reads the log, clicks real dialogue choices, and prints PASS, FAIL or SKIP per check.

A check reads the game's own log lines and the lines of `rvquest`; it needs a Debug build in the slot.
"""
import glob
import os
import re
import shutil
import sys
import time


def _workspace_bridge():
    here = os.path.dirname(os.path.abspath(__file__))
    folder = here
    for _ in range(6):
        candidate = os.path.join(folder, "Workspace", "tools", "bridge")
        if os.path.isfile(os.path.join(candidate, "s1.py")):
            return candidate
        folder = os.path.dirname(folder)
    raise SystemExit("[check] Workspace/tools/bridge/s1.py not found above " + here)


sys.path.insert(0, _workspace_bridge())
from s1 import Game, BridgeDown  # noqa: E402

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")


class Run:
    def __init__(self, slot):
        for stream in (sys.stdout, sys.stderr):
            stream.reconfigure(encoding="utf-8", errors="replace")
        self.g = Game(slot)
        self.slot = slot
        self.results = []
        self.stage = ""

    # ---- results -------------------------------------------------------------------------------

    def begin(self, stage):
        self.stage = stage
        print("\n##### " + stage, flush=True)

    def check(self, name, ok, detail=""):
        verdict = "PASS" if ok else "FAIL"
        self.results.append((self.stage, name, verdict, detail))
        print("%s  %s%s" % (verdict, name, ("  - " + str(detail)) if detail else ""), flush=True)
        return ok

    def note(self, text):
        print("      " + text, flush=True)

    def summary(self):
        counts = {"PASS": 0, "FAIL": 0}
        for _, _, verdict, _ in self.results:
            counts[verdict] += 1
        print("\n===== %d PASS, %d FAIL" % (counts["PASS"], counts["FAIL"]))
        for stage, name, verdict, detail in self.results:
            if verdict != "PASS":
                print("%s  [%s] %s  - %s" % (verdict, stage, name, detail))
        return 1 if counts["FAIL"] else 0

    # ---- the log -------------------------------------------------------------------------------

    def log_path(self):
        """The newest log of this slot. A co-op client that relaunched for Side Hustle's mod sync logs in its profile."""
        found = glob.glob(os.path.join(self.g.root, "MelonLoader", "Logs", "*.log"))
        found += glob.glob(os.path.join(self.g.root, "SideHustle_Profiles", "session", "*", "MelonLoader", "Logs", "*.log"))
        return max(found, key=os.path.getmtime) if found else None

    def mark(self):
        path = self.log_path()
        return (path, os.path.getsize(path) if path else 0)

    def lines(self, mark, grep=None):
        path, offset = mark
        now = self.log_path()
        if now != path:
            offset = 0
        if not now:
            return []
        with open(now, "r", encoding="utf-8", errors="replace") as handle:
            handle.seek(offset)
            found = handle.read().splitlines()
        if grep:
            rx = re.compile(grep)
            found = [l for l in found if rx.search(l)]
        return found

    def wait_line(self, mark, pattern, timeout=15.0):
        rx = re.compile(pattern)
        end = time.time() + timeout
        while True:
            for line in self.lines(mark):
                if rx.search(line):
                    return line
            if time.time() >= end:
                return None
            time.sleep(0.4)

    # ---- driving the game ----------------------------------------------------------------------

    def cmd(self, command, wait=0.8):
        m = self.mark()
        try:
            self.g.run(command, settle_ms=int(wait * 1000), max_wait_ms=int(wait * 1000) + 8000)
        except TimeoutError as e:
            self.note("console '%s': %s" % (command, e))
            self._wait_game()
            self.g.run(command, settle_ms=int(wait * 1000), max_wait_ms=int(wait * 1000) + 8000)
        return self.lines(m)

    def _wait_game(self):
        end = time.time() + 45
        while time.time() < end:
            try:
                self.g.call("get_state", {}, timeout_ms=4000)
                return
            except (TimeoutError, BridgeDown):
                time.sleep(1)
        self.note("the game does not answer after 45 s")

    def tp(self, x, y, z, yaw=None):
        args = {"x": x, "y": y, "z": z}
        if yaw is not None:
            args["yaw"] = yaw
        self.g.world("teleport", args, timeout_ms=8000)
        time.sleep(1.2)

    def save(self):
        m = self.mark()
        self.g.call("save_now", {}, timeout_ms=20000)
        time.sleep(3)
        return m

    def load(self, save_slot, timeout=180, settle=8):
        """Back to the menu, then load the save. Returns the mark taken before, so the load's lines can be read."""
        m = self.mark()
        self.g.call("return_to_menu", {}, timeout_ms=10000)
        end = time.time() + 60
        while time.time() < end:
            time.sleep(2)
            try:
                state = self.g.call("get_current_save", {}, timeout_ms=5000).get("result") or {}
                if not state.get("inGame"):
                    break
            except (TimeoutError, BridgeDown):
                continue
        time.sleep(3)
        self.g.call("load_save", {"slot": save_slot}, timeout_ms=15000)
        try:
            self.g.call("wait_until_loaded", {"timeout_ms": timeout * 1000}, timeout_ms=timeout * 1000 + 5000)
        except TimeoutError as e:
            self.note("wait_until_loaded: %s" % e)
        end = time.time() + 60
        while time.time() < end:
            time.sleep(2)
            try:
                state = self.g.call("get_state", {}, timeout_ms=4000).get("result") or {}
            except (TimeoutError, BridgeDown):
                continue
            if state.get("ready") and not state.get("loading"):
                break
        time.sleep(settle)
        return m

    def shot(self, name):
        """A screenshot copied to tests/out/<name>.png. A person looks at it; no check reads pixels."""
        path = self.g.shot()
        if not path or not os.path.isfile(path):
            self.note("screenshot failed: %r" % path)
            return None
        os.makedirs(OUT, exist_ok=True)
        target = os.path.join(OUT, name + ".png")
        shutil.copyfile(path, target)
        self.note("screenshot: " + target)
        return target

    # ---- dialogue ------------------------------------------------------------------------------

    def dialog(self):
        """The dialogue UI's elements as the game shows them."""
        # By name: the list is capped, and an open character creator's swatches would fill an empty query.
        els = (self.g.call("ui_find", {"query": "Dialogue"}, timeout_ms=8000).get("result") or {}).get("elements") or []
        return [e for e in els if "Dialogue" in (e.get("path") or "")]

    def choices(self):
        return [e.get("label") or "" for e in self.dialog() if "DialogueChoice" in (e.get("path") or "")]

    def wait_choices(self, timeout=15.0):
        """The choices of the current line. The game shows them once the line has finished rolling out."""
        end = time.time() + timeout
        while True:
            found = self.choices()
            if found or time.time() >= end:
                return found
            time.sleep(0.4)

    def click(self, part, timeout=15.0):
        """Click the choice whose text contains `part`, once it shows; True when there was one."""
        end = time.time() + timeout
        while True:
            for e in self.dialog():
                if part in (e.get("label") or ""):
                    # By label: the path of the first choice is a substring of every other choice's path.
                    self.g.call("ui_click", {"query": e["label"]}, timeout_ms=8000)
                    time.sleep(0.8)
                    return True
            if time.time() >= end:
                self.note("choice not visible: %r in %r" % (part, self.choices()))
                return False
            time.sleep(0.4)

    def talk(self, npc):
        """Open the NPC's conversation the way the talk key does. The bridge's interact can reach pickpocket instead."""
        self.cmd("rvpick %s open" % npc, wait=1.0)
        time.sleep(0.6)

    def continue_to_choices(self, timeout=20.0):
        """Continue through lines without choices until choices show. Empty when the conversation ended without any."""
        end = time.time() + timeout
        while time.time() < end:
            found = self.choices()
            if found:
                return found
            if self.dialog():
                self.g.call("dialogue_continue", {}, timeout_ms=5000)
            time.sleep(0.8)
        return []

    def close_dialog(self):
        """Continue until the conversation is closed. Returns how many lines it took."""
        for i in range(6):
            if not self.dialog():
                return i
            self.g.call("dialogue_continue", {}, timeout_ms=5000)
            time.sleep(0.6)
        return 6

    # ---- rvquest -------------------------------------------------------------------------------

    def quests(self):
        """Both journal quests. q1 and q2 are lists of (state, title), None when the quest is not in the journal."""
        lines = self.cmd("rvquest", wait=0.6)
        out = {"q1": None, "q2": None}
        current = None
        order = []
        for line in lines:
            if "[rvquest]" not in line:
                continue
            text = line.split("[rvquest]", 1)[1].strip()
            if re.match(r"stage=", text):
                for key, value in re.findall(r"(\w+)=(\S+)", text):
                    out[key] = value
                continue
            if text.startswith("at "):
                for key, value in re.findall(r"(\w+)=(-?\d[-\d.,]*)", text):
                    out["at_" + key] = tuple(float(v) for v in value.split(","))
                continue
            title = re.match(r"'(.*)': (\d+) step|'(.*)': not in the journal", text)
            if title:
                order.append(title.group(2) is not None)
                current = [] if title.group(2) is not None else None
                out["q%d" % len(order)] = current
                continue
            step = re.match(r"\[(\d+)\] (\w+) '(.*)'", text)
            if step and current is not None:
                current.append((step.group(2), step.group(3)))
        return out


def states(quest):
    """A quest's steps as one string, A active, C completed, - inactive."""
    if quest is None:
        return "<none>"
    return "".join({"Active": "A", "Completed": "C", "Inactive": "-"}.get(s, "?") for s, _ in quest)
