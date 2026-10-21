#!/usr/bin/env python3
"""METINAR-017 (#40) — generator tabel balansu (PROPOZYCJA, nie finalny balans).

Odtwarza pliki CSV w tym katalogu (exp_curve, mob_level_stats, sanity_warrior_ttk, skill_tiers,
skill_tier_items) z formuł opisanych w docs/design/*.md.
Uruchomienie: python3 docs/design/tables/generate_tables.py
Bez zależności zewnętrznych. Każda liczba to proponowana wartość startowa do balansowania.

Maks. poziom 99 (decyzja właściciela 2026-10-21, wcześniej 60). Formuły bez zmian, więc poziomy 1–60 mają
identyczne wartości jak przy limicie 60; poziomy 60–99 to długa końcówka gry (patrz CHARACTER_PROGRESSION.md §2).
"""
import csv
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
MAX_LEVEL = 99  # decyzja właściciela 2026-10-21 (wcześniej 60)
KILL_EQUIVALENTS_PER_HOUR = 25  # założenie tempa: walki + zadania + spacer (CHARACTER_PROGRESSION.md §2)


def round_to(x, step):
    return int(step * round(x / step))


# --- Moby (CHARACTER_PROGRESSION.md §3, MOBS_NPC.md §3) ---
def mob_exp(m):
    return round(20 + 10 * m ** 1.15)


def mob_hp(m):  # kalibracja: Trujący Pająk poz. 5 = 120 HP (wartość prototypu właściciela)
    return round_to(30 + 11 * m ** 1.3, 5)


def mob_atk(m):
    return round(6 + 3.0 * m ** 1.1)


def mob_def(m):
    return round(4 + 2.2 * m)


def mob_gold_avg(m):
    return round(4 + 2.0 * m)


# --- Krzywa EXP (CHARACTER_PROGRESSION.md §2) ---
def kill_equivalents(level):
    return 4 + 1.2 * level


def exp_to_next(level):
    return round_to(mob_exp(level) * kill_equivalents(level), 10)


# --- Gracz (CHARACTER_PROGRESSION.md §1) ---
CLASSES = {
    # id: (hpPerLevel, mpPerLevel, start Might, Finesse, Spirit, Vigor, auto-growth per level)
    "warrior": (14, 4, 6, 3, 2, 5, {"might": 1, "vigor": 1}),
    "ninja": (9, 5, 3, 6, 2, 4, {"finesse": 1, "vigor": 1}),
    "shaman": (8, 9, 2, 3, 6, 4, {"spirit": 1, "vigor": 1}),
    "slajter": (11, 7, 5, 2, 5, 4, {"might": 1, "spirit": 1}),
}


def player_hp(cls, level, vigor):
    return 60 + level * CLASSES[cls][0] + vigor * 6


def player_mp(cls, level, spirit):
    return 20 + level * CLASSES[cls][1] + spirit * 5


def weapon_atk(item_level):
    return 4 + 1.6 * item_level


# --- Stopnie umiejętności (CLASSES_SKILLS.md §2.1; decyzja właściciela 2026-10-21 10:13) ---
# Mechanika stopni jak w gatunku, ale nazwy, liczby i opisy są własne MetinAR.
# rankIndex 1–29: Wprawa 1–14 (punkty), Biegłość I (15. punkt = automatyczny awans), Biegłość II–VIII (Traktat),
# Kunszt I–VI (Iskra), Szczyt (Pieczęć).
ROMAN = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII"]
BASIC_POINT_RANKS = 15          # punkty umiejętności na umiejętność (Wprawa 1–14 + awans do Biegłości I)
TREATISE_CHANCES = [70, 65, 60, 55, 50, 45, 40]       # Biegłość I→II … VII→VIII
SPARK_CHANCES = [35, 30, 26, 22, 18, 15]              # Biegłość VIII→Kunszt I … Kunszt V→VI
SPARK_MIN_LEVEL = [60, 66, 72, 78, 84, 90]
SEAL_CHANCE, SEAL_MIN_LEVEL = 12, 95
TREATISE_COOLDOWN_H, SPARK_COOLDOWN_H, SEAL_COOLDOWN_H = 6, 24, 72
TREATISE_EXP_PCT, SPARK_EXP_PCT, SEAL_EXP_PCT = 3, 6, 10   # % EXP do następnego poziomu postaci za próbę
TREATISE_MIN_LEVEL = 20


