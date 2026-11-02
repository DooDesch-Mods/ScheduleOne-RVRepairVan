"""
The RV questline in a running game, with real clicks on the dialogue choices.

    python tests/questline.py <slot> [part ...]

Parts run in the order given; without a part all of them run. Start on a fresh copy of the save template `played`
(session.ps1 -Profile main), whose RV is wrecked at stage 1 with one journal entry from an older build.

    old       the old save: the quest opens with the ten steps and Donna active
    errands   Donna, Ming and the crate, stage by stage
    marco     Mrs. Ming's referral keeps the conversation open: quote, later, the package run, a save and load
    pay       a sample, short of cash, the repair with its cinematic, then the RV
    home      the second quest: ask Marco, the engine, the drive to the yard, the floor, nothing left after a load
    oldsaves  saves that were further along before this quest existed
    day       the setting RepairTakesADay: the booked job, the wait, Marco's text
    simple    the setting QuestMode Simple: no journal quests
"""
import sys
import time

from check import Run, states

SAVE = 5


def near(r, who, off=1.2):
    """Stand next to an NPC or the RV, wherever it is right now. `who` is donna, ming, marco or rv."""
    pos = r.quests().get("at_" + who)
    if not pos:
        r.note("%s has no position, the player stays where they are" % who)
        return
    r.tp(pos[0] + off, pos[1] + 0.2, pos[2] + off)


def q(r):
    now = r.quests()
    r.note("stage=%s mask=%s repaired=%s q1=%s q2=%s" % (now.get("stage"), now.get("mask"), now.get("repaired"), states(now["q1"]), states(now["q2"])))
    return now


def said(lines, part):
    return [l for l in lines if "[Reply]" in l and part in l]


# ---- the parts ---------------------------------------------------------------------------------


def old(r):
    r.begin("old save")
    now = q(r)
    r.check("stage 1, the RV is wrecked", now.get("stage") == "1" and now.get("repaired") == "False", now.get("stage"))
    r.check("ten steps, Donna active", states(now["q1"]) == "A---------", states(now["q1"]))
    r.check("no second quest yet", now["q2"] is None)


def errands(r):
    r.begin("Donna, Ming, crate")
    r.cmd("rvstage 1", wait=1)
    near(r, "donna")
    r.talk("donna")
    r.note("Donna offers %r" % r.choices())
    r.check("Donna offers the RV question", r.click("in die Luft geflogen"))
    time.sleep(1)
    r.close_dialog()
    now = q(r)
    r.check("after Donna the journal says Ming", states(now["q1"])[:2] == "CA", states(now["q1"]))

    near(r, "ming")
    r.talk("ming")
    r.note("Ming offers %r" % r.choices())
    r.check("Ming offers the question", r.click("Donna meint"))
    time.sleep(1)
    r.note("Ming then offers %r" % r.wait_choices())
    r.check("Ming's errand can be accepted", r.click("hole"))
    time.sleep(1)
    r.close_dialog()
    now = q(r)
    r.check("after Ming the journal says crate", states(now["q1"])[:3] == "CCA", states(now["q1"]))

    r.cmd("give rv_ming_crate", wait=1)
    time.sleep(4)
    now = q(r)
    r.check("with the crate in hand the journal says bring it", states(now["q1"])[:4] == "CCCA", states(now["q1"]))
    near(r, "ming")
    r.talk("ming")
    r.check("Ming takes the crate", r.click("deine Kiste"))
    time.sleep(1)
    r.close_dialog()
    now = q(r)
    r.check("after the crate the journal says Marco", states(now["q1"])[:5] == "CCCCA" and now.get("stage") == "5", states(now["q1"]))


def not_greeted(r, stage):
    """A save whose player never spoke to Marco. The template's player has, and the game reads this on load."""
    r.cmd("rvstage %d" % stage, wait=1.2)
    r.cmd("setvar MarcoGreeted false", wait=0.8)
    r.save()
    r.load(SAVE)
    near(r, "marco")


