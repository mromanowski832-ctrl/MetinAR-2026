using System;
using MetinAR.Gameplay;
using MetinAR.Gameplay.Combat;
using MetinAR.Progression;
using UnityEngine;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// METINAR-020 (S2a): turns FPP hits into damage. <see cref="HandleHitDetected"/> has the signature of
    /// <c>FppCombatController.HitDetected(ArEncounterMob, int swingId)</c> from #31, so the FPP integration is
    /// <c>FppCombat.HitDetected += Combat.HandleHitDetected</c>. Each hit goes through the pure
    /// <see cref="EncounterCombatSession"/> (DamageResolver → mob HP → death → EXP and talary into
    /// <see cref="LocalPlayerSession"/>), then the mob label/HP bar, a HUD toast and a log line are updated.
    /// Added at runtime by <see cref="ArEncounterSceneController"/>; no scene or prefab change.
    /// </summary>
    public sealed class ArEncounterCombat : MonoBehaviour
    {
        public const string LogPrefix = "[ArDamage] ";

        private ArEncounterSceneController _controller;
        private ArEncounterMob _mob;
        private int _hitEvents;

        /// <summary>Raised after every resolved or ignored hit event.</summary>
        public event Action<CombatHitReport> HitResolved;

        /// <summary>Raised once when the bound mob reaches 0 HP (after rewards were granted).</summary>
        public event Action<ArEncounterMob, MobKillReward> MobDefeated;

        /// <summary>Fixed seed for tests; otherwise a time-based seed is used per encounter.</summary>
        public ulong? SeedOverride { get; set; }

        public EncounterCombatSession Session { get; private set; }

        public ArEncounterMob BoundMob => _mob;

        public CombatHitReport LastReport { get; private set; }

        public int HitEvents => _hitEvents;

        public void Initialize(ArEncounterSceneController controller)
        {
            if (_controller != null)
            {
                _controller.MobPlaced -= Bind;
            }

            _controller = controller;
            if (_controller != null)
            {
                _controller.MobPlaced += Bind;
            }
        }

        private void OnDestroy()
        {
            if (_controller != null)
            {
                _controller.MobPlaced -= Bind;
            }
        }

        /// <summary>Starts the fight against <paramref name="mob"/> (called when the mob is placed).</summary>
        public void Bind(ArEncounterMob mob)
        {
            _mob = mob;
            Session = null;
            LastReport = null;
            if (mob == null)
            {
                return;
            }

            BalanceConfig config = LocalPlayerSession.Config;
            ArEncounterRequest request = mob.Request;
            int level = Mathf.Clamp(request != null ? request.Level : 5, 1, config.MaxMobLevel);
            // The encounter request carries no mob id yet; the starter kit has only the Venomous Spider (MOBS_NPC.md §3).
            MobCombatProfile profile = MobCombatProfile.VenomousSpider(config, level);
            int? startHp = null;
            if (request != null && request.MaxHp == profile.MaxHp && request.CurrentHp >= 1)
            {
                startHp = request.CurrentHp;
            }

            ulong seed = SeedOverride ?? (ulong)DateTime.UtcNow.Ticks;
            Session = EncounterCombatSession.WithStarterLoadout(config, profile, LocalPlayerSession.Progress, new SeededGameplayRandom(seed), startHp);
            mob.ShowHp(Session.MobHp, Session.MobMaxHp);
            Debug.Log(LogPrefix + "bound " + profile.Id + " lvl=" + profile.Level + " hp=" + Session.MobHp + "/" + Session.MobMaxHp
                      + " def=" + profile.Def + " player lvl=" + LocalPlayerSession.Progress.Level);
        }

        /// <summary>FPP hit event: the blade geometrically hit <paramref name="mob"/> during swing <paramref name="swingId"/>.</summary>
        public void HandleHitDetected(ArEncounterMob mob, int swingId)
        {
            _hitEvents++;
            if (Session == null || mob == null || mob != _mob)
            {
                Debug.LogWarning(LogPrefix + "hit ignored: mob is not the bound encounter mob (swing " + swingId + ")");
                return;
            }

            CombatHitReport report = Session.RegisterHit(swingId);
            LastReport = report;
            if (report.Status == CombatHitStatus.Resolved)
            {
                mob.ShowHp(report.HpAfter, report.MaxHp);
                Debug.Log(LogPrefix + "swing=" + swingId + " outcome=" + report.Damage.Outcome + (report.Damage.IsCritical ? " crit" : string.Empty)
                          + " dmg=" + report.Damage.Damage + " hp=" + report.HpBefore + "->" + report.HpAfter + "/" + report.MaxHp);
                Toast(report.Damage.Landed
                    ? (report.Damage.IsCritical ? "Krytyk! −" : "Trafienie −") + report.Damage.Damage + " HP"
                    : "Pudło");
            }

            if (report.Killed && report.Reward.HasValue)
            {
                MobKillReward reward = report.Reward.Value;
                mob.ShowDefeated();
                PlayerProgress p = LocalPlayerSession.Progress;
                Debug.Log(LogPrefix + "defeated " + Session.Mob.Id + " exp=+" + reward.Exp + " talary=+" + reward.Talary
                          + " player lvl=" + p.Level + " exp=" + p.ExpIntoLevel + "/" + p.ExpToNextLevel + " orbs=" + p.FilledHudOrbs.ToString("0.###")
                          + " talary=" + p.Talary);
                string text = "Pokonano! +" + reward.Exp + " EXP, +" + reward.Talary + " talarów";
                if (report.LevelsGained > 0)
                {
                    text += "\nAwans: poziom " + report.PlayerLevelAfter + "!";
                }

                Toast(text);
                LocalPlayerSession.NotifyChanged();
                MobDefeated?.Invoke(mob, reward);
            }

            HitResolved?.Invoke(report);
        }

        private void Toast(string message)
        {
            if (_controller != null && _controller.Hud != null)
            {
                _controller.Hud.ShowToast(message);
            }
        }
    }
}
