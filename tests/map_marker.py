"""
In-game check of the RV's phone-map marker: the vehicle marker while someone drives, the property marker (owned icon)
when the RV is parked away from home, also after a save and load, and only the property marker at home again.

    python tests/map_marker.py <slot> [save slot]

Needs a Debug build of this worktree in the slot and a loaded save; it repairs the RV and gives it the engine upgrade
through the debug commands. Screenshots of the opened map go to tests/out/map-*.png.
"""
import math
import re
import sys
import time

from check import Run

AWAY = "10.05 -18.7 90"   # Marco's yard, far from the RV's own spot
STATE = re.compile(r"\[Map\] rvmap: chassis (\w+), driving (\w+) \| vehicle marker (.+?) \| property marker (.+)$")
POS = re.compile(r"at \((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)")


def state(r):
    """The markers as rvmap reports them: (vehicle, property), each the text after 'marker', or None if missing."""
    for line in reversed(r.cmd("rvmap", wait=0.6)):
        m = STATE.search(line)
        if m:
            r.note(line.split("[Map] ", 1)[-1])
            return m.group(3), m.group(4)
    r.note("no rvmap line")
    return None, None


def pos(marker):
    m = POS.search(marker or "")
    return (float(m.group(1)), float(m.group(3))) if m else None


def far(a, b, metres):
    return a is not None and b is not None and math.hypot(a[0] - b[0], a[1] - b[1]) > metres


def map_shot(r, name):
    r.cmd("rvmap open", wait=1.5)
    r.shot(name)
    lines = r.cmd("rvmap close", wait=1.0)
    r.check("the phone is put away again", any("phone put away" in l for l in lines), lines)


def main():
    slot = sys.argv[1] if len(sys.argv) > 1 else "mptest5"
    save_slot = int(sys.argv[2]) if len(sys.argv) > 2 else 5
    r = Run(slot)

    r.begin("setup")
    r.cmd("rvrepair", wait=1.5)
    r.cmd("rvupg 16", wait=1.0)
    vehicle, prop = state(r)
    home = pos(prop)
    r.check("at home the property marker is on", (prop or "").startswith("on 'RV"), prop)

    r.begin("driving")
    m = r.mark()
    r.cmd("rvdrive", wait=3)
    line = r.wait_line(m, r"\[Map\] RV marker: vehicle \(driving\)", timeout=10)
    r.check("the log switches to the vehicle marker", line is not None, r.lines(m, r"\[Map\]"))
    vehicle, prop = state(r)
    r.check("the vehicle marker is on", (vehicle or "").startswith("on '"), vehicle)
    r.check("the property marker is off", (prop or "").startswith("off"), prop)
    map_shot(r, "map-driving")

    r.begin("parked away from home")
    m = r.mark()
    # Right after the exit: an RV left on its own spot goes home after a short grace time.
    r.cmd("rvdrive exit", wait=0.5)
    r.cmd("rvdrive tp " + AWAY, wait=3)
    line = r.wait_line(m, r"\[Map\] RV marker: property \(parked\)", timeout=10)
    r.check("the log switches to the property marker", line is not None, r.lines(m, r"\[Map\]"))
    r.check("the RV did not go home", not r.lines(m, r"property at home"), r.lines(m, r"\[Map\]|\[Mobile\]"))
    vehicle, prop = state(r)
    r.check("the vehicle marker is off", (vehicle or "").startswith("off") or vehicle == "none", vehicle)
    r.check("the property marker is on with the owned icon", (prop or "").startswith("on 'RV") and "icon=owned" in prop, prop)
    r.check("the property marker moved with the RV", far(pos(prop), home, 20), "%r vs home %r" % (pos(prop), home))
    map_shot(r, "map-parked")

    r.begin("parked away, after a save and load")
    r.save()
    r.load(save_slot)
    vehicle, prop = state(r)
    r.check("the property marker is on with the owned icon", (prop or "").startswith("on 'RV") and "icon=owned" in prop, prop)
    r.check("the property marker stays where the RV stands", far(pos(prop), home, 20), "%r vs home %r" % (pos(prop), home))
    r.check("the vehicle marker is off", (vehicle or "").startswith("off") or vehicle == "none", vehicle)

    r.begin("back home")
    m = r.mark()
    r.cmd("rvdrive home", wait=3)
    line = r.wait_line(m, r"\[Map\] RV marker: property at home", timeout=10)
    r.check("the log reports the property marker at home", line is not None, r.lines(m, r"\[Map\]"))
    vehicle, prop = state(r)
    r.check("the property marker is on with the owned icon", (prop or "").startswith("on 'RV") and "icon=owned" in prop, prop)
    r.check("the property marker is back home", not far(pos(prop), home, 5), "%r vs home %r" % (pos(prop), home))
    errors = [l for l in r.lines((r.log_path(), 0), r"\[ERROR\] \[RVRepairVan\].*(Map|POI)")]
    r.check("no marker errors in the log", not errors, errors)

    sys.exit(r.summary())


if __name__ == "__main__":
    main()
