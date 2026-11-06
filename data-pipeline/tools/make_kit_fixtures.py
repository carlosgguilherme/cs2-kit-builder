"""Generate shared/test-fixtures/kit-engine/catalog.json (synthetic, invented
names and prices) used by the kit engine tests on every platform."""
import json, os

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "shared", "test-fixtures", "kit-engine", "catalog.json")

RED, DARKRED, BLUE, NAVY, TEAL, GREEN, GOLD, ORANGE, PINK, WHITE = (
    "#c62828", "#7f1d1d", "#1e5bd6", "#1a237e", "#00897b", "#2e7d32", "#c9a227", "#e65100", "#e91e8c", "#f2f2f2")
BLACK, GRAY, LIGHTGRAY, BEIGE = "#1c1c1c", "#5f5f5f", "#9e9e9e", "#b8a27a"

# (id, weapon, category, name, rarity, exterior, priceCents, colors, extra)
ITEMS = [
    ("knife-crimson-fn", "Karambit", "Knives", "Karambit | Crimson Flow", "Covert", "Factory New", 450000, [(RED, .45), (GRAY, .45), (WHITE, .10)], {"multi": True}),
    ("knife-redweb-ft", "Gut Knife", "Knives", "Gut Knife | Red Web", "Covert", "Field-Tested", 90000, [(DARKRED, .40), (BLACK, .45), (GRAY, .15)], {}),
    ("knife-urban-bs", "Navaja Knife", "Knives", "Navaja Knife | Urban", "Covert", "Battle-Scarred", 35000, [(GRAY, .60), (LIGHTGRAY, .40)], {}),
    ("knife-sapphire-fn", "Bayonet", "Knives", "Bayonet | Deep Sapphire", "Covert", "Factory New", 1200000, [(BLUE, .45), (GRAY, .45), (WHITE, .10)], {"multi": True}),
    ("knife-night-ft", "Flip Knife", "Knives", "Flip Knife | Midnight", "Covert", "Field-Tested", 60000, [(BLACK, .70), (GRAY, .30)], {}),
    ("knife-water-mw", "Huntsman Knife", "Knives", "Huntsman Knife | Clear Water", "Covert", "Minimal Wear", 45000, [(BLUE, .35), (GRAY, .55), (WHITE, .10)], {}),
    ("knife-sunset-fn", "Falchion Knife", "Knives", "Falchion Knife | Sunset", "Covert", "Factory New", 150000, [(ORANGE, .30), (PINK, .25), (GRAY, .45)], {"multi": True}),
    ("gloves-vivid-ft", "Sport Gloves", "Gloves", "Sport Gloves | Vivid", "Extraordinary", "Field-Tested", 400000, [(PINK, .40), (TEAL, .35), (BLACK, .25)], {}),
    ("gloves-crimson-ft", "Driver Gloves", "Gloves", "Driver Gloves | Crimson Knit", "Extraordinary", "Field-Tested", 220000, [(RED, .55), (BLACK, .45)], {}),
    ("gloves-spruce-bs", "Hand Wraps", "Gloves", "Hand Wraps | Spruce", "Extraordinary", "Battle-Scarred", 30000, [(GRAY, .55), (GREEN, .45)], {}),
    ("gloves-dark-ft", "Bloodhound Gloves", "Gloves", "Bloodhound Gloves | Charcoal", "Extraordinary", "Field-Tested", 40000, [(BLACK, .65), (GRAY, .35)], {}),
    ("gloves-mint-ft", "Moto Gloves", "Gloves", "Moto Gloves | Mint", "Extraordinary", "Field-Tested", 120000, [(TEAL, .55), (WHITE, .45)], {}),
    ("ak-stripe-ft", "AK-47", "Rifles", "AK-47 | Red Stripe", "Classified", "Field-Tested", 14000, [(BLACK, .75), (GRAY, .17), (DARKRED, .08)], {"sales": 900}),
    ("ak-stripe-ft-st", "AK-47", "Rifles", "AK-47 | Red Stripe", "Classified", "Field-Tested", 30000, [(BLACK, .75), (GRAY, .17), (DARKRED, .08)], {"stattrak": True, "skin": "ak-stripe"}),
    ("ak-blood-mw", "AK-47", "Rifles", "AK-47 | Blood Rush", "Covert", "Minimal Wear", 30000, [(RED, .40), (BLACK, .35), (GRAY, .25)], {"sales": 300}),
    ("ak-crimson-ft", "AK-47", "Rifles", "AK-47 | Crimson Lines", "Mil-Spec Grade", "Field-Tested", 4500, [(RED, .35), (BLACK, .45), (GRAY, .20)], {"sales": 1200}),
    ("ak-slate-ft", "AK-47", "Rifles", "AK-47 | Slate", "Restricted", "Field-Tested", 1500, [(GRAY, .70), (BLACK, .30)], {"sales": 5000}),
    ("ak-laminate-mw", "AK-47", "Rifles", "AK-47 | Blue Layers", "Mil-Spec Grade", "Minimal Wear", 3500, [(BLUE, .45), (NAVY, .25), (GRAY, .30)], {"sales": 400}),
    ("ak-serpent-ft", "AK-47", "Rifles", "AK-47 | Green Serpent", "Covert", "Field-Tested", 250000, [(GREEN, .40), (BEIGE, .30), (BLACK, .30)], {}),
    ("ak-hardened-bs", "AK-47", "Rifles", "AK-47 | Tempered", "Classified", "Battle-Scarred", 60000, [(BLUE, .35), (GOLD, .30), (GRAY, .35)], {"multi": True}),
    ("m4-howler-ft", "M4A4", "Rifles", "M4A4 | Howler", "Contraband", "Field-Tested", 2500000, [(RED, .45), (ORANGE, .20), (BLACK, .35)], {}),
    ("m4-beast-ft", "M4A1-S", "Rifles", "M4A1-S | Wild Beast", "Covert", "Field-Tested", 8000, [(BLUE, .35), (TEAL, .25), (BLACK, .40)], {"sales": 1500}),
    ("m4-daimyo-ft", "M4A4", "Rifles", "M4A4 | Red Lord", "Restricted", "Field-Tested", 1200, [(RED, .35), (BLACK, .45), (GRAY, .20)], {"sales": 2500}),
    ("m4-nitro-ft", "M4A1-S", "Rifles", "M4A1-S | Nitrous", "Classified", "Field-Tested", 900, [(GRAY, .60), (RED, .15), (WHITE, .25)], {"sales": 1800}),
    ("m4-space-ft", "M4A4", "Rifles", "M4A4 | Deep Space", "Covert", "Field-Tested", 3000, [(NAVY, .45), (BLUE, .25), (BLACK, .30)], {"sales": 700}),
    ("m4-lizard-mw", "M4A1-S", "Rifles", "M4A1-S | Lizard", "Restricted", "Minimal Wear", 1500, [(GRAY, .55), (LIGHTGRAY, .45)], {"sales": 900}),
    ("awp-desert-ft", "AWP", "Sniper", "AWP | Desert Sim", "Covert", "Field-Tested", 45000, [(WHITE, .45), (ORANGE, .20), (BLACK, .35)], {"sales": 600}),
    ("awp-viper-ft", "AWP", "Sniper", "AWP | Viper", "Classified", "Field-Tested", 600, [(GRAY, .65), (LIGHTGRAY, .35)], {"sales": 3000}),
    ("awp-veins-ft", "AWP", "Sniper", "AWP | Red Veins", "Mil-Spec Grade", "Field-Tested", 400, [(RED, .30), (BLACK, .50), (GRAY, .20)], {"sales": 2000}),
    ("awp-coil-ft", "AWP", "Sniper", "AWP | Blue Coil", "Covert", "Field-Tested", 1500, [(BLUE, .40), (BLACK, .40), (GRAY, .20)], {"sales": 1000}),
    ("awp-stripe-ft", "AWP", "Sniper", "AWP | Red Stripe", "Classified", "Field-Tested", 8000, [(BLACK, .70), (RED, .15), (GRAY, .15)], {"sales": 800}),
    ("awp-lore-ft", "AWP", "Sniper", "AWP | Golden Legend", "Covert", "Field-Tested", 6000000, [(GOLD, .50), (BEIGE, .30), (BLACK, .20)], {}),
    ("awp-pink-ft-sv", "AWP", "Sniper", "AWP | Pink Camo", "Mil-Spec Grade", "Field-Tested", 50000, [(PINK, .35), (GRAY, .65)], {"souvenir": True}),
    ("deagle-code-ft", "Desert Eagle", "Pistols", "Desert Eagle | Code Crimson", "Covert", "Field-Tested", 4000, [(RED, .40), (BLACK, .40), (GRAY, .20)], {"sales": 900}),
    ("deagle-blaze-fn", "Desert Eagle", "Pistols", "Desert Eagle | Flame", "Restricted", "Factory New", 250000, [(ORANGE, .45), (BLACK, .55)], {}),
    ("deagle-night-ft", "Desert Eagle", "Pistols", "Desert Eagle | Nightfall", "Mil-Spec Grade", "Field-Tested", 500, [(BLACK, .70), (GRAY, .30)], {"sales": 1200}),
    ("deagle-ply-ft", "Desert Eagle", "Pistols", "Desert Eagle | Blue Grid", "Mil-Spec Grade", "Field-Tested", 400, [(BLUE, .40), (GRAY, .60)], {"sales": 800}),
    ("deagle-web-ft", "Desert Eagle", "Pistols", "Desert Eagle | Red Web", "Covert", "Field-Tested", 3000, [(DARKRED, .45), (BLACK, .40), (GRAY, .15)], {"sales": 400}),
    ("usp-confirmed-ft", "USP-S", "Pistols", "USP-S | Confirmed", "Covert", "Field-Tested", 20000, [(RED, .35), (BEIGE, .30), (BLACK, .35)], {"sales": 700}),
    ("glock-element-ft", "Glock-18", "Pistols", "Glock-18 | Elemental", "Classified", "Field-Tested", 1500, [(RED, .35), (BLUE, .25), (BLACK, .40)], {"sales": 1500, "multi": True}),
    ("usp-cyber-ft", "USP-S", "Pistols", "USP-S | Cyber", "Classified", "Field-Tested", 1200, [(RED, .30), (GRAY, .50), (WHITE, .20)], {"sales": 1300}),
    ("glock-dune-bs", "Glock-18", "Pistols", "Glock-18 | Dune", "Consumer Grade", "Battle-Scarred", 50, [(BEIGE, .80), (GRAY, .20)], {"sales": 8000}),
    ("usp-blueprint-ft", "USP-S", "Pistols", "USP-S | Blueprint", "Mil-Spec Grade", "Field-Tested", 600, [(BLUE, .50), (WHITE, .20), (GRAY, .30)], {"sales": 900}),
    ("glock-vogue-ft", "Glock-18", "Pistols", "Glock-18 | Vogue", "Classified", "Field-Tested", 800, [(PINK, .30), (TEAL, .25), (ORANGE, .20), (BLACK, .25)], {"sales": 1100, "multi": True}),
    ("owned:ak-stripe-ft", "AK-47", "Rifles", "AK-47 | Red Stripe", "Classified", "Field-Tested", 14000, [(BLACK, .75), (GRAY, .17), (DARKRED, .08)], {"owned": True, "skin": "ak-stripe"}),
]

CATEGORY = {"Sniper": "Rifles"}


def main():
    out = []
    for item_id, weapon, cat, name, rarity, ext, price, colors, extra in ITEMS:
        out.append({
            "itemId": item_id,
            "skinId": extra.get("skin") or item_id.rsplit("-", 1)[0].replace("owned:", ""),
            "name": ("★ " if cat in ("Knives", "Gloves") else "") + name,
            "weapon": weapon, "category": CATEGORY.get(cat, cat), "rarity": rarity, "exterior": ext,
            "statTrak": bool(extra.get("stattrak")), "souvenir": bool(extra.get("souvenir")),
            "priceCents": price, "salesVolume": extra.get("sales"), "owned": bool(extra.get("owned")),
            "isMultiColor": bool(extra.get("multi")),
            "colors": [{"hex": h, "weight": w} for h, w in colors],
        })
    doc = {"description": "Synthetic catalog for kit engine tests. Names and prices are invented.",
           "currency": "BRL", "candidates": out}
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
    print(len(out), "candidates")


if __name__ == "__main__":
    main()