def marco(r):
    r.begin("Marco: greeting before Mrs. Ming sent the player")
    not_greeted(r, 1)
    m = r.mark()
    r.talk("marco")
    time.sleep(2)
    r.check("the first talk is Marco's greeting", bool(r.dialog()) and not r.choices(), r.choices())
    r.close_dialog()
    time.sleep(2)
    r.check("the greeting stays on its own", not r.dialog() and bool(r.lines(m, r"greeting stays on its own")), r.lines(m, r"\[Questline\]"))

    r.begin("Marco: referral")
    not_greeted(r, 5)
    now = q(r)
    r.check("stage 5 opens 'tell Marco'", states(now["q1"])[:5] == "CCCCA", states(now["q1"]))
    m = r.mark()
    r.talk("marco")
    time.sleep(2)
    r.shot("after-0-greeting")
    r.check("the first talk is Marco's greeting, without choices", bool(r.dialog()) and not r.choices(), r.choices())
    choice_list = r.continue_to_choices()
    r.check("his choices follow the greeting", bool(r.lines(m, r"Marco's choices follow his greeting")), r.lines(m, r"\[Questline\]"))
    r.shot("after-1-marco-open")
    r.note("Marco offers %r" % choice_list)
    r.check("the referral is on offer", any("Mrs. Ming" in c for c in choice_list))

    m = r.mark()
    r.check("clicked the referral", r.click("Mrs. Ming"))
    time.sleep(1.2)
    after = r.wait_choices()
    r.note("after the referral %r" % after)
    r.shot("after-2-quote")
    r.check("the conversation stays open with the quote", any("Repariere es" in c for c in after), after)
    r.check("'lower' is on offer", any("billiger" in c for c in after), after)
    r.check("'later' is on offer", any("Später" in c for c in after), after)
    r.check("no bubble in the world", not r.lines(m, r"\[Say\]"), r.lines(m, r"\[Say\]"))
    now = q(r)
    r.check("stage 7, 'pay Marco' active with the price", states(now["q1"])[:6] == "CCCCCA" and now.get("stage") == "7", states(now["q1"]))
    r.note("pay step: %s" % now["q1"][5][1])
    r.check("the pay step names the price", "$" in now["q1"][5][1], now["q1"][5][1])

    r.begin("Marco: later and lower")
    r.check("clicked later", r.click("Später"))
    time.sleep(1)
    r.close_dialog()
    r.check("later closes the conversation", not r.dialog())
    r.talk("marco")
    menu = r.wait_choices()
    r.note("reopened: %r" % menu)
    r.check("the repair stays on offer", any("reparieren" in c for c in menu), menu)
    r.check("the referral is gone", not any("Mrs. Ming" in c for c in menu), menu)
    r.check("clicked lower", r.click("billiger"))
    time.sleep(1.2)
    r.shot("after-3-favour")
    r.close_dialog()
    now = q(r)
    r.check("the package run shows beside the payment", states(now["q1"])[:7] == "CCCCCAA", states(now["q1"]))
    r.check("errand is set", now.get("errand") == "1", now.get("errand"))
    r.check("pickup is set", now.get("pickup") == "True", now.get("pickup"))

    r.begin("Marco: the package run survives a load")
    before = [l for l in r.cmd("rvdrops", wait=0.8) if "ackage" in l or "pkg" in l.lower()]
    r.note("drops before: %r" % before[:6])
    r.save()
    m = r.load(SAVE)
    now = q(r)
    r.check("after the load the package run still shows", states(now["q1"])[:7] == "CCCCCAA", states(now["q1"]))
    r.check("errand is restored", now.get("errand") == "1", now.get("errand"))
    r.check("one quest, no copy", len(r.lines(m, r"\[Quest\] 'Wieder auf Achse' started")) <= 1, r.lines(m, r"\[Quest\].*started"))
    after = [l for l in r.cmd("rvdrops", wait=0.8) if "ackage" in l or "pkg" in l.lower()]
    r.note("drops after: %r" % after[:6])