def skill_ranks():
    """Lista stopni: (rankIndex, tierId, tierPl, tierRank, label, raisedBy, chance%, cooldownH, expPct, minLevel)."""
    out = []
    for r in range(1, 15):
        out.append(("practice", "Wprawa", r, "Wprawa %d" % r, "skill_point", 100, 0, 0, 1))
    out.append(("proficiency", "Biegłość", 1, "Biegłość I", "skill_point_auto_promotion", 100, 0, 0, 1))
    for k in range(2, 9):
        out.append(("proficiency", "Biegłość", k, "Biegłość " + ROMAN[k - 1], "treatise_proficiency_<classId>",
                    TREATISE_CHANCES[k - 2], TREATISE_COOLDOWN_H, TREATISE_EXP_PCT, TREATISE_MIN_LEVEL))
    for k in range(1, 7):
        out.append(("artistry", "Kunszt", k, "Kunszt " + ROMAN[k - 1], "spark_artistry",
                    SPARK_CHANCES[k - 1], SPARK_COOLDOWN_H, SPARK_EXP_PCT, SPARK_MIN_LEVEL[k - 1]))
    out.append(("apex", "Szczyt", 1, "Szczyt", "seal_apex", SEAL_CHANCE, SEAL_COOLDOWN_H, SEAL_EXP_PCT, SEAL_MIN_LEVEL))
    return [(i + 1,) + row for i, row in enumerate(out)]


def skill_coef_mult(idx):
    if idx <= 14:                       # Wprawa
        return 1 + 0.06 * (idx - 1)     # 1,00 … 1,78
    if idx <= 22:                       # Biegłość I–VIII
        return 1.84 + 0.07 * (idx - 15)  # 1,84 … 2,33
    if idx <= 28:                       # Kunszt I–VI
        return 2.45 + 0.10 * (idx - 23)  # 2,45 … 2,95
    return 3.30                         # Szczyt


def skill_mp_mult(idx):
    return 1 + 0.04 * (idx - 1)


def skill_cooldown_mult(idx):
    return max(0.65, 1 - 0.0125 * (idx - 1))


def skill_duration_mult(idx):
    return 1 + 0.025 * (idx - 1)


# Założony stopień pierwszej umiejętności wojownika (Ciężkie Cięcie) wg poziomu — tylko do kontroli TTK.
EXPECTED_HEAVY_SLASH_RANK = {1: 1, 5: 5, 10: 10, 20: 15, 30: 17, 40: 19, 50: 21, 60: 23, 70: 25, 80: 27, 90: 28, 99: 29}
HEAVY_SLASH_COEF = 1.6

SKILL_TIER_ITEMS = [
    # itemId, namePl, tier, stack, sources, normalDropPct, eliteDropPct, guardianDropPct, minMobLevel, shopPrice, notes
    ("treatise_proficiency_<classId>", "Traktat Biegłości (wersja dla klasy: Wojownik/Ninja/Szaman/Slajter)", "proficiency", 20,
     "drop|quest_daily|shop_trainer", 0.8, 4.0, 25.0, 20, "round5(60*L) min 1200",
     "4 warianty (po jednym na klasę); zadanie dzienne od poz. 20: 15% szansy na 1 sztukę"),
    ("spark_artistry", "Iskra Kunsztu", "artistry", 10,
     "drop|quest_story", 0.0, 1.5, 15.0, 55, "",
     "wspólna dla klas; zadania fabularne od poz. 60: 1 sztuka za zadanie; nie do kupienia"),
    ("seal_apex", "Pieczęć Szczytu", "apex", 5,
     "drop|quest_story", 0.0, 0.0, 2.0, 90, "",
     "wspólna dla klas; finał fabuły ścieżki (poz. 95): 1 sztuka na ścieżkę; nie do kupienia"),
]


