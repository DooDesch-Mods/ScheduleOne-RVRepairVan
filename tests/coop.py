"""
The RV questline in co-op: a client plays it, the host only watches. Every step checks both journals.

    python tests/coop.py <host slot> <client slot> [part ...]

Start with `pwsh -File tests/coop_up.ps1`: a joined pair on a fresh copy of the save template `played`. The parts run
in this order and build on each other:

    join     the client finishes its character creator and has the host's journal
    marco    the referral, later, the favour, all from the client
    package  the client carries the package to Marco
    pay      the client pays from its own cash, the quest closes at the RV
    home     the second quest: the client asks, buys the engine and the floor
"""
import sys
import time

from check import Run, states
from questline import near


def cash(r):
    return (r.g.call("get_state", {}, timeout_ms=5000).get("result") or {}).get("cash") or 0


def both(h, c, q1=None, q2=None, stage=None, name="both journals"):
    """The same journal on both machines. A value of None is not compared."""
    time.sleep(1.5)
    a, b = h.quests(), c.quests()
    c.note("host   stage=%s mask=%s q1=%s q2=%s" % (a.get("stage"), a.get("mask"), states(a["q1"]), states(a["q2"])))
    c.note("client stage=%s mask=%s q1=%s q2=%s" % (b.get("stage"), b.get("mask"), states(b["q1"]), states(b["q2"])))
    for who, now in (("host", a), ("client", b)):
        if q1 is not None:
            c.check("%s, %s: quest 1 is %s" % (name, who, q1), states(now["q1"]) == q1, states(now["q1"]))
        if q2 is not None:
            c.check("%s, %s: quest 2 is %s" % (name, who, q2), states(now["q2"]) == q2, states(now["q2"]))
        if stage is not None:
            c.check("%s, %s: stage %s" % (name, who, stage), now.get("stage") == stage, now.get("stage"))
    return a, b


def finish_creator(c):
    """A player's first join plays the intro cutscene, then opens the character creator. Both block every conversation."""
    start = time.time()
    end = start + 240
    while time.time() < end:
        if any("done with the default looks" in l for l in c.cmd("rvdrive creator", wait=1)):
            c.note("intro and creator took %d s" % (time.time() - start))
            time.sleep(2)
            return
        time.sleep(2)
    c.note("no character creator opened in 240 s")


def join(h, c):
    c.begin("Join")
    finish_creator(c)
    a, b = both(h, c, q1="A---------", stage="1", name="after the join")
    c.check("client is not repaired", b.get("repaired") == "False", b.get("repaired"))


def marco(h, c):
    c.begin("Client: referral")
    h.cmd("rvstage 5", wait=1.5)
    both(h, c, q1="CCCCA-----", stage="5", name="stage 5")
    near(c, "marco")
    near(h, "marco", off=3)
    m = c.mark()
    c.talk("marco")
    time.sleep(2)
    c.shot("coop-0-first-talk")
    c.check("the first talk is Marco's greeting, without choices", bool(c.dialog()) and not c.choices(), c.choices())
    menu = c.continue_to_choices()
    c.check("his choices follow the greeting", c.wait_line(m, r"Marco's choices follow his greeting", timeout=5) is not None, c.lines(m, r"\[Questline\]"))
    c.shot("coop-0b-after-greeting")
    c.note("Marco offers %r" % menu)
    c.check("the referral is on offer", any("Mrs. Ming" in x for x in menu), menu)
    m = c.mark()
    c.check("clicked the referral", c.click("Mrs. Ming"))
    time.sleep(1.5)
    after = c.wait_choices()
    c.note("after the referral %r" % after)
    c.shot("coop-1-client-quote")
    c.check("the conversation stays open with the quote", any("Repariere es" in x for x in after), after)
    c.check("no bubble in the world", not c.lines(m, r"\[Say\]"), c.lines(m, r"\[Say\]"))
    both(h, c, q1="CCCCCA----", stage="7", name="after the referral")

    c.begin("Client: later and lower")
    c.check("clicked later", c.click("Später"))
    time.sleep(1)
    c.close_dialog()
    c.talk("marco")
    menu = c.wait_choices()
    c.note("reopened: %r" % menu)
    c.check("the referral is gone", not any("Mrs. Ming" in x for x in menu), menu)
    c.check("clicked lower", c.click("billiger"))
    time.sleep(1.5)
    c.close_dialog()
    both(h, c, q1="CCCCCAA---", stage="7", name="after the favour")


