"""
In-game check of pouring at a chemistry station in the RV (issue #114): the beaker must pour at home and with the RV
parked away on a slope, leaning either way. Without the fix ('rvpour vanilla') one lean side stops the beaker short of
the pour angle; that run is the reproduction and is reported, not judged.

    python tests/pour_tilt.py <slot> [lean degrees] [liquid level]

Needs a Debug build of this worktree in the slot and a loaded save. It repairs the RV, gives it the engine, places a
chemistry station on the RV floor if none is there, and drives the RV off home through the debug commands.
"""
import re
import sys

from check import Run

AWAY = "10.05 -18.7 90"   # Marco's yard, far from the RV's own spot
RESULT = re.compile(r"\[Pour\] tip result: (POURED|NO POUR) .*Floor tilt ([\d.]+) deg, (-?[\d.]+) deg of it .*Fix (on|off), chassis (\w+)")


def tip(r, level, label):
    m = r.mark()
    r.cmd("rvpour tip %s 3" % level, wait=0.5)
    line = r.wait_line(m, r"\[Pour\] tip result", timeout=20)
    if line is None:
        r.check(label + ": the tip finished", False, r.lines(m, r"\[Pour\]"))
        return None
    r.note(line.split("[Pour] ", 1)[-1])
    g = RESULT.search(line)
    return {"poured": g.group(1) == "POURED", "tilt": float(g.group(2)), "lean": float(g.group(3)),
            "fix": g.group(4) == "on", "chassis": g.group(5)} if g else None


def vanilla(r, on):
    lines = r.cmd("rvpour vanilla", wait=0.5)
    want = "fix off" if on else "fix on"
    if not any(want in l for l in lines):
        lines = r.cmd("rvpour vanilla", wait=0.5)
    r.check("pour fix switched " + ("off" if on else "on"), any(want in l for l in lines), lines)


def side(r, level, name, repro):
    """One lean side: the unmodded pour (reported), then the fixed pour (judged)."""
    vanilla(r, True)
    before = tip(r, level, name + ", fix off")
    if before is not None:
        repro.append((name, before))
    vanilla(r, False)
    after = tip(r, level, name + ", fix on")
    r.check(name + ": the beaker pours with the fix", after is not None and after["poured"], after)
    return after


def main():
    slot = sys.argv[1] if len(sys.argv) > 1 else "mptest5"
    lean = float(sys.argv[2]) if len(sys.argv) > 2 else 6.0
    level = sys.argv[3] if len(sys.argv) > 3 else "0.9"
    r = Run(slot)
    repro = []

    r.begin("setup")
    r.cmd("rvdrive home", wait=3)
    r.cmd("rvrepair", wait=1.5)
    r.cmd("rvupg 18", wait=1.5)   # workshop floor (the build grid) and engine
    m = r.mark()
    r.cmd("rvpour tip %s 0.2" % level, wait=0.5)
    if r.lines(m, r"no chemistry station in the RV"):
        # The floor grids are two tiles wide, so the 4x2 station only fits turned by 90 degrees.
        lines = r.cmd("rvplace chemistrystation 90", wait=3)
        r.check("a chemistry station is placed on the RV floor", any("rvplace: 'chemistrystation'" in l for l in lines), lines)
        m = r.mark()
        r.cmd("rvpour tip %s 0.2" % level, wait=0.5)
    trial = r.wait_line(m, r"\[Pour\] tip result", timeout=10)
    r.check("a trial tip runs on the RV's chemistry station", trial is not None, r.lines(m, r"\[Pour\]"))
    if trial is None:
        r.note("setup failed - no chemistry station to tip, the rest of the run would measure nothing")
        sys.exit(r.summary())

    r.begin("at home")
    home = side(r, level, "at home", repro)
    r.check("at home the floor is level", home is not None and home["chassis"] == "home", home)

    r.begin("parked away")
    m = r.mark()
    r.cmd("rvdrive", wait=3)
    r.cmd("rvdrive exit", wait=0.5)
    r.cmd("rvdrive tp " + AWAY, wait=4)
    bad = r.lines(m, r"\[(Mobile|Drive)\] .*(not ready|no chassis out)")
    r.check("the RV is out on its chassis", not bad, bad)
    if bad:
        r.note("setup failed - the RV did not leave home, the away stages would measure the home floor")
        sys.exit(r.summary())
    away = side(r, level, "parked away as it stands", repro)
    r.check("parked away the chassis is out", away is not None and away["chassis"] == "out", away)

    r.begin("pitched one way")
    # The station stands turned by 90 degrees, so its beaker tips about the RV's width: pitching the RV puts the
    # slope into the beaker's tip plane, rolling it would not.
    for l in r.cmd("rvtilt pitch %s" % lean, wait=1.0):
        r.note(l.split("] ", 2)[-1])
    a = side(r, level, "pitch +%s" % lean, repro)

    r.begin("pitched the other way")
    for l in r.cmd("rvtilt pitch %s" % (-2 * lean), wait=1.0):
        r.note(l.split("] ", 2)[-1])
    b = side(r, level, "pitch -%s" % lean, repro)
    r.check("the two sides lean the beaker opposite ways, each by most of the slope",
            a is not None and b is not None and a["lean"] * b["lean"] < 0
            and min(abs(a["lean"]), abs(b["lean"])) >= 0.8 * lean, (a, b))

    r.begin("reproduction without the fix")
    for name, res in repro:
        r.note("%-28s %s (floor tilt %.1f, %.1f in the tip plane)" % (name, "POURED" if res["poured"] else "NO POUR", res["tilt"], res["lean"]))
    if not any(not res["poured"] for _, res in repro):
        r.note("without the fix every side poured - the issue did not reproduce in this run")
    r.check("without the fix one leaning side stops short of pouring (the reported bug)",
            any(not res["poured"] and abs(res["lean"]) >= 0.8 * lean for _, res in repro), repro)

    log = r.lines((r.log_path(), 0), r"\[Pour\]")
    r.check("the log names the pour measured as at home", any("pours as at home on the tilted RV floor" in l for l in log), log[-5:])
    errors = r.lines((r.log_path(), 0), r"\[ERROR\] \[RVRepairVan\].*Pour")
    r.check("no pour errors in the log", not errors, errors)

    r.cmd("rvdrive home", wait=3)
    sys.exit(r.summary())


if __name__ == "__main__":
    main()