def pay(r):
    r.begin("Marco: package in hand")
    r.cmd("give rv_marco_package", wait=1)
    time.sleep(4)
    now = q(r)
    r.check("with the package the journal says bring it", states(now["q1"])[:8] == "CCCCCACA", states(now["q1"]))
    near(r, "marco")
    r.talk("marco")
    r.note("offers %r" % r.wait_choices())
    r.check("Marco takes the package", r.click("dein Paket"))
    time.sleep(1)
    r.close_dialog()
    now = q(r)
    r.check("stage 8, the package run is done", now.get("stage") == "8" and states(now["q1"])[:8] == "CCCCCACC", "%s %s" % (now.get("stage"), states(now["q1"])))

    r.begin("Marco: a sample")
    price_before = int(now.get("price", "0"))
    r.cmd("rvtest", wait=1)
    r.cmd("rvhold 1", wait=1)
    r.talk("marco")
    menu = r.wait_choices()
    r.note("holding product: %r" % menu)
    m = r.mark()
    sample = [c for c in menu if "$" in c and "reparieren" not in c]
    if r.check("a sample is on offer", bool(sample), menu):
        r.check("clicked the sample", r.click(sample[0].split("] ", 1)[1]))
        reply = r.wait_line(m, r"\[Reply\] marco", timeout=8)
        r.note("reply: %s" % reply)
        r.check("Marco answers in the conversation", reply is not None, r.lines(m, r"\[Say\]"))
        r.check("no bubble in the world", not r.lines(m, r"\[Say\]"), r.lines(m, r"\[Say\]"))
        time.sleep(1.5)
        r.shot("after-3b-sample")
    r.close_dialog()
    now = q(r)
    r.note("price %s -> %s; pay step: %s" % (price_before, now.get("price"), now["q1"][5][1]))
    r.check("the price fell", int(now.get("price", "0")) < price_before, now.get("price"))
    r.check("the pay step shows the new price", "{:,}".format(int(now.get("price"))) in now["q1"][5][1], now["q1"][5][1])

    r.begin("Marco: short of cash")
    cash = (r.g.call("get_state", {}, timeout_ms=5000).get("result") or {}).get("cash") or 0
    r.cmd("changecash -%d" % int(cash), wait=0.8)
    cash_left = (r.g.call("get_state", {}, timeout_ms=5000).get("result") or {}).get("cash") or 0
    r.note("cash now %s" % cash_left)
    r.talk("marco")
    r.wait_choices()
    m = r.mark()
    r.check("clicked the repair", r.click("reparieren"))
    reply = r.wait_line(m, r"\[Reply\] marco", timeout=8)
    r.note("reply: %s" % reply)
    time.sleep(2)
    r.shot("after-4-short-of-cash")
    r.check("Marco answers in the conversation", reply is not None, r.lines(m, r"\[Reply\]|\[Say\]"))
    r.check("the conversation stays on screen", bool(r.dialog()))
    r.check("no bubble in the world", not r.lines(m, r"\[Say\]"), r.lines(m, r"\[Say\]"))
    now = q(r)
    r.check("nothing was charged, still not paid", now.get("stage") in ("7", "8") and now.get("repaired") == "False", now.get("stage"))
    r.close_dialog()

    r.begin("Marco: the repair")
    r.cmd("changecash 100000", wait=0.8)
    r.talk("marco")
    r.wait_choices()
    m = r.mark()
    r.check("clicked the repair", r.click("reparieren"))
    line = r.wait_line(m, r"\[Reply\] marco", timeout=40)
    time.sleep(2)
    r.note("after the repair: %s" % line)
    r.check("Marco speaks after the cinematic", line is not None)
    r.shot("after-5-repair-done")
    r.check("the conversation is open after the cinematic", bool(r.dialog()))
    r.close_dialog()
    r.check("the conversation closes", not r.dialog())
    cash_after = (r.g.call("get_state", {}, timeout_ms=5000).get("result") or {}).get("cash") or 0
    r.note("cash after the repair: %s" % cash_after)
    now = q(r)
    r.check("stage 9, 'see your RV' active", now.get("stage") == "9" and states(now["q1"])[9] == "A", "%s %s" % (now.get("stage"), states(now["q1"])))
    r.check("the RV is repaired", now.get("repaired") == "True", now.get("repaired"))

    r.begin("The first quest ends at the RV")
    r.save()
    m = r.load(SAVE)
    now = q(r)
    r.check("after a load 'see your RV' is still open", states(now["q1"])[9] == "A", states(now["q1"]))
    m = r.mark()
    r.cmd("rvdrive home", wait=1)
    r.cmd("rvdrive exit", wait=1)
    near(r, "rv", off=4)
    r.check("the quest closes at the RV", r.wait_line(m, r"\[Quest\] 'Wieder auf Achse' completed", timeout=20) is not None, r.lines(m, r"\[Quest\]"))
    r.check("the second quest starts", r.wait_line(m, r"\[Quest\] 'Zuhause auf.*started", timeout=20) is not None, r.lines(m, r"\[Quest\]"))
    now = q(r)
    r.check("Home on Wheels opens with 'ask Marco'", states(now["q2"]) == "A---", states(now["q2"]))
    r.shot("after-6-second-quest")


