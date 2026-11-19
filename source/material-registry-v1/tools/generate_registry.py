#!/usr/bin/env python3
"""JOSCRAFT material registry generator (read-only analysis; never touches the game project).

Inputs (all verified by SHA-256 and recorded in the output):
  --catalog   data/recipe-catalog.json              (JOSCRAFT normalized Mojang recipes)
  --runtime   runtime/RecipeCatalog.cs              (stable IDs 0-33 + tag bindings, parsed as text)
  --roadmap   docs/material-roadmap.csv             (existing JOSCRAFT plan, for reconciliation only)
  --samples   checkout of Mojang/bedrock-samples @ pinned commit
              (behavior_pack/recipes, behavior_pack/items, behavior_pack/blocks, metadata/vanilladata_modules)
  --community Bedrock-OSS/bedrock-wiki tags.json    (NON-authoritative cross-reference only)
  --out       output directory
Run: python3 generate_registry.py --catalog ... --runtime ... --roadmap ... --samples ... --community ... --out ...
"""
import argparse, csv, hashlib, json, os, re, collections, datetime

SAMPLES_REPO = "https://github.com/Mojang/bedrock-samples"
SAMPLES_COMMIT = "15701fa9c13923f12662984c32c124327e40e3d3"
SAMPLES_VERSION = "1.26.60.5"
LEARN_TAG_LIST = "https://learn.microsoft.com/en-us/minecraft/creator/reference/content/recipereference/examples/recipedefinitions/recipetaglist?view=minecraft-bedrock-stable"
LEARN_TAG_COMPONENT = "https://learn.microsoft.com/en-us/minecraft/creator/reference/content/itemreference/examples/itemcomponents/minecraft_tags"
COMMUNITY_REPO = "https://github.com/Bedrock-OSS/bedrock-wiki"
COMMUNITY_COMMIT = "0930b394916fad2275d600f3bf77eed8f2783d02"
COMMUNITY_FILE = "docs/public/assets/tables/items/vanilla-item-tags/tags.json"
NPM_VANILLA_DATA = "https://www.npmjs.com/package/@minecraft/vanilla-data/v/1.26.60"

# Tags listed by name on the official Microsoft Learn page (fetched 2026-11-13, page updated 2024-08-15, "current as of 1.19.50").
LEARN_TAGS = {"minecraft:is_armor","minecraft:arrow","minecraft:banner","minecraft:boats","minecraft:bookshelf_books","minecraft:chainmail_tier",
 "minecraft:chest_boat","minecraft:coals","minecraft:is_cooked","minecraft:crimson_stems","minecraft:diamond_tier","minecraft:digger","minecraft:door",
 "minecraft:is_fish","minecraft:is_food","minecraft:golden_tier","minecraft:hanging_actor","minecraft:hanging_sign","minecraft:is_axe","minecraft:is_hoe",
 "minecraft:horse_armor","minecraft:iron_tier","minecraft:leather_tier","minecraft:lectern_books","minecraft:logs","minecraft:logs_that_burn",
 "minecraft:mangrove_logs","minecraft:is_meat","minecraft:is_minecart","minecraft:music_disc","minecraft:netherite_tier","minecraft:is_pickaxe",
 "minecraft:piglin_loved","minecraft:piglin_repellents","minecraft:planks","minecraft:sand","minecraft:is_shovel","minecraft:sign",
 "minecraft:soul_fire_base_blocks","minecraft:spawn_egg","minecraft:stone_bricks","minecraft:stone_crafting_materials","minecraft:stone_tier",
 "minecraft:stone_tool_materials","minecraft:is_sword","minecraft:is_tool","minecraft:vibration_damper","minecraft:warped_stems","minecraft:wooden_slabs",
 "minecraft:wooden_tier","minecraft:wool"}

ROLES = ("keys", "ingredients", "input", "reagent", "base", "addition", "template", "outputs")
EXISTING_STATIONS = {"crafting_table", "furnace"}          # stations the current game actually runs
STATION_BLOCK = {"crafting_table": "minecraft:crafting_table", "furnace": "minecraft:furnace", "stonecutter": "minecraft:stonecutter_block",
                 "blast_furnace": "minecraft:blast_furnace", "smoker": "minecraft:smoker", "campfire": "minecraft:campfire",
                 "soul_campfire": "minecraft:soul_campfire", "brewing_stand": "minecraft:brewing_stand", "smithing_table": "minecraft:smithing_table",
                 "cartography_table": "minecraft:cartography_table"}

def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()

