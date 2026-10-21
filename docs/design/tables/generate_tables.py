#!/usr/bin/env python3
"""METINAR-017 (#40) — generator tabel balansu (PROPOZYCJA, nie finalny balans).

Odtwarza pliki CSV w tym katalogu z formuł opisanych w docs/design/*.md.
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
        rows.append([lvl, might, vigor, round(atk, 1), round(mitig, 3), round(hit, 1), mob_hp(lvl), round(hits, 1),
                     player_hp("warrior", lvl, vigor)])
    write("sanity_warrior_ttk.csv",
          ["level", "might", "vigor", "atk", "mobMitigation", "avgHitNoCrit", "mobHp", "hitsToKill", "playerMaxHp"], rows)


if __name__ == "__main__":
    main()
