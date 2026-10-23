using System.Collections.Generic;
using MetinAR.ArEncounter;
using MetinAR.Gameplay.Combat;
using MetinAR.Gameplay.Data;
using Xunit;

namespace MetinAR.Gameplay.Tests
{
    /// <summary>
    /// #97 regression 5 (combat side): a tracking loss with a session reset pauses combat and the camera-relative restore keeps
    /// the fight — the same <see cref="EncounterCombatSession"/> continues with the mob's HP, hit count and reward intact.
    /// The scene controller only resolves swings while the no-walk frame allows combat (FppCombatController.CombatEnabled);
    /// here a swing is only registered when <see cref="ArNoWalkFrame.CombatAllowed"/> is true.
    /// </summary>
    public class ArNoWalkCombatContinuityTests
    {
        private static readonly BalanceConfig Config = BalanceConfig.CreateDefault();
        private static readonly double[] PlainHit = { 0.5, 0.5, 0.9 };

        private static double[] Repeat(double[] pattern, int times, params double[] tail)
        {
            var list = new List<double>();
            for (int i = 0; i < times; i++) list.AddRange(pattern);
            list.AddRange(tail);
            return list.ToArray();
        }

        [Fact]
        public void T5_TrackingLossAndRestore_KeepMobHpAndResumeTheSameFight()
        {
            var settings = new ArNoWalkSettings();
            double stop = ArMobStandoff.StopDistance(1.21, settings, ArEncounterFraming.MobVisualScale, out _);
            float resetAfter = ArPlacementSettings.CreatePrototypeDefaults().trackingLostResetSeconds;
            var encounter = new ArNoWalkEncounter(settings, new ArTrackingRecovery(resetAfter), stop);
            var session = EncounterCombatSession.WithStarterLoadout(Config, MobCombatProfile.VenomousSpider(Config),
                new PlayerProgress(new ExpCurve(Config), ClassId.Warrior), new ScriptedRandom(Repeat(PlainHit, 14, 0.5)));

            var cam = new ArYawPose(0, 1.2, 0, 0);
            var mob = new ArYawPose(0, 0, stop, 180);
            int swing = 0;
            const double dt = 1.0 / 60;

            void Frame(bool tracking, ArYawPose camera, bool swingThisFrame)
            {
                ArNoWalkFrame f = encounter.Tick(tracking, camera, mob, dt, !session.MobDefeated);
                if (f.Action == ArNoWalkAction.Restore) encounter.MarkRestored();
                mob = f.MobPose;
                if (swingThisFrame && f.CombatAllowed) session.RegisterHit(++swing);
            }

            for (int i = 0; i < 5; i++) Frame(true, cam, true);  // 5 hits × 9 = 45
            Assert.Equal(75, session.MobHp);

            // Tracking lost long enough for a session reset; the player keeps swinging - nothing lands.
            int lostFrames = (int)((resetAfter + 1.5) / dt);
            for (int i = 0; i < lostFrames; i++) Frame(false, cam, true);
            Assert.True(encounter.Recovery.ResetCount >= 1);
            Assert.Equal(5, session.HitsLanded);
            Assert.Equal(75, session.MobHp);

            // Tracking back in a new world frame: restored camera-relative, HP kept, the same fight continues to the kill.
            var camAfter = new ArYawPose(3.0, 0.8, -1.0, 120);
            Frame(true, camAfter, true);  // the restore frame itself is not a combat frame
            Assert.Equal(1, encounter.RestoreCount);
            Assert.Equal(stop, ArYawPose.HorizontalDistance(camAfter, mob), 9);
            Assert.Equal(75, session.MobHp);
            for (int i = 0; i < 9; i++) Frame(true, camAfter, true);
            Assert.True(session.MobDefeated);
            Assert.Equal(14, session.HitsLanded);
            Assert.Equal(120, session.TotalDamage);
            Assert.NotNull(session.Reward);

            // A defeated mob does not walk any more.
            var far = new ArYawPose(3.0, 0.8, -4.0, 120);
            ArNoWalkFrame after = encounter.Tick(true, far, mob, dt, !session.MobDefeated);
            Assert.Equal(0.0, after.StepMeters);
        }
    }
}