def load_json_lenient(path):
    t = open(path, encoding="utf-8-sig").read()
    try: return json.loads(t)
    except json.JSONDecodeError: return json.loads(re.sub(r"//[^\n]*", "", t))

# ----------------------------------------------------------------------------------------------- runtime (stable IDs)
def parse_runtime(path):
    src = open(path, encoding="utf-8").read()
    ids = {("minecraft:" + m.group(1)): int(m.group(2)) for m in re.finditer(r'\["([a-z0-9_]+)"\]=(\d+)', src)}
    # Tag bindings: verify the exact source text we rely on still exists (fails loudly if Codex changed it).
    expect = {
        "minecraft:planks": '"minecraft:planks"=>-1',
        "minecraft:coals": '"minecraft:coals"=>-2',
        "minecraft:stone_crafting_materials": '"minecraft:stone_crafting_materials" or "minecraft:stone_tool_materials"=>-3',
        "minecraft:stone_tool_materials": '"minecraft:stone_crafting_materials" or "minecraft:stone_tool_materials"=>-3',
    }
    for tag, needle in expect.items():
        if needle not in src: raise SystemExit(f"RecipeCatalog.cs changed: missing binding text for {tag}")
    if "-1=>BlockCatalog.IsPlank(type),-2=>type is 19 or 33,-3=>type==14" not in src:
        raise SystemExit("RecipeCatalog.IngredientMatches changed; update parse_runtime")
    bindings = {"minecraft:planks": {"code": -1, "acceptsStableIds": [10, 11, 12], "predicate": "BlockCatalog.IsPlank (10..12)"},
                "minecraft:coals": {"code": -2, "acceptsStableIds": [19, 33], "predicate": "type is 19 or 33"},
                "minecraft:stone_crafting_materials": {"code": -3, "acceptsStableIds": [14], "predicate": "type==14"},
                "minecraft:stone_tool_materials": {"code": -3, "acceptsStableIds": [14], "predicate": "type==14"}}
    data_rule = "data>0 => unresolved (Ingredient returns 0)" if 'TryGetProperty("data",out var data)&&data.GetInt32()>0)return 0' in src else "changed"
    return ids, bindings, data_rule

# ----------------------------------------------------------------------------------------------- references
def split_id(raw):
    """'minecraft:coal:1' -> ('minecraft:coal', 1, 'aux_suffix'); 'minecraft:potion_type:awkward' -> pseudo."""
    parts = raw.split(":")
    if len(parts) == 2: return raw, None, None
    if parts[1] == "potion_type": return raw, None, "potion_type"
    if len(parts) == 3 and re.fullmatch(r"-?\d+", parts[2]): return parts[0] + ":" + parts[1], int(parts[2]), "aux_suffix"
    return raw, None, "unknown_form"

def collect_refs(catalog):
    refs = []   # (recipe_file, recipe_kind, stations, role, key, entry)
    for r in catalog["recipes"]:
        for role in ROLES:
            v = r.get(role)
            if v is None: continue
            items = list(v.items()) if role == "keys" else ([(None, v)] if isinstance(v, dict) else [(None, x) for x in v])
            for key, e in items:
                for x in (e if isinstance(e, list) else [e]):
                    refs.append((r["file"], r["kind"], tuple(r["stations"]), role, key, x))
    return refs

