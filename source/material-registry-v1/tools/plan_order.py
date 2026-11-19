#!/usr/bin/env python3
"""Greedy implementation order for JOSCRAFT materials. Reads material-registry.json + recipe-catalog.json, writes implementation-order.csv.

A recipe counts as AVAILABLE when: its output is known, every non-tag input is known (aux/data>0 inputs are never known),
every tag input has at least one known member (official member or community CANDIDATE – marked as needing a runtime binding),
and at least one of its stations is a known station (crafting_table/furnace already exist; others become known when their block is added).
"""
import json, csv, sys, re, collections

reg = json.load(open(sys.argv[1], encoding="utf-8"))
cat = json.load(open(sys.argv[2], encoding="utf-8"))
out = sys.argv[3]
entries = {e["id"]: e for e in reg["entries"]}
tags = {t["id"]: t for t in reg["tags"]}
known = {e["id"] for e in reg["entries"] if e["joscraft"]["stableId"] is not None}
STATION_BLOCK = {"crafting_table": "minecraft:crafting_table", "furnace": "minecraft:furnace", "stonecutter": "minecraft:stonecutter_block",
                 "blast_furnace": "minecraft:blast_furnace", "smoker": "minecraft:smoker", "campfire": "minecraft:campfire",
                 "soul_campfire": "minecraft:soul_campfire", "brewing_stand": "minecraft:brewing_stand", "smithing_table": "minecraft:smithing_table",
                 "cartography_table": "minecraft:cartography_table"}

def split(raw):
    p = raw.split(":")
    if len(p) == 3 and re.fullmatch(r"-?\d+", p[2]): return p[0] + ":" + p[1], int(p[2])
    return raw, None

recipes = []
for r in cat["recipes"]:
    if "deprecated" in r["stations"]: continue
    ins, ok = [], True
    for role in ("keys", "ingredients", "input", "reagent", "base", "addition", "template"):
        v = r.get(role)
        if v is None: continue
        vals = list(v.values()) if role == "keys" else ([v] if isinstance(v, dict) else v)
        for e in vals:
            for x in (e if isinstance(e, list) else [e]):
                if x.get("tag"): ins.append(("tag", x["id"]))
                else:
                    base, aux = split(x["id"]); d = x.get("data", aux)
                    ins.append(("id", base) if not d else ("unknown", f"{base}@{d}"))
    outs = [split(o["id"])[0] if not o.get("data") else f"{o['id']}@{o['data']}" for o in r.get("outputs", [])]
    if not outs: continue
    recipes.append({"file": r["file"], "ins": ins, "outs": outs, "stations": r["stations"]})

def members(tag):
    t = tags.get(tag)
    if not t: return set()
    m = set(t["officialMembers"]) if isinstance(t["officialMembers"], list) else set()
    if isinstance(t["communityCandidates"], list): m |= set(t["communityCandidates"])
    return m

def available(r, K):
    if not all(o in K for o in r["outs"]): return False
    if not any(STATION_BLOCK.get(s) in K for s in r["stations"]): return False
    for kind, v in r["ins"]:
        if kind == "unknown": return False
        if kind == "id" and v not in K: return False
        if kind == "tag" and not (members(v) & K): return False
    return True

def avail_set(K): return {r["file"] for r in recipes if available(r, K)}

involve = collections.defaultdict(list)
for r in recipes:
    for kind, v in r["ins"]:
        if kind == "id": involve[v].append(r)
        if kind == "tag":
            for m in members(v): involve[m].append(r)
    for o in r["outs"]: involve[o].append(r)
    for st in r["stations"]:
        if st in STATION_BLOCK: involve[STATION_BLOCK[st]].append(r)

def missing_deps(mid, K):
    best = None
    for r in recipes:
        if mid not in r["outs"]: continue
        miss = sorted({v for k, v in r["ins"] if k == "id" and v not in K} | {"#" + v for k, v in r["ins"] if k == "tag" and not (members(v) & K)}
                      | {v for k, v in r["ins"] if k == "unknown"} | ({"station:" + "/".join(r["stations"])} if not any(STATION_BLOCK.get(s) in K for s in r["stations"]) else set()))
        if best is None or len(miss) < len(best): best = miss
    return best  # None = no recipe produces it (world/loot/mob source)

