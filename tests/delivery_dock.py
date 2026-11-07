"""
A delivery to the RV loading dock that is open when a save loads (issue: the van vanishes and leaves only its seats).

    python tests/delivery_dock.py <slot> [guarded|unguarded]

Needs a Debug build in the slot and a loaded save. `unguarded` switches the fix off first (`rvdeliver guard off`)
and expects the original IndexOutOfRangeException; `guarded` (default) expects no exception and an activated van.

Steps: repair the RV, fit the loading dock, send a vanilla delivery to the RV, wait for the van to arrive,
save, reload the save, read the log of the load.
"""
import sys
import time

from check import Run

SAVE = 5


def main():
    slot = sys.argv[1] if len(sys.argv) > 1 else "mptest2"
    mode = sys.argv[2] if len(sys.argv) > 2 else "guarded"
    r = Run(slot)

    r.begin("setup")
    r.cmd("rvrepair", wait=1.5)
    lines = r.cmd("rvupg 8", wait=2.0)
    r.check("loading dock fitted", any("loading dock attached" in l for l in lines), lines[-1] if lines else "no log line")
    r.cmd("rvdeliver guard " + ("off" if mode == "unguarded" else "on"))

    r.begin("order and arrival")
    m = r.mark()
    lines = r.cmd("rvdeliver soil 1", wait=1.0)
    r.check("delivery sent to the RV", any("rvdeliver:" in l for l in lines), lines[-1] if lines else "no log line")
    pattern = r"dock guard is off" if mode == "unguarded" else r"activates at RV dock"
    arrived = r.wait_line(m, pattern, timeout=150)
    r.check("van arrives at the RV dock", arrived is not None, arrived or "no arrival line within 150 s")
    r.check("no exception on arrival", not r.lines(m, r"IndexOutOfRange"), "")

    r.begin("save and reload with the van parked")
    r.save()
    lm = r.load(SAVE)
    time.sleep(25)   # the dock is attached a few seconds after the load; give the re-activation time
    exc = r.lines(lm, r"IndexOutOfRange")
    act = r.lines(lm, r"activated deferred delivery|dock attached before delivery|activates at RV dock")
    dock = r.lines(lm, r"loading dock attached")
    deferred = r.lines(lm, r"activation deferred|activated deferred")
    r.note("deferred lines: %d, dock lines: %d" % (len(deferred), len(dock)))
    if mode == "unguarded":
        r.check("original failure reproduced (IndexOutOfRangeException on load)", bool(exc), "%d line(s)" % len(exc))
    else:
        r.check("no IndexOutOfRangeException on load", not exc, exc[0] if exc else "")
        r.check("van activated after the load", bool(act), "%d activation line(s)" % len(act))
        r.check("loading dock attached", bool(dock), "")
    r.shot("delivery_dock_" + mode)
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