# ----------------------------------------------------------------------------------------------- classification
SHAPE_BY_PROPERTY = [  # (all props must be present, shape) – evidence: official block property names (mojang-blocks.json)
    ({"liquid_depth"}, "fluid"), ({"door_hinge_bit", "upper_block_bit"}, "door"), ({"minecraft:vertical_half"}, "slab"),
    ({"upside_down_bit", "weirdo_direction"}, "stairs"), ({"in_wall_bit"}, "fence_gate"), ({"wall_post_bit"}, "wall"),
    ({"head_piece_bit"}, "bed"), ({"growth"}, "crop"), ({"button_pressed_bit"}, "button"), ({"torch_facing_direction"}, "torch"),
    ({"ground_sign_direction"}, "sign"), ({"rail_direction"}, "rail"), ({"vine_direction_bits"}, "plant"), ({"age_bit"}, "plant"),
    ({"upside_down_bit", "open_bit", "direction"}, "trapdoor"), ({"persistent_bit", "update_bit"}, "leaves"), ({"height", "covered_bit"}, "layer"),
    ({"redstone_signal"}, "pressure_plate_or_redstone"),
]
SHAPE_BY_NAME = [  # evidence: name pattern only
    (r"_fence$", "fence"), (r"(^|_)glass_pane$|_pane$", "pane"), (r"_bars$", "pane"), (r"_carpet$|^carpet$|moss_carpet", "carpet"),
    (r"(^|_)chest$", "chest"), (r"_slab$", "slab"), (r"_stairs$", "stairs"), (r"_wall$", "wall"), (r"_door$", "door"), (r"_trapdoor$", "trapdoor"),
    (r"_sign$|_hanging_sign$", "sign"), (r"_button$", "button"), (r"_pressure_plate$", "pressure_plate_or_redstone"), (r"lantern$", "lantern"),
    (r"(_sapling|_flower|_tulip|_mushroom|^poppy|^dandelion|_orchid|^allium|^azure_bluet|^oxeye_daisy|^cornflower|lily|_roots|_fungus|^fern|short_grass|_bush)$", "plant"),
    (r"^(ladder|scaffolding|vine)$", "attached"), (r"_candle$|^candle$", "candle"), (r"head$|_skull$", "head"),
]
def shape_for(name, block_props, kind):
    if kind in ("item", "pseudo", "unresolved", "tag"): return ("item" if kind == "item" else "n/a"), "kind"
    props = set(block_props or [])
    for need, shape in SHAPE_BY_PROPERTY:
        if need <= props: return shape, "block_properties"
    short = name.split(":", 1)[1]
    for pat, shape in SHAPE_BY_NAME:
        if re.search(pat, short): return shape, "name_pattern"
    if {"minecraft:connection_east", "minecraft:connection_west"} <= props: return "pane_or_fence", "block_properties"
    if props & {"minecraft:cardinal_direction", "facing_direction", "direction", "minecraft:block_face"}:
        return "full_cube_oriented", "default_assumption"
    return "full_cube", "default_assumption"

WOODS = ["oak", "spruce", "birch", "jungle", "acacia", "dark_oak", "mangrove", "cherry", "pale_oak", "bamboo", "crimson", "warped"]
CATEGORY_RULES = [  # first match wins; evidence: name pattern
    ("fluid", r"^(water|lava|flowing_water|flowing_lava|.*_bucket|bucket)$"),
    ("station", r"^(crafting_table|furnace|blast_furnace|smoker|stonecutter_block|stonecutter|anvil|smithing_table|brewing_stand|loom|cartography_table|grindstone|enchanting_table|lectern|campfire|soul_campfire|barrel|composter|fletching_table|crafter)$"),
    ("storage", r"(^|_)(chest|shulker_box|barrel|bundle|hopper)$"),
    ("tool", r"_(pickaxe|axe|shovel|hoe|sword|spear|mace)$|^(shears|flint_and_steel|fishing_rod|bow|crossbow|brush|compass|clock|spyglass|lead|name_tag|carrot_on_a_stick|warped_fungus_on_a_stick)$"),
    ("armor", r"_(helmet|chestplate|leggings|boots|horse_armor)$|^(shield|turtle_helmet|wolf_armor)$"),
    ("ore_metal", r"(_ore$|^raw_|_ingot$|_nugget$|^(iron|gold|copper|diamond|emerald|lapis|redstone|netherite|coal|amethyst)_block$|^ancient_debris$|^(diamond|emerald|lapis_lazuli|quartz|amethyst_shard|netherite_scrap)$|copper)"),
    ("food_farming", r"(wheat|seeds|bread|apple|carrot|potato|beetroot|melon|pumpkin|sugar|cookie|cake|pie|stew|soup|_beef|_porkchop|_chicken|_mutton|_rabbit|_cod|_salmon|beef$|porkchop$|chicken$|mutton$|rabbit$|cod$|salmon$|berries|kelp|honey|egg$|milk|cocoa|hay_block|dried_kelp|bowl$)"),
    ("wood", r"(" + "|".join(WOODS) + r")_|(^|_)(planks|log|wood|stem|hyphae|stick|wooden_|ladder|bowl|boat|raft)|^stick$|^(fence_gate|trapdoor|wooden_door|wooden_button|wooden_pressure_plate)$"),
    ("glass_sand", r"(glass|sand(stone)?|^sand$|red_sand|tinted)"),
    ("stone_building", r"(stone|cobble|brick|deepslate|andesite|diorite|granite|tuff|basalt|blackstone|calcite|dripstone|prismarine|purpur|quartz|terracotta|concrete|clay|mud|end_stone|obsidian|smooth_)"),
    ("color_decor", r"(dye|wool|carpet|banner|bed$|candle|_glazed_|paint|flower_pot|item_frame|armor_stand)"),
    ("redstone", r"(redstone|repeater|comparator|piston|observer|dispenser|dropper|lever|rail|target|daylight|tripwire|tnt|note_block|jukebox|minecart|lightning_rod|sculk)"),
    ("brewing_magic", r"(potion|brewing|blaze|ghast|nether_wart|fermented|glistering|enchant|book|ender|experience|beacon|conduit|respawn|lodestone|totem)"),
]
def category_for(name):
    short = name.split(":", 1)[1]
    for cat, pat in CATEGORY_RULES:
        if re.search(pat, short): return cat
    return "misc"