def package(h, c):
    c.begin("Client: the package")
    c.cmd("rvgive rv_marco_package", wait=1)
    time.sleep(4)
    both(h, c, q1="CCCCCACA--", name="package in the client's hand")
    near(c, "marco")
    c.talk("marco")
    c.note("offers %r" % c.wait_choices())
    c.check("Marco takes the package", c.click("dein Paket"))
    time.sleep(1)
    c.close_dialog()
    both(h, c, q1="CCCCCACC--", stage="8", name="after the package")


def pay(h, c):
    c.begin("Client: short of cash")
    c.cmd("rvcash -%s" % cash(c), wait=0.8)
    c.note("client cash %s, host cash %s" % (cash(c), cash(h)))
    c.talk("marco")
    c.wait_choices()
    m = c.mark()
    c.check("clicked the repair", c.click("reparieren"))
    reply = c.wait_line(m, r"\[Reply\] marco", timeout=8)
    c.note("reply: %s" % reply)
    time.sleep(2)
    c.shot("coop-2-client-short-of-cash")
    c.check("Marco answers the client in the conversation", reply is not None, c.lines(m, r"\[Reply\]|\[Say\]"))
    both(h, c, stage="8", name="nothing charged")
    c.close_dialog()

    c.begin("Client: the repair")
    c.cmd("rvcash 100000", wait=0.8)
    client_before, host_before = cash(c), cash(h)
    a = h.quests()
    price = int(a.get("price", "0"))
    c.note("price %s, client %s, host %s" % (price, client_before, host_before))
    c.talk("marco")
    c.wait_choices()
    m, hm = c.mark(), h.mark()
    c.check("clicked the repair", c.click("reparieren"))
    line = c.wait_line(m, r"\[Reply\] marco", timeout=40)
    c.note("after the repair: %s" % line)
    time.sleep(2)
    c.shot("coop-3-client-repair-done")
    c.check("Marco speaks to the client after the cinematic", line is not None, c.lines(m, r"\[Say\]"))
    settled = h.wait_line(hm, r"PayRepair ticket \d+ kept", timeout=10)
    c.note("host: %s" % settled)
    c.check("the host keeps the client's payment", settled is not None, h.lines(hm, r"\[Net\]"))
    c.close_dialog()
    c.check("the client paid the price", abs((client_before - cash(c)) - price) < 1, cash(c))
    c.check("the host paid nothing", abs(host_before - cash(h)) < 1, cash(h))
    a, b = both(h, c, q1="CCCCCCCC-A", stage="9", name="after the repair")
    c.check("repaired on the host", a.get("repaired") == "True", a.get("repaired"))
    c.check("repaired on the client", b.get("repaired") == "True", b.get("repaired"))

    c.begin("Client: the first quest ends at the RV")
    m, hm = c.mark(), h.mark()
    near(c, "rv", off=4)
    c.check("the quest closes on the client", c.wait_line(m, r"\[Quest\] 'Wieder auf Achse' completed", timeout=20) is not None, c.lines(m, r"\[Quest\]"))
    c.check("the quest closes on the host", h.wait_line(hm, r"\[Quest\] 'Wieder auf Achse' completed", timeout=20) is not None, h.lines(hm, r"\[Quest\]"))
    c.check("the second quest starts on the client", c.wait_line(m, r"\[Quest\] 'Zuhause auf.*started", timeout=20) is not None, c.lines(m, r"\[Quest\]"))
    c.check("the second quest starts on the host", h.wait_line(hm, r"\[Quest\] 'Zuhause auf.*started", timeout=20) is not None, h.lines(hm, r"\[Quest\]"))
    both(h, c, q2="A---", name="second quest")
    c.shot("coop-4-client-second-quest")


