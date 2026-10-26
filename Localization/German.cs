using System.Collections.Generic;
using DooDesch.Localization;

namespace RVRepairVan.Localization
{
    /// <summary>
    /// German translation table. Keys are the English source strings as they appear in code
    /// (gettext style, see DooDesch.Localization.L10n) - changing an English literal there
    /// requires updating its key here, or that line silently falls back to English.
    /// </summary>
    internal static class German
    {
        internal static void Register()
        {
            L10n.Register("de", new Dictionary<string, string>
            {
                // Quest journal
                ["Back on the Road"] = "Wieder auf Achse",
                ["Your RV's wrecked. Someone in Hyland Point has to know a guy."] =
                    "Dein Wohnmobil ist Schrott. Irgendwer in Hyland Point kennt doch sicher jemanden.",

                // Objectives
                ["Find a way to repair your RV"] = "Finde einen Weg, dein Wohnmobil zu reparieren",
                ["Ask Donna at the motel about your RV"] = "Frag Donna im Motel nach deinem Wohnmobil",
                ["Talk to Mrs. Ming at the Chinese restaurant"] = "Sprich mit Mrs. Ming im Chinarestaurant",
                ["Pick up Mrs. Ming's crate from the dead drop"] = "Hol Mrs. Mings Kiste am Toten Briefkasten ab",
                ["Bring the crate to Mrs. Ming"] = "Bring die Kiste zu Mrs. Ming",
                ["Tell Marco at the docks that Mrs. Ming sent you"] = "Sag Marco am Hafen, dass Mrs. Ming dich schickt",
                ["Pick up Marco's package from the dead drop"] = "Hol Marcos Paket am Toten Briefkasten ab",
                ["Bring the package to Marco"] = "Bring das Paket zu Marco",
                ["Pay Marco for the repair"] = "Bezahl Marco für die Reparatur",
                ["Go see your RV"] = "Sieh dir dein Wohnmobil an",

                // Donna
                ["My RV got blown up. Know anyone who can fix it?"] =
                    "Mein Wohnmobil ist in die Luft geflogen. Kennst du jemanden, der es reparieren kann?",
                ["I run a motel, not a garage. Try Mrs. Ming at the Chinese restaurant, ok? She knows people."] =
                    "Ich führe ein Motel, keine Werkstatt. Frag Mrs. Ming im Chinarestaurant, ok? Die kennt Leute.",

                // Ming
                ["Donna says you know someone who can fix my RV."] =
                    "Donna meint, du kennst jemanden, der mein Wohnmobil reparieren kann.",
                ["Oh yes! Marco, at the docks. Very good mechanic. But first you do small favor for me, yes? Crate in a dead drop. You bring it here, I call Marco."] =
                    "Oh ja! Marco, am Hafen. Sehr guter Mechaniker. Aber erst machst du kleinen Gefallen für mich, ja? Kiste in Totem Briefkasten. Du bringst sie her, ich rufe Marco an.",
                ["I'll get it."] = "Ich hole sie.",
                ["Not now."] = "Jetzt nicht.",
                ["Very good! And do not open, yes?"] = "Sehr gut! Und nicht aufmachen, ja?",
                ["Okay. You come back when ready."] = "Okay. Du kommst wieder, wenn du bereit bist.",
                ["Here's your crate."] = "Hier ist deine Kiste.",
                ["Very good! You are careful. I like careful. I call Marco now. Go see him at the docks, tell him Ming sends you."] =
                    "Sehr gut! Du bist vorsichtig. Ich mag vorsichtig. Ich rufe Marco an. Geh zu ihm an den Hafen, sag ihm, Ming schickt dich.",
                ["I lost your crate."] = "Ich habe deine Kiste verloren.",
                ["You lose my crate? Not very good. Five hundred, then we are okay, yes?"] =
                    "Du verlierst meine Kiste? Nicht sehr gut. Fünfhundert, dann sind wir okay, ja?",
                ["Very good. Now go see Marco at the docks, tell him Ming sends you."] =
                    "Sehr gut. Jetzt geh zu Marco am Hafen, sag ihm, Ming schickt dich.",
                ["Okay. You bring money, yes?"] = "Okay. Du bringst Geld, ja?",

                // Marco
                ["Mrs. Ming sent me. Can you fix my RV?"] = "Mrs. Ming schickt mich. Kannst du mein Wohnmobil reparieren?",
                ["Ming sent ya? Well, any friend of hers. That RV's a burnt-out shell, buddy - another week and she'd have gone to the crusher. Normally that's {0}. For you, {1}."] =
                    "Ming schickt dich? Na, ihre Freunde sind auch meine Freunde. Das Ding ist ein ausgebranntes Wrack, Kumpel - noch eine Woche und es wär in der Schrottpresse gelandet. Normal kostet das {0}. Für dich {1}.",
                ["Repair my RV ({0})"] = "Mein Wohnmobil reparieren ({0})",
                ["Can you go any lower?"] = "Geht's noch billiger?",
                ["Tell ya what. There's a package of mine in a dead drop. Bring it here and we'll talk."] =
                    "Ich sag dir was. In einem Toten Briefkasten liegt ein Paket von mir. Bring's her, dann reden wir.",
                ["Here's your package."] = "Hier ist dein Paket.",
                ["Much obliged. Here's the deal: bring me packaged product in your hand, and every piece comes off your bill."] =
                    "Sehr nett. Der Deal: Bring mir verpackte Ware in der Hand, und jedes Stück geht von deiner Rechnung ab.",
                ["Packaged product, buddy. Have it in your hand when you talk to me - every piece knocks up to {0} off the bill."] =
                    "Verpackte Ware, Kumpel. Halt sie in der Hand, wenn du mit mir redest - jedes Stück drückt die Rechnung um bis zu {0}.",
                ["I lost your package."] = "Ich habe dein Paket verloren.",
                ["You lost it? Well, that's gonna cost ya. Five hundred and we're even."] =
                    "Verloren? Tja, das kostet dich was. Fünfhundert, dann sind wir quitt.",
                ["Pleasure doing business. Now, bring me packaged product in your hand, and every piece comes off your bill."] =
                    "Angenehme Geschäfte. Und jetzt: Bring mir verpackte Ware in der Hand, und jedes Stück geht von deiner Rechnung ab.",
                ["I'll be here."] = "Ich bin hier.",
                ["Your RV looks fine to me."] = "Dein Wohnmobil sieht für mich in Ordnung aus.",
                ["That's {0}, buddy. Come back when you've got it."] = "Das macht {0}, Kumpel. Komm wieder, wenn du's hast.",
                ["Alright, let me grab my tools."] = "Alles klar, ich hol mein Werkzeug.",
                ["There she is, good as new. Try not to blow her up again, buddy."] =
                    "Da steht sie, wie neu. Jag sie nicht gleich wieder in die Luft, Kumpel.",
                ["Hope she runs fine. If you want more out of her, come see me - I do build-outs too."] =
                    "Hoffe, sie läuft. Wenn du mehr aus ihr rausholen willst, komm vorbei - ich mach auch Ausbauten.",
                ["Hope she runs fine, buddy."] = "Hoffe, sie läuft, Kumpel.",

                // Samples
                ["Give Marco a packaged sample"] = "Marco eine verpackte Probe geben",
                ["Give Marco: {0} (-{1})"] = "Marco geben: {0} (-{1})",
                ["product"] = "Ware",
                ["That ain't packaged, buddy. Bring it sealed."] = "Das ist nicht verpackt, Kumpel. Bring's versiegelt.",
                ["Thanks, buddy. That's {0} off."] = "Danke, Kumpel. Das sind {0} weniger.",
                ["Ninety-nine percent pure? Where'd you learn chemistry, buddy? That's {0} off."] =
                    "Neunundneunzig Prozent rein? Wo hast du Chemie gelernt, Kumpel? Das sind {0} weniger.",

                // Loss-fee sub-choices
                ["Pay ${0}"] = "{0} $ zahlen",
                ["I lost your crate. (Pay ${0})"] = "Ich habe deine Kiste verloren. ({0} $ zahlen)",
                ["I lost your package. (Pay ${0})"] = "Ich habe dein Paket verloren. ({0} $ zahlen)",
                ["I'll come back with the money."] = "Ich komme mit dem Geld wieder.",

                // Marco's RV build-outs (each one paid for, each one with its own cinematic)
                ["Put in a work floor ({0})"] = "Einen Arbeitsboden einbauen ({0})",
                ["Make room for a crew ({0})"] = "Platz für eine Crew schaffen ({0})",
                ["Build a loading dock ({0})"] = "Eine Laderampe bauen ({0})",
                ["Get the engine running ({0})"] = "Den Motor zum Laufen bringen ({0})",
                ["Floor's in. You can set up shop in there now."] = "Boden ist drin. Jetzt kannst du da drin aufbauen.",
                ["Room for {0} now. Hire 'em through the Fixer, and put a locker in there so they get paid. Yeah, science."] =
                    "Jetzt ist Platz für {0}. Stell sie über den Fixer ein und stell einen Spind rein, damit sie bezahlt werden. Yeah, Wissenschaft.",
                ["Dock's done. Deliveries can come straight to the RV now."] =
                    "Rampe steht. Lieferungen kommen jetzt direkt zum Wohnmobil.",
                ["She runs. Just don't leave the keys in."] = "Sie läuft. Lass bloß den Schlüssel nicht stecken.",

                // Marco's bodyshop: the RV's furniture and the workshop floor
                ["Can you do more to the RV?"] = "Kannst du noch mehr am Wohnmobil machen?",
                ["Park her on the yellow spot out front, then ask me to modify your vehicle."] =
                    "Stell sie vorne auf den gelben Platz und sag mir dann, dass ich dein Fahrzeug umbauen soll.",
                ["I can't work on her where she sits. Get the engine in first, then drive her over."] =
                    "Da, wo sie steht, kann ich nicht an ihr arbeiten. Erst kommt der Motor rein, dann fährst du sie rüber.",
                ["Interior"] = "Innenraum",
                ["Workshop"] = "Werkstatt",
                ["Workshop Floor"] = "Werkstattboden",
                ["Build on every bit of floor the furniture leaves free."] = "Bau auf jedem Stück Boden, das die Möbel frei lassen.",
                ["{0} of {1} fitted"] = "{0} von {1} eingebaut",
                ["Paid for. Taking it out or putting it back is free."] = "Bezahlt. Aus- und Einbau kosten nichts mehr.",
                ["Pay once, then take it out or put it back for free."] = "Einmal zahlen, danach kosten Aus- und Einbau nichts.",
                ["Clear the floor here first."] = "Räum hier zuerst den Boden frei.",
                ["Repair the RV first."] = "Reparier zuerst das Wohnmobil.",
                ["Bed"] = "Bett",
                ["Kitchen Bench"] = "Küchenzeile",
                ["Upper Cabinets"] = "Oberschränke",
                ["Lower Cabinets"] = "Unterschränke",
                ["Bedroom Wall"] = "Schlafzimmerwand",
                ["Wall Behind Driver"] = "Wand hinter dem Fahrer",
                ["Bedside Table"] = "Nachttisch",
                ["Decor"] = "Deko",
                ["Drive"] = "Fahren",
                ["Seat taken"] = "Platz belegt",
                ["Sit"] = "Hinsetzen",
                ["Missing engine"] = "Motor fehlt",
                ["Battery dead"] = "Batterie leer",
                ["Swap battery"] = "Batterie tauschen",
                ["Left the keys in again? Battery's dead, bring a new one to the wheel."] = "Schon wieder den Schlüssel stecken lassen? Die Batterie ist leer, bring eine neue ans Steuer.",
                ["Leaves of Grass. Signed 'W.W.'"] = "Grashalme. Signiert 'W.W.'",
                ["The RV is away."] = "Das Wohnmobil ist unterwegs.",
                ["Drive it back to its spot."] = "Fahr es zurück an seinen Platz.",

                // Repair-takes-a-day option
                ["Alright, I'll send the boys over to your RV. Give me a day - I'll text ya when she's done."] =
                    "Alles klar, ich schick die Jungs zu deinem Wohnmobil. Gib mir einen Tag - ich schreib dir, wenn sie fertig ist.",
                ["Your RV's all done, buddy. Good as new. Next time, better call Marco."] = "Dein Wohnmobil ist fertig, Kumpel. Wie neu. Nächstes Mal: Better call Marco.",

                // Quest items
                ["Mrs. Ming's Crate"] = "Mrs. Mings Kiste",
                ["A sealed crate for Mrs. Ming, stamped 'Golden Moth Chemical'. Don't open it."] = "Eine versiegelte Kiste für Mrs. Ming, gestempelt 'Golden Moth Chemical'. Nicht öffnen.",
                ["Marco's Package"] = "Marcos Paket",
                ["Marco's package. Smells like fried chicken. Bring it to him at the docks."] = "Marcos Paket. Riecht nach Brathähnchen. Bring es zu ihm an den Hafen.",
            });
        }
    }
}