# ----------------------------------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    for a in ("catalog", "runtime", "roadmap", "samples", "community", "out", "importinfo"): ap.add_argument("--" + a, required=a != "importinfo")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    catalog = json.load(open(a.catalog, encoding="utf-8"))
    stable, bindings, data_rule = parse_runtime(a.runtime)
    stable_by_num = {v: k for k, v in stable.items()}
    vd = os.path.join(a.samples, "metadata", "vanilladata_modules")
    items = {x["name"]: x for x in json.load(open(os.path.join(vd, "mojang-items.json")))["data_items"]}
    blocks = {x["name"]: x for x in json.load(open(os.path.join(vd, "mojang-blocks.json")))["data_items"]}
    community = json.load(open(a.community, encoding="utf-8"))
    comm_members = {}
    for row in community["rows"]:
        tag = row["tag"].strip("`")
        comm_members[tag] = sorted({m.strip("`") for m in row.get("vanilla_usage", []) if isinstance(m, str)})

    # Official per-item tag evidence: minecraft:tags components in vanilla behavior_pack items/blocks at the pinned commit.
    official_item_tags = collections.defaultdict(list)
    for sub in ("items", "blocks"):
        d = os.path.join(a.samples, "behavior_pack", sub)
        for f in sorted(os.listdir(d)):
            j = load_json_lenient(os.path.join(d, f))
            body = j.get("minecraft:item") or j.get("minecraft:block") or {}
            ident = body.get("description", {}).get("identifier")
            comp = body.get("components", {})
            t = comp.get("minecraft:tags")
            tags = t.get("tags") if isinstance(t, dict) else t if isinstance(t, list) else []
            for tag in tags or []:
                official_item_tags[tag].append({"member": ident, "evidence": f"{SAMPLES_REPO}/blob/{SAMPLES_COMMIT}/behavior_pack/{sub}/{f}"})

    refs = collect_refs(catalog)
    # ---- fidelity: catalog vs official source recipes (metadata the normalization dropped) ----
    src_dir = os.path.join(a.samples, "behavior_pack", "recipes")
    dropped = collections.Counter(); dropped_examples = collections.defaultdict(list); string_aux = collections.Counter()
    catalog_by_file = {r["file"]: r for r in catalog["recipes"]}
    missing_files = sorted(set(os.listdir(src_dir)) ^ set(catalog_by_file))
    for f in sorted(os.listdir(src_dir)):
        j = load_json_lenient(os.path.join(src_dir, f))
        body_key = next(k for k in j if k.startswith("minecraft:recipe_"))
        body = j[body_key]
        for k in ("unlock", "group"):
            if k in body:
                dropped[k] += 1
                if len(dropped_examples[k]) < 5: dropped_examples[k].append({"file": f, k: body[k]})
        if "priority" in body and "priority" not in catalog_by_file.get(f, {}):
            dropped["priority"] += 1
        if "format_version" in j: dropped["format_version"] += 1
        for m in re.finditer(r'"(minecraft:[a-z0-9_]+:-?\d+)"', json.dumps(body)): string_aux[m.group(1)] += 1

    # ---- aggregate references ----
    agg = {}
    tag_use = collections.defaultdict(lambda: {"recipes": set(), "roles": collections.Counter(), "outputs": set()})
    out_by_recipe = {r["file"]: [o["id"] for o in r.get("outputs", [])] for r in catalog["recipes"]}
    for file, kind, stations, role, key, e in refs:
        rid = e["id"]
        if e.get("tag"):
            u = tag_use["minecraft:" + rid.split(":", 1)[1] if ":" in rid else rid]
            u["recipes"].add(file); u["roles"][role] += 1; u["outputs"].update(out_by_recipe[file]); continue
        base, aux, form = split_id(rid)
        data = e.get("data")
        if aux is not None: data = aux
        ent = agg.setdefault(base, {"refs": 0, "asInput": set(), "asOutput": set(), "variants": collections.defaultdict(lambda: {"count": 0, "recipes": set(), "forms": set()}), "forms": set()})
        ent["refs"] += 1
        (ent["asOutput"] if role == "outputs" else ent["asInput"]).add(file)
        if form: ent["forms"].add(form)
        if data is not None:
            v = ent["variants"][data]; v["count"] += 1; v["recipes"].add(file); v["forms"].add(form or "data_field")

    # ---- recipe dependency index (who needs what) ----
    recipe_inputs = {}
    for r in catalog["recipes"]:
        ins = set()
        for role in ROLES[:-1]:
            v = r.get(role)
            if v is None: continue
            vals = list(v.values()) if role == "keys" else ([v] if isinstance(v, dict) else v)
            for e in vals:
                for x in (e if isinstance(e, list) else [e]):
                    if x.get("tag"): ins.add("#" + x["id"])
                    else: ins.add(split_id(x["id"])[0])
        recipe_inputs[r["file"]] = ins

    entries = []; unresolved = []
    for name in sorted(agg):
        ent = agg[name]; it = items.get(name); bl = blocks.get(name)
        _, _, form = split_id(name)
        if form == "potion_type": kind, reason = "pseudo", "potion_type selector (brewing); not an item/block id"
        elif it and bl and (it["raw_id"] < 256): kind, reason = "block", "item raw_id<256 or negative and same id in block registry (block item)"
        elif it and bl: kind, reason = "item", "item raw_id>=256; a block with the same id exists (placement behavior not proven)"
        elif it: kind, reason = "item", "item registry only"
        elif bl: kind, reason = "block_only", "block registry only; not obtainable as an item under this id"
        else: kind, reason = "unresolved", "id not present in mojang-items.json or mojang-blocks.json at the pinned commit (legacy alias or aux form)"
        props = [p["name"] for p in bl["properties"]] if bl else None
        shape, shape_src = shape_for(name, props, kind)
        stable_id = stable.get(name)
        e = {
            "id": name, "kind": kind, "kindEvidence": reason, "category": category_for(name), "categoryEvidence": "name_pattern",
            "shape": shape, "shapeEvidence": shape_src,
            "registry": {
                "item": None if not it else {"rawId": it["raw_id"], "serializationId": it["serialization_id"]},
                "block": None if not bl else {"rawId": bl["raw_id"], "serializationId": bl["serialization_id"], "properties": props},
            },
            "recipes": {"asInput": len(ent["asInput"]), "asOutput": len(ent["asOutput"]), "outputFiles": sorted(ent["asOutput"])},
            "dataVariants": [{"data": d, "refs": v["count"], "forms": sorted(v["forms"]), "recipes": sorted(v["recipes"]), "resolution": "unknown"}
                              for d, v in sorted(ent["variants"].items(), key=lambda kv: kv[0])],
            "joscraft": {"stableId": stable_id, "state": "implemented" if stable_id is not None else "missing",
                         "graphics": "own_v1" if stable_id is not None else "missing", "function": "implemented" if stable_id is not None else "missing"},
        }
        # Dependencies: inputs of every recipe producing this material (union per recipe, cheapest listed first later).
        deps = []
        for f in sorted(ent["asOutput"]):
            deps.append({"recipe": f, "inputs": sorted(recipe_inputs[f]), "stations": list(catalog_by_file[f]["stations"])})
        e["craftedFrom"] = deps
        if kind == "item" and bl:   # item that shares its id with a block (door, bed, wheat …): placement is NOT asserted
            bshape, bsrc = shape_for(name, props, "block")
            e["blockForm"] = {"shape": bshape, "evidence": bsrc, "placesBlock": "unknown"}
        if kind in ("unresolved", "pseudo") or any(v["data"] not in (0,) for v in e["dataVariants"]):
            unresolved.append({"id": name, "kind": kind, "reason": reason, "refs": ent["refs"], "dataVariants": [v["data"] for v in e["dataVariants"]]})
        entries.append(e)

    # Stable IDs that never appear in recipes (e.g. grass_block) still get an entry.
    for sid, num in sorted(stable.items(), key=lambda kv: kv[1]):
        if sid not in agg:
            it = items.get(sid); bl = blocks.get(sid); props = [p["name"] for p in bl["properties"]] if bl else None
            kind = "block" if it and bl and it["raw_id"] < 256 else "item" if it else "block_only" if bl else "unresolved"
            shape, src = shape_for(sid, props, kind)
            entries.append({"id": sid, "kind": kind, "kindEvidence": "stable id without recipe reference", "category": category_for(sid), "categoryEvidence": "name_pattern",
                            "shape": shape, "shapeEvidence": src,
                            "registry": {"item": None if not it else {"rawId": it["raw_id"], "serializationId": it["serialization_id"]},
                                         "block": None if not bl else {"rawId": bl["raw_id"], "serializationId": bl["serialization_id"], "properties": props}},
                            "recipes": {"asInput": 0, "asOutput": 0, "outputFiles": []}, "dataVariants": [], "craftedFrom": [],
                            "joscraft": {"stableId": num, "state": "implemented", "graphics": "own_v1", "function": "implemented"}})
    # Supplement: materials needed by the planned sets that no recipe references (fluids, farmland, crop blocks).
    SUPPLEMENT = ["minecraft:water", "minecraft:flowing_water", "minecraft:lava", "minecraft:flowing_lava", "minecraft:water_bucket", "minecraft:lava_bucket",
                  "minecraft:farmland", "minecraft:wheat_seeds", "minecraft:carrots", "minecraft:potatoes", "minecraft:beetroot", "minecraft:sweet_berry_bush"]
    have = {e["id"] for e in entries}
    for sid in SUPPLEMENT:
        if sid in have: continue
        it = items.get(sid); bl = blocks.get(sid)
        if not it and not bl: continue
        props = [p["name"] for p in bl["properties"]] if bl else None
        kind = "block" if it and bl and it["raw_id"] < 256 else "item" if it else "block_only"
        shape, src = shape_for(sid, props, kind)
        ent = {"id": sid, "kind": kind, "kindEvidence": "official registry supplement (not referenced by any recipe)", "category": category_for(sid), "categoryEvidence": "name_pattern",
               "shape": shape, "shapeEvidence": src,
               "registry": {"item": None if not it else {"rawId": it["raw_id"], "serializationId": it["serialization_id"]},
                            "block": None if not bl else {"rawId": bl["raw_id"], "serializationId": bl["serialization_id"], "properties": props}},
               "recipes": {"asInput": 0, "asOutput": 0, "outputFiles": []}, "dataVariants": [], "craftedFrom": [], "supplement": True,
               "joscraft": {"stableId": None, "state": "missing", "graphics": "missing", "function": "missing"}}
        if kind == "item" and bl:
            bshape, bsrc = shape_for(sid, props, "block"); ent["blockForm"] = {"shape": bshape, "evidence": bsrc, "placesBlock": "unknown"}
        entries.append(ent)
    entries.sort(key=lambda e: e["id"])

    # ---- tags ----
    tags = []
    for tag in sorted(tag_use):
        u = tag_use[tag]
        official = official_item_tags.get(tag, [])
        tags.append({
            "id": tag, "kind": "tag", "recipes": len(u["recipes"]), "recipeFiles": sorted(u["recipes"]),
            "outputsOfRecipes": sorted(u["outputs"]),
            "listedOnMicrosoftLearn": tag in LEARN_TAGS,
            "officialMembers": sorted({o["member"] for o in official}) if official else "unknown",
            "officialMemberEvidence": official or [],
            "officialMembershipNote": "Mojang/Microsoft publish the tag name (Learn) but no complete membership list for Bedrock; vanilla behavior_pack JSON only tags a few data-driven items." if not official else "Only the data-driven vanilla JSON members are proven; the full list remains unknown.",
            "communityCandidates": comm_members.get(tag, "not_listed"),
            "communityAuthoritative": False,
            "runtimeBinding": bindings.get(tag),
        })

    # ---- stable-id audit ----
    stable_audit = []
    for sid, num in sorted(stable.items(), key=lambda kv: kv[1]):
        e = next(x for x in entries if x["id"] == sid)
        stable_audit.append({"stableId": num, "id": sid, "kind": e["kind"], "inItemRegistry": e["registry"]["item"] is not None, "inBlockRegistry": e["registry"]["block"] is not None})

    # ---- tag omission audit (the stone_tool_materials class of bug) ----
    impl = set(stable)
    tag_audit = []
    for t in tags:
        tag = t["id"]; b = bindings.get(tag)
        recipes_needing = [f for f in t["recipeFiles"] if out_by_recipe[f] and all(o in impl for o in out_by_recipe[f])]
        cand = t["communityCandidates"] if isinstance(t["communityCandidates"], list) else []
        impl_cand = sorted(c for c in cand if c in impl)
        accepted = sorted(stable_by_num[n] for n in (b["acceptsStableIds"] if b else []))
        issues = []
        if recipes_needing and not b:
            other = [f for f in recipes_needing
                     if set(catalog_by_file[f]["stations"]) & EXISTING_STATIONS
                     and all(i == "#" + tag or (i.startswith("#") and i[1:] in bindings) or (not i.startswith("#") and i in impl) for i in recipe_inputs[f])]
            issues.append({"severity": "error" if other else "info", "code": "UNBOUND_TAG_FOR_IMPLEMENTED_OUTPUT",
                           "detail": f"{len(recipes_needing)} recipe(s) produce implemented materials but the tag has no runtime binding",
                           "recipesOtherwiseSatisfiable": other})
        if b:
            missing = sorted(set(impl_cand) - set(accepted))
            extra = sorted(set(accepted) - set(impl_cand))
            if missing: issues.append({"severity": "warning", "code": "IMPLEMENTED_CANDIDATE_NOT_ACCEPTED", "members": missing})
            if extra and isinstance(t["communityCandidates"], list): issues.append({"severity": "warning", "code": "ACCEPTED_BUT_NOT_A_KNOWN_CANDIDATE", "members": extra})
        tag_audit.append({"tag": tag, "binding": b["predicate"] if b else None, "acceptedIds": accepted, "implementedCandidates": impl_cand,
                          "recipesWithImplementedOutputs": recipes_needing, "issues": issues})

    # ---- recipe family gaps: registry items whose sibling species have a recipe but they do not ----
    produced = {split_id(o["id"])[0] for r in catalog["recipes"] for o in r.get("outputs", []) if not o.get("data")}
    species = WOODS + ["poplar"]
    oak_alias = {"button": "wooden_button", "pressure_plate": "wooden_pressure_plate", "trapdoor": "trapdoor", "door": "wooden_door", "fence_gate": "fence_gate"}
    gaps = []
    for fam in ["button", "pressure_plate", "trapdoor", "door", "fence_gate", "fence", "slab", "stairs", "sign", "hanging_sign", "boat", "chest_boat", "shelf", "planks"]:
        members_ = []
        for sp in species:
            ident = "minecraft:" + (oak_alias[fam] if sp == "oak" and fam in oak_alias else f"{sp}_{fam}")
            if ident in items or ident in blocks: members_.append(ident)
        have = [m for m in members_ if m in produced]; lack = [m for m in members_ if m not in produced]
        if have and lack:
            gaps.append({"family": fam, "withRecipe": have, "registryIdWithoutRecipeInSamples": lack,
                         "note": "Not invented here: either produced via a legacy/aux id (see dataVariants of minecraft:boat, minecraft:sign …) or hardcoded in the engine outside the sample JSON."})

    # ---- roadmap reconciliation ----
    roadmap = list(csv.DictReader(open(a.roadmap, encoding="utf-8-sig")))
    road_ids = {r["Minecraft ID or tag"] for r in roadmap}
    reg_ids = {e["id"] for e in entries} | {"#" + t["id"] for t in tags}
    recon = {"roadmapRows": len(roadmap), "registryIdsAndTags": len(reg_ids),
             "inRoadmapNotInRegistry": sorted(road_ids - reg_ids)[:400], "inRegistryNotInRoadmap": sorted(reg_ids - road_ids)[:400],
             "note": "Roadmap keys keep aux suffixes (e.g. minecraft:coal:1) as separate ids; the registry folds them into dataVariants of the base id."}

    # ---- outputs ----
    sources = {
        "catalog": {"path": "data/recipe-catalog.json", "sha256": sha(a.catalog), "source": catalog["source"]},
        "runtime": {"path": "runtime/RecipeCatalog.cs", "sha256": sha(a.runtime)},
        "roadmap": {"path": "docs/material-roadmap.csv", "sha256": sha(a.roadmap)},
        "officialRegistries": {"repository": SAMPLES_REPO, "commit": SAMPLES_COMMIT, "version": SAMPLES_VERSION,
                               "items": {"path": "metadata/vanilladata_modules/mojang-items.json", "sha256": sha(os.path.join(vd, "mojang-items.json")), "count": len(items),
                                         "url": f"{SAMPLES_REPO}/blob/{SAMPLES_COMMIT}/metadata/vanilladata_modules/mojang-items.json"},
                               "blocks": {"path": "metadata/vanilladata_modules/mojang-blocks.json", "sha256": sha(os.path.join(vd, "mojang-blocks.json")), "count": len(blocks),
                                          "url": f"{SAMPLES_REPO}/blob/{SAMPLES_COMMIT}/metadata/vanilladata_modules/mojang-blocks.json"},
                               "recipesDir": f"{SAMPLES_REPO}/tree/{SAMPLES_COMMIT}/behavior_pack/recipes"},
        "officialTagNames": {"url": LEARN_TAG_LIST, "pageUpdated": "2024-08-15", "statedCurrency": "Bedrock 1.19.50", "fetched": "2026-11-13", "membersListed": False},
        "officialTagComponent": {"url": LEARN_TAG_COMPONENT, "pageUpdated": "2026-08-25", "fetched": "2026-11-13"},
        "officialNpmVanillaData": {"url": NPM_VANILLA_DATA, "version": "1.26.60", "integrity": "sha512-6ARh4/fbNRA4XRRrMO3ys0ldO208ipcpyLAf4tpc3KXw/E6EUeaFKuFqJ2ESMQ6sSUUIfK1dNXm9MtFdWj9BPg==",
                                   "finding": "Contains item/block type ids only; no tag membership."},
        "communityTags": {"repository": COMMUNITY_REPO, "commit": COMMUNITY_COMMIT, "file": COMMUNITY_FILE, "sha256": sha(a.community),
                          "statedCurrency": "Preview 1.26.70.21", "authoritative": False,
                          "use": "candidate list for warnings/planning only; never used to mark a recipe available"},
    }
    registry = {
        "schema": "registry-schema.json", "registryVersion": 1, "generated": datetime.date.today().isoformat(),
        "sources": sources,
        "classificationRules": {
            "block": "id in mojang-items.json with raw_id < 256 (incl. negative) AND in mojang-blocks.json",
            "item": "id in mojang-items.json otherwise (sameNameBlock may exist; placement is not inferred)",
            "block_only": "id only in mojang-blocks.json",
            "pseudo": "minecraft:potion_type:* selectors",
            "unresolved": "id in neither official registry (legacy alias / aux string); metadata kept in dataVariants",
            "shape": "official block property names first, then name pattern, else default_assumption (explicitly low confidence)",
        },
        "runtime": {"stableIds": {k: v for k, v in sorted(stable.items(), key=lambda kv: kv[1])}, "tagBindings": bindings, "dataRule": data_rule},
        "entries": entries, "tags": tags,
    }
    json.dump(registry, open(os.path.join(a.out, "material-registry.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    kinds = collections.Counter(e["kind"] for e in entries)
    audit = {
        "generated": registry["generated"], "sources": {k: v.get("sha256") if isinstance(v, dict) else v for k, v in sources.items()},
        "counts": {
            "recipes": len(catalog["recipes"]), "recipeKinds": dict(collections.Counter(r["kind"] for r in catalog["recipes"])),
            "references": len(refs), "tagReferences": sum(1 for r in refs if r[5].get("tag")),
            "uniqueMaterialIds": len(entries), "uniqueTags": len(tags), "byKind": dict(kinds),
            "byCategory": dict(collections.Counter(e["category"] for e in entries)),
            "byShape": dict(collections.Counter(e["shape"] for e in entries)),
            "shapeEvidence": dict(collections.Counter(e["shapeEvidence"] for e in entries)),
            "withDataVariants": sum(1 for e in entries if e["dataVariants"]),
            "dataVariantRefs": sum(v["refs"] for e in entries for v in e["dataVariants"]),
            "implementedStableIds": len(stable),
            "stations": dict(collections.Counter(s for r in catalog["recipes"] for s in r["stations"])),
        },
        "coverage": {
            "materialsImplemented": sum(1 for e in entries if e["joscraft"]["stableId"] is not None),
            "materialsMissing": sum(1 for e in entries if e["joscraft"]["stableId"] is None and e["kind"] in ("block", "item", "block_only")),
            "tagsWithRuntimeBinding": sum(1 for t in tags if t["runtimeBinding"]),
            "tagsWithOfficialMembership": sum(1 for t in tags if t["officialMembers"] != "unknown"),
            "tagsListedOnLearn": sum(1 for t in tags if t["listedOnMicrosoftLearn"]),
        },
        "stableIdAudit": stable_audit,
        "tagAudit": tag_audit,
        "unresolvedReferences": sorted(unresolved, key=lambda u: (-u["refs"], u["id"])),
        "catalogFidelity": {
            "sourceFiles": len(os.listdir(src_dir)), "catalogRecipes": len(catalog_by_file), "fileSetDifference": missing_files,
            "droppedFields": dict(dropped), "droppedExamples": dict(dropped_examples),
            "auxStringIdsInSource": dict(string_aux.most_common()),
            "note": "Normalization keeps ids, tags, counts, data, patterns, outputs, stations and priority; it drops unlock conditions, group and format_version.",
        },
        "recipeFamilyGaps": gaps,
        "roadmapReconciliation": recon,
    }
    json.dump(audit, open(os.path.join(a.out, "material-audit.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(json.dumps(audit["counts"], indent=1)); print(json.dumps(audit["coverage"], indent=1))
    print("tag issues:", [(t["tag"], t["issues"]) for t in tag_audit if t["issues"]])

if __name__ == "__main__":
    main()