def home(r):
    r.begin("Home on Wheels: ask Marco")
    near(r, "marco")
    r.talk("marco")
    menu = r.wait_choices()
    r.note("offers %r" % menu)
    r.check("the question is on offer", any("noch mehr" in c for c in menu), menu)
    r.check("the engine is on offer", any("Motor" in c for c in menu), menu)
    r.check("clicked the question", r.click("noch mehr"))
    time.sleep(1.2)
    answer = r.wait_choices()
    r.note("after the question %r" % answer)
    r.shot("after-7-ask-more")
    r.check("the engine is offered in the conversation", any("Motor" in c for c in answer), answer)
    r.check("'later' is on offer", any("Später" in c for c in answer), answer)
    now = q(r)
    r.check("the question is ticked off", states(now["q2"]) == "CA--", states(now["q2"]))

    r.begin("Home on Wheels: the engine")
    m = r.mark()
    r.check("clicked the engine", r.click("Motor"))
    line = r.wait_line(m, r"\[Reply\] marco", timeout=30)
    r.note("after the engine: %s" % line)
    r.check("Marco speaks after the cinematic", line is not None, r.lines(m, r"\[Say\]"))
    time.sleep(3)
    r.shot("after-8-engine-done")
    r.close_dialog()
    now = q(r)
    r.check("the drive to the lot is active", states(now["q2"]) == "CCA-", states(now["q2"]))

    r.begin("Home on Wheels: the yard")
    yard(r)

    r.begin("Home on Wheels: the floor")
    m = r.mark()
    near(r, "marco")
    r.talk("marco")
    r.check("Marco opens his menu", r.click("Motz meine Karre"))
    time.sleep(5)
    r.cmd("bodyshop buy rvrepairvan.workshop/floor", wait=2)
    done = r.wait_line(m, r"\[Quest\] 'Zuhause auf.*completed", timeout=30)
    r.check("the floor ends the quest", done is not None, r.lines(m, r"\[Quest\]"))
    text = r.wait_line(m, r"\[Marco\] texted the player", timeout=10)
    r.check("Marco texts the player", text is not None, text)
    r.shot("after-9-quest-done")
    r.cmd("bodyshop close", wait=2)

    r.begin("Home on Wheels: nothing is left after a load")
    r.save()
    m = r.load(SAVE)
    now = q(r)
    r.check("no quest in the journal after the load", states(now["q2"]) in ("<none>", "CCCC"), states(now["q2"]))
    r.check("nothing started after the load", not r.lines(m, r"\[Quest\].*started"), r.lines(m, r"\[Quest\]"))


def yard(r):
    r.begin("Home on Wheels: the yard")
    r.cmd("rvdrive", wait=2)
    r.cmd("rvdrive exit", wait=3)
    r.cmd("rvdrive tp 10.05 -18.7 90", wait=3)
    bays = [l for l in r.cmd("bodyshop bays", wait=0.8) if "bay 'yard'" in l]
    r.note("bays: %r" % bays)
    r.check("the RV stands in the yard", bool(bays) and "empty" not in bays[0], bays)
    time.sleep(3)
    now = q(r)
    r.check("in the yard the floor step is active", states(now["q2"]) == "CCCA", states(now["q2"]))
    near(r, "marco")
    r.talk("marco")
    menu = r.wait_choices()
    r.note("offers %r" % menu)
    r.check("Marco no longer offers 'do more' with the RV in his yard", not any("noch mehr" in c for c in menu), menu)
    r.shot("after-8b-on-the-lot")
    r.close_dialog()


def oldsaves(r):
    """Saves from before the second quest: the repair and the purchases are there, the quest is not. Each case is saved and loaded."""
    cases = [
        ("repaired, nothing bought", 10, 0, "A---"),
        ("repaired at stage 0, from Simple mode", 0, 0, "A---"),
        ("engine only", 10, 16, "CCA-"),
        ("floor only", 10, 2, "CA-C"),
        ("crew only", 10, 4, "CA--"),
        ("engine and floor", 10, 18, "<none>"),
        ("everything", 10, 30, "<none>"),
        ("asked Marco, then saved", 11, 0, "CA--"),
    ]
    r.cmd("rvdrive home", wait=2)
    for name, stage, mask, want in cases:
        r.begin("Old save: " + name)
        r.cmd("rvupg %d" % mask, wait=1)
        r.cmd("rvstage %d" % stage, wait=1)
        r.save()
        m = r.load(SAVE)
        now = q(r)
        r.check("the journal is %s" % want, states(now["q2"]) == want, states(now["q2"]))
        r.check("the first quest stays closed", now["q1"] is None, states(now["q1"]))
        started = r.lines(m, r"\[Quest\] 'Zuhause auf.*started")
        r.check("started at most once", len(started) <= 1, started)


