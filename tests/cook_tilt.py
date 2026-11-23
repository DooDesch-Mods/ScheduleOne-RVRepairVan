"""
In-game check of a real cook at the RV's chemistry station (issue #114): the pourable of the first step must tip far
enough to pour at home, with the RV parked away on flat ground, turned, and on a slope.

    python tests/cook_tilt.py <slot> [seconds] [lean degrees] [cooks per pose]

It runs the game's own cook task: 'rvcook start' fills the station and presses Begin, 'rvcook tilt' grabs the bottle
with a played-in click and holds the tilt key, so the drag, the torque, the constraint and every collision are the
game's. Needs a Debug build of this worktree in the slot and a loaded save. It repairs the RV, gives it the engine and
places a chemistry station on the RV floor if none is there.

Away from home the task used to lose the spawn pose of its ingredients in about every second cook, so each away pose
cooks several times (default 3). Each cook also checks that every ingredient rests at its spawn point after 0.5 s.
"""
import re
import sys

from check import Run

AWAY = "10.05 -18.7"     # Marco's yard, far from the RV's own spot
SPAWN_POINT = re.compile(r"\[Cook\] start: spawn point \d+ '.*' at \(([-\d.]+), ([-\d.]+), ([-\d.]+)\)")
SETTLED = re.compile(r"\[Cook\] start: item '(.*)' after 0.5 s at \(([-\d.]+), ([-\d.]+), ([-\d.]+)\)")
# An ingredient's centre of mass sits above its spawn point; sideways it stays within a few cm when it lands where the
# task put it. A lost spawn pose stacks all three near the container's origin, 30 cm and more away.
SPAWN_TOLERANCE = 0.12
RESULT = re.compile(r"\[Cook\] tilt result: (POURED|NO POUR) .*Tilt up to ([\d.]+) deg.*spout up to ([\d.]+) deg.*pour angle ([\d.]+)\. (\d+) collider\(s\) touched\..*chassis (\w+)")


def cook(r, label, seconds, results):
    m = r.mark()
    r.cmd("rvcook start", wait=0.5)
    started = r.wait_line(m, r"\[Cook\] start: '.*' begun", timeout=10)
    r.check(label + ": the cook task starts", started is not None, r.lines(m, r"\[Cook\]"))
    if started is None:
        r.cmd("rvcook stop", wait=1.0)
        return None
    r.wait_line(m, r"\[Cook\] start: item .* after 0.5 s", timeout=10)
    points = [(float(g.group(1)), float(g.group(3))) for g in (SPAWN_POINT.search(l) for l in r.lines(m, r"spawn point")) if g]
    seen, far = [], []
    for l in r.lines(m, r"after 0.5 s at"):
        g = SETTLED.search(l)
        if not g or not points:
            continue
        x, z = float(g.group(2)), float(g.group(4))
        d = min(((x - px) ** 2 + (z - pz) ** 2) ** 0.5 for px, pz in points)
        seen.append("%s %.2f m" % (g.group(1).replace("_StationItem(Clone)", ""), d))
        if d > SPAWN_TOLERANCE:
            far.append(seen[-1])
    r.check(label + ": the ingredients rest at their spawn points", bool(points) and bool(seen) and not far,
            ("off: %s" % far) if far else ("from the nearest spawn point: %s" % seen if seen else "no spawn points or items logged"))
    r.cmd("rvcook tilt 1 %s" % seconds, wait=0.5)
    line = r.wait_line(m, r"\[Cook\] tilt result", timeout=seconds + 20)
    for l in r.lines(m, r"\[Cook\] (trace|touched|tilt:)"):
        r.note(l.split("[Cook] ", 1)[-1])
    r.cmd("rvcook stop", wait=1.5)
    if line is None:
        r.check(label + ": the tilt finished", False, r.lines(m, r"\[Cook\]|\[ERROR\]"))
        return None
    r.note(line.split("[Cook] ", 1)[-1])
    g = RESULT.search(line)
    res = {"poured": g.group(1) == "POURED", "tilt": float(g.group(2)), "spout": float(g.group(3)),
           "need": float(g.group(4)), "touched": int(g.group(5)), "chassis": g.group(6)} if g else None
    results.append((label, res))
    r.check(label + ": the bottle pours", res is not None and res["poured"], res)
    return res


def park(r, heading):
    m = r.mark()
    r.cmd("rvdrive tp %s %s" % (AWAY, heading), wait=4)
    bad = r.lines(m, r"\[(Mobile|Drive)\] .*(not ready|no chassis out)")
    r.check("the RV stands away facing %s" % heading, not bad, bad)
    return not bad


def main():
    slot = sys.argv[1] if len(sys.argv) > 1 else "mptest5"
    seconds = float(sys.argv[2]) if len(sys.argv) > 2 else 4.0
    lean = float(sys.argv[3]) if len(sys.argv) > 3 else 6.0
    cooks = int(sys.argv[4]) if len(sys.argv) > 4 else 3
    r = Run(slot)

    def cook_n(label):
        for i in range(cooks):
            cook(r, "%s #%d" % (label, i + 1) if cooks > 1 else label, seconds, results)
    results = []

    r.begin("setup")
    r.cmd("rvdrive home", wait=3)
    r.cmd("rvrepair", wait=1.5)
    r.cmd("rvupg 18", wait=1.5)   # workshop floor (the build grid) and engine
    m = r.mark()
    r.cmd("rvcook start", wait=1.0)
    if r.lines(m, r"no chemistry station in the RV"):
        # The floor grids are two tiles wide, so the 4x2 station only fits turned by 90 degrees.
        lines = r.cmd("rvplace chemistrystation 90", wait=3)
        r.check("a chemistry station is placed on the RV floor", any("rvplace: 'chemistrystation'" in l for l in lines), lines)
    else:
        r.wait_line(m, r"\[Cook\] start", timeout=10)
        r.cmd("rvcook stop", wait=1.5)

    r.begin("at home (control)")
    cook(r, "at home", seconds, results)

    r.begin("parked away, flat")
    r.cmd("rvdrive", wait=3)
    r.cmd("rvdrive exit", wait=1.0)
    if not park(r, 90):
        sys.exit(r.summary())
    cook_n("away, heading 90")

    r.begin("parked away, turned")
    if park(r, 215):
        cook_n("away, heading 215")

    r.begin("parked away, on a slope")
    for l in r.cmd("rvtilt pitch %s" % lean, wait=1.0):
        r.note(l.split("] ", 2)[-1])
    cook_n("away, pitch +%s" % lean)
    for l in r.cmd("rvtilt pitch %s" % (-2 * lean), wait=1.0):
        r.note(l.split("] ", 2)[-1])
    cook_n("away, pitch -%s" % lean)

    r.begin("all poses")
    for name, res in results:
        if res is None:
            r.note("%-22s no result" % name)
        else:
            r.note("%-22s %-7s tilt %5.1f, spout %5.1f of %5.1f, %d collider(s) touched, chassis %s"
                   % (name, "POURED" if res["poured"] else "NO POUR", res["tilt"], res["spout"], res["need"], res["touched"], res["chassis"]))
    errors = r.lines((r.log_path(), 0), r"\[ERROR\] \[RVRepairVan\]")
    r.check("no RV Repair Van errors in the log", not errors, errors[-5:])

    r.cmd("rvdrive home", wait=3)
    sys.exit(r.summary())


if __name__ == "__main__":
    main()