def write_skill_tables():
    rows = []
    for (idx, tier, tierPl, tierRank, label, raised, chance, cd, exp_pct, min_lvl) in skill_ranks():
        expected_attempts = round(100 / chance, 2)
        rows.append([idx, tier, tierPl, tierRank, label, raised,
                     1 if raised.startswith("skill_point") else 0, chance, cd, exp_pct, min_lvl,
                     expected_attempts, round(skill_coef_mult(idx), 2), round(skill_mp_mult(idx), 2),
                     round(skill_cooldown_mult(idx), 4), round(skill_duration_mult(idx), 3)])
    write("skill_tiers.csv",
          ["rankIndex", "tierId", "tierPl", "tierRank", "labelPl", "raisedBy", "skillPointCost",
           "successChancePct", "attemptCooldownHours", "expCostPctOfExpToNext", "minCharacterLevel",
           "expectedAttempts", "coefMult", "mpCostMult", "cooldownMult", "durationMult"], rows)

    write("skill_tier_items.csv",
          ["itemId", "namePl", "tierId", "stack", "sources", "normalMobDropPct", "eliteDropPct", "guardianDropPct",
           "minMobLevel", "shopPrice", "notes"], [list(r) for r in SKILL_TIER_ITEMS])

    ranks = skill_ranks()
    for item, cd in (("treatise_proficiency_<classId>", TREATISE_COOLDOWN_H), ("spark_artistry", SPARK_COOLDOWN_H),
                     ("seal_apex", SEAL_COOLDOWN_H)):
        att = sum(100 / r[6] for r in ranks if r[5] == item)
        print("%s: ~%.1f sztuk na umiejętność (średnio), minimum %d prób, ~%.0f dni przy próbie co %d h" % (
            item, att, sum(1 for r in ranks if r[5] == item), att * cd / 24, cd))


def write(name, header, rows):
    with open(os.path.join(HERE, name), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(header)
        w.writerows(rows)


def main():
    rows = []
    cumulative = 0
    for lvl in range(1, MAX_LEVEL + 1):
        nxt = exp_to_next(lvl) if lvl < MAX_LEVEL else 0
        rows.append([lvl, nxt, cumulative, round(kill_equivalents(lvl), 1) if lvl < MAX_LEVEL else 0,
                     mob_exp(lvl), nxt // 4 if nxt else 0])
        cumulative += nxt
    write("exp_curve.csv",
          ["level", "expToNext", "cumulativeExpAtLevelStart", "sameLevelKillEquivalents", "sameLevelMobExp", "expPerHudOrb"],
          rows)

    # Podsumowanie tempa (tylko wydruk, do dokumentacji): ekwiwalenty zabić i godziny do progów poziomów.
    ke = 0.0
    for lvl in range(1, MAX_LEVEL):
        ke += kill_equivalents(lvl)
        if lvl + 1 in (10, 20, 30, 40, 50, 60, 70, 80, 90, 99):
            print("poz. %d: suma EXP %d, %.0f ekw. zabić, ~%.1f h" % (
                lvl + 1, sum(exp_to_next(x) for x in range(1, lvl + 1)), ke, ke / KILL_EQUIVALENTS_PER_HOUR))

    rows = []
    for m in range(1, MAX_LEVEL + 1):
        rows.append([m, mob_hp(m), mob_atk(m), mob_def(m), mob_exp(m), mob_gold_avg(m),
                     max(1, round(mob_gold_avg(m) * 0.7)), round(mob_gold_avg(m) * 1.3)])
    write("mob_level_stats.csv",
          ["mobLevel", "normalHp", "normalAtk", "normalDef", "normalExp", "goldAvg", "goldMin", "goldMax"], rows)

    # Przykładowa postać wojownika, który wkłada 2/3 wolnych punktów w Moc (sanity check TTK).
    rows = []
    for lvl in (1, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 99):
        free = 2 * (lvl - 1)
        might = 6 + (lvl - 1) + round(free * 2 / 3)
        vigor = 5 + (lvl - 1) + (free - round(free * 2 / 3))
        rarity_refine = 1.0 if lvl < 10 else (1.12 if lvl < 30 else 1.30)
        atk = weapon_atk(lvl) * rarity_refine + might * 0.8 + lvl * 0.5
        mdef = mob_def(lvl)
        mitig = mdef / (mdef + 50 + 8 * lvl)
        hit = atk * (1 - mitig)
        hits = mob_hp(lvl) / hit
        rk = EXPECTED_HEAVY_SLASH_RANK[lvl]
        label = skill_ranks()[rk - 1][4]
        skill_hit = hit * HEAVY_SLASH_COEF * skill_coef_mult(rk)
        rows.append([lvl, might, vigor, round(atk, 1), round(mitig, 3), round(hit, 1), mob_hp(lvl), round(hits, 1),
                     player_hp("warrior", lvl, vigor), label, round(skill_coef_mult(rk), 2),
                     round(skill_hit, 1), round(mob_hp(lvl) / skill_hit, 1)])
    write("sanity_warrior_ttk.csv",
          ["level", "might", "vigor", "atk", "mobMitigation", "avgHitNoCrit", "mobHp", "hitsToKill", "playerMaxHp",
           "assumedHeavySlashRank", "heavySlashCoefMult", "heavySlashHit", "heavySlashOnlyHitsToKill"], rows)

    write_skill_tables()


if __name__ == "__main__":
    main()