def day(r):
    """RepairTakesADay: Marco books the job in the conversation, the journal waits, his text moves it on."""
    r.begin("Repair takes a day")
    r.cmd("rvwreck", wait=2)
    r.cmd("rvpref RepairTakesADay true", wait=0.6)
    r.cmd("rvstage 7", wait=1.2)
    r.cmd("changecash 100000", wait=0.6)
    near(r, "marco")
    r.talk("marco")
    r.wait_choices()
    m = r.mark()
    r.check("clicked the repair", r.click("reparieren"))
    reply = r.wait_line(m, r"\[Reply\] marco", timeout=30)
    r.note("reply: %s" % reply)
    time.sleep(2)
    r.shot("after-10-booked")
    r.check("Marco books the job in the conversation", reply is not None, r.lines(m, r"\[Say\]"))
    r.close_dialog()
    now = q(r)
    r.check("the journal waits for Marco's text", now.get("stage") == "9" and states(now["q1"])[8:] == "A-", states(now["q1"]))
    near(r, "rv", off=4)
    time.sleep(4)
    now = q(r)
    r.check("near the wreck nothing completes", states(now["q1"])[8:] == "A-", states(now["q1"]))
    m = r.mark()
    r.cmd("rvjob", wait=1)
    text = r.wait_line(m, r"\[Marco\] texted the player", timeout=15)
    r.note("text: %s" % text)
    time.sleep(1)
    now = q(r)
    if now["q1"] is not None:
        r.check("after the text the journal says see the RV, or the quest closed at once", states(now["q1"])[8:] in ("CA", "-A"), states(now["q1"]))
    r.check("the RV is repaired", now.get("repaired") == "True", now.get("repaired"))
    done = r.wait_line(m, r"\[Quest\] 'Wieder auf Achse' completed", timeout=15)
    r.check("standing at the RV the quest closes", done is not None, r.lines(m, r"\[Quest\]"))
    r.cmd("rvpref RepairTakesADay false", wait=0.6)


def simple(r):
    """Simple mode: a save from the questline loses its journal quests."""
    r.begin("Simple mode")
    r.cmd("rvrepair", wait=2)
    r.cmd("rvupg 0", wait=1)
    r.cmd("rvstage 10", wait=1)
    time.sleep(7)
    now = q(r)
    r.check("Questline mode shows the second quest", states(now["q2"]) == "A---", states(now["q2"]))
    r.cmd("setvar MarcoGreeted false", wait=0.8)
    r.save()
    r.cmd("rvpref QuestMode Simple", wait=0.6)
    m = r.load(SAVE, settle=12)
    now = q(r)
    r.check("no second quest in Simple mode", now["q2"] is None, states(now["q2"]))
    r.check("the cancel is logged", bool(r.lines(m, r"\[Quest\].*cancelled")), r.lines(m, r"\[Quest\]"))
    near(r, "marco")
    m = r.mark()
    r.talk("marco")
    time.sleep(2)
    r.close_dialog()
    time.sleep(2)
    r.check("Marco's greeting stays on its own", not r.dialog() and bool(r.lines(m, r"greeting stays on its own")), r.lines(m, r"\[Questline\]"))
    # Back to the menu writes the preferences to the slot, so the next start of this slot is in Questline mode again.
    r.cmd("rvpref QuestMode Questline", wait=0.6)
    r.load(SAVE, settle=12)
    now = q(r)
    r.check("back in Questline mode the second quest returns", states(now["q2"]) == "A---", states(now["q2"]))


PARTS = {"old": old, "errands": errands, "marco": marco, "pay": pay, "home": home, "oldsaves": oldsaves, "day": day, "simple": simple}


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    r = Run(sys.argv[1])
    names = sys.argv[2:] or list(PARTS)
    for name in names:
        PARTS[name](r)
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