W = r"(oak|spruce|birch)"
SETS = [
    ("1_wood", lambda i: re.search(rf"^minecraft:({W}_(slab|stairs|fence|door|trapdoor|button|pressure_plate|sign|hanging_sign|boat|chest_boat|wood|shelf)|stripped_{W}_(log|wood)|wooden_(door|button|pressure_plate|shovel|hoe)|fence_gate|{W}_fence_gate|trapdoor|chest|barrel|ladder|bowl|oak_sign|crafting_table)$", i)),
    ("2_stone_building", lambda i: re.search(r"^minecraft:(stone_(slab|stairs|bricks?|brick_(slab|stairs|wall)|button|pressure_plate|shovel|hoe)|cobblestone_(slab|stairs|wall)|normal_stone_(slab|stairs)|smooth_stone(_slab)?|brick_(slab|stairs|wall)|stone_brick_(slab|stairs|wall)|cracked_stone_bricks|chiseled_stone_bricks|stonebrick)$", i)),
    ("3_glass_sand", lambda i: re.search(r"^minecraft:(sand|red_sand|glass|glass_pane|sandstone|cut_sandstone|smooth_sandstone|chiseled_sandstone|sandstone_(slab|stairs|wall)|red_sandstone|gravel|flint)$", i)),
    ("4_basic_ores", lambda i: re.search(r"^minecraft:((iron|gold|copper|diamond|lapis|redstone|emerald)_ore|raw_(iron|gold|copper)|(iron|gold|copper)_ingot|(iron|gold)_nugget|(iron|gold|diamond|copper|lapis|redstone|emerald|raw_iron|raw_gold|raw_copper)_block|diamond|emerald|lapis_lazuli|redstone|(iron|golden|diamond)_(pickaxe|axe|shovel|hoe|sword)|bucket|shears|flint_and_steel|iron_bars|iron_door|iron_trapdoor|chain|iron_chain|cauldron)$", i)),
    ("5_food_farming", lambda i: re.search(r"^minecraft:(wheat_seeds|wheat|bread|hay_block|apple|golden_apple|sugar_cane|sugar|potato|baked_potato|beetroot|beetroot_seeds|beetroot_soup|melon_slice|melon_block|melon_seeds|pumpkin|pumpkin_seeds|pumpkin_pie|egg|cake|cookie|cocoa_beans|mushroom_stew|brown_mushroom|red_mushroom|(cooked_)?(beef|porkchop|chicken|mutton|rabbit|cod|salmon)|rabbit_stew|kelp|dried_kelp|dried_kelp_block|farmland|wooden_hoe|stone_hoe|milk_bucket|composter|bone_meal|carrots|wheat_block|potatoes|sweet_berry_bush)$", i)),
    ("6_fluids", lambda i: re.search(r"^minecraft:(water|lava|flowing_water|flowing_lava|water_bucket|lava_bucket|bucket|ice|packed_ice|snow|snowball|obsidian|cauldron)$", i)),
    ("7_stations", lambda i: re.search(r"^minecraft:(stonecutter_block|smoker|blast_furnace|campfire|soul_campfire|anvil|smithing_table|loom|cartography_table|grindstone|brewing_stand|enchanting_table|fletching_table|lectern|crafter)$", i)),
]
ANTIGRAVITY = ["minecraft:oak_slab", "minecraft:oak_stairs", "minecraft:wooden_door", "minecraft:oak_fence", "minecraft:chest"]

rows = []; K = set(known); placed = set(known); rank = 0
base_avail = avail_set(K)
for name, rule in SETS:
    cands = [i for i, e in entries.items() if i not in placed and e["kind"] in ("block", "item", "block_only") and rule(i)]
    PIN = {"1_wood": ANTIGRAVITY,   # first non-full-cube set modelled by Antigravity
           "6_fluids": ["minecraft:water", "minecraft:flowing_water", "minecraft:water_bucket", "minecraft:lava", "minecraft:flowing_lava", "minecraft:lava_bucket"]}
    pinned = [c for c in PIN.get(name, []) if c in cands]
    cands = pinned + [c for c in cands if c not in pinned]
    remaining = list(cands)
    while remaining:
        before = avail_set(K)
        if pinned: pick = pinned.pop(0); gain = avail_set(K | {pick}) - before
        else:
            scored = []
            def gain_of(extra):
                Kx = K | set(extra); pool = {r["file"]: r for x in extra for r in involve[x]}
                return {f for f, r in pool.items() if f not in before and available(r, Kx)}
            for c in remaining:
                g = gain_of([c])
                look = max((len(gain_of([c, d])) for d in remaining if d != c), default=0)   # one-step lookahead for chains (ore -> ingot)
                md = missing_deps(c, K)
                ready = 1 if (md is None or md == []) else 0          # world-sourced or craftable right now
                scored.append((len(g), look, ready, len(involve[c]), c, g))
            scored.sort(key=lambda s: (-s[0], -s[1], -s[2], -s[3], s[4]))
            pick, gain = scored[0][4], scored[0][5]
        deps = missing_deps(pick, K)
        e = entries[pick]
        tag_bind = sorted({v for r in recipes if r["file"] in gain for k, v in r["ins"] if k == "tag" and not tags.get(v, {}).get("runtimeBinding")})
        new_station = [s for s, b in STATION_BLOCK.items() if b == pick]
        K.add(pick); placed.add(pick); remaining.remove(pick); rank += 1
        rows.append({"rank": rank, "set": name, "id": pick, "kind": e["kind"], "category": e["category"], "shape": e["shape"] if not e.get("blockForm") else "item->" + e["blockForm"]["shape"], "shapeEvidence": e["shapeEvidence"] if not e.get("blockForm") else e["blockForm"]["evidence"],
                     "recipesUnlocked": len(gain), "cumulativeAvailable": len(avail_set(K)),
                     "missingDependencies": "" if deps is None else ";".join(deps), "obtainedWithoutRecipe": "yes" if deps is None else "",
                     "needsTagBinding": ";".join(tag_bind), "unlocksStation": ";".join(new_station),
                     "antigravityModel": "yes" if pick in ANTIGRAVITY else "", "graphics": "missing", "function": "missing",
                     "unlockedRecipes": ";".join(sorted(gain))[:500]})
with open(out, "w", newline="", encoding="utf-8") as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0].keys())); w.writeheader(); w.writerows(rows)
print("baseline available:", len(base_avail), sorted(base_avail)[:40])
print("rows:", len(rows), "final available:", rows[-1]["cumulativeAvailable"])
for s in [x[0] for x in SETS]:
    sub = [r for r in rows if r["set"] == s]
    print(s, len(sub), "unlocked", sum(r["recipesUnlocked"] for r in sub), [ (r["id"].split(':')[1], r["recipesUnlocked"]) for r in sub[:8]])