def home(h, c):
    c.begin("Client: ask Marco")
    near(c, "marco")
    c.talk("marco")
    menu = c.wait_choices()
    c.note("offers %r" % menu)
    hm = h.mark()
    c.check("clicked the question", c.click("noch mehr"))
    time.sleep(1.5)
    answer = c.wait_choices()
    c.note("after the question %r" % answer)
    c.shot("coop-5-client-ask-more")
    c.check("the engine is offered in the conversation", any("Motor" in x for x in answer), answer)
    c.check("the host got the question", h.wait_line(hm, r"asked Marco about the build-outs", timeout=10) is not None, h.lines(hm, r"\[Questline\]"))
    both(h, c, q2="CA--", stage="11", name="after the question")

    c.begin("Client: the engine")
    client_before, host_before = cash(c), cash(h)
    m, hm = c.mark(), h.mark()
    c.check("clicked the engine", c.click("Motor"))
    line = c.wait_line(m, r"\[Reply\] marco", timeout=30)
    c.note("after the engine: %s" % line)
    c.check("Marco speaks to the client after the cinematic", line is not None, c.lines(m, r"\[Say\]"))
    time.sleep(3)
    c.close_dialog()
    settled = h.wait_line(hm, r"BuyUpgrade ticket \d+ kept", timeout=10)
    c.check("the host keeps the client's payment", settled is not None, h.lines(hm, r"\[Net\]"))
    c.check("the client paid $10,000", abs((client_before - cash(c)) - 10000) < 1, cash(c))
    c.check("the host paid nothing", abs(host_before - cash(h)) < 1, cash(h))
    both(h, c, q2="CCA-", name="after the engine")

    c.begin("Host drives the RV into the yard")
    h.cmd("rvdrive", wait=2)
    h.cmd("rvdrive exit", wait=3)
    h.cmd("rvdrive tp 10.05 -18.7 90", wait=3)
    bays = [l for l in h.cmd("bodyshop bays", wait=0.8) if "bay 'yard'" in l]
    h.note("bays: %r" % bays)
    time.sleep(4)
    both(h, c, q2="CCCA", name="RV in the yard")
    c.shot("coop-6-client-on-the-lot")

    c.begin("Client: the floor")
    m, hm = c.mark(), h.mark()
    near(c, "marco")
    c.talk("marco")
    c.check("Marco opens his menu", c.click("Motz meine Karre"))
    time.sleep(5)
    c.cmd("bodyshop buy rvrepairvan.workshop/floor", wait=2)
    c.check("the floor ends the quest on the client", c.wait_line(m, r"\[Quest\] 'Zuhause auf.*completed", timeout=30) is not None, c.lines(m, r"\[Quest\]"))
    c.check("the floor ends the quest on the host", h.wait_line(hm, r"\[Quest\] 'Zuhause auf.*completed", timeout=30) is not None, h.lines(hm, r"\[Quest\]"))
    # The host sends the text and the game carries it to the client's phone, so only the host logs it.
    c.check("Marco sends one text", h.wait_line(hm, r"\[Marco\] texted the player", timeout=10) is not None and len(h.lines(hm, r"\[Marco\] texted")) == 1, h.lines(hm, r"\[Marco\]"))
    c.shot("coop-7-client-quest-done")
    c.cmd("bodyshop close", wait=2)
    c.shot("coop-8-client-text")


PARTS = {"join": join, "marco": marco, "package": package, "pay": pay, "home": home}


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    h, c = Run(sys.argv[1]), Run(sys.argv[2])
    for name in sys.argv[3:] or list(PARTS):
        PARTS[name](h, c)
    return c.summary()


if __name__ == "__main__":
    sys.exit(main())
