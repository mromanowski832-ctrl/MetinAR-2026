using System;

namespace MetinAR.ArEncounter
{
    /// <summary>What the scene adapter must do this frame for the placed mob (#97).</summary>
    public enum ArNoWalkAction
    {
        /// <summary>Nothing beyond applying <see cref="ArNoWalkFrame.MobPose"/> when <see cref="ArNoWalkFrame.MobMoved"/>.</summary>
        None,

        /// <summary>Tracking lost too long: reset the AR session, keep the mob object, its HP and reward state.</summary>
        ResetSession,

        /// <summary>Tracking back after a reset: put the mob at <see cref="ArNoWalkFrame.MobPose"/> (camera-relative restore), anchor it, then call <see cref="ArNoWalkEncounter.MarkRestored"/>.</summary>
        Restore,
    }

    /// <summary>Per-frame output of <see cref="ArNoWalkEncounter.Tick"/>.</summary>
    public readonly struct ArNoWalkFrame
    {
        public ArNoWalkFrame(ArNoWalkAction action, ArYawPose mobPose, bool mobMoved, bool combatAllowed, bool mobVisible, ArMobMotion motion, double stepMeters)
        {
            Action = action;
            MobPose = mobPose;
            MobMoved = mobMoved;
            CombatAllowed = combatAllowed;
            MobVisible = mobVisible;
            Motion = motion;
            StepMeters = stepMeters;
        }

        public ArNoWalkAction Action { get; }
        public ArYawPose MobPose { get; }
        public bool MobMoved { get; }

        /// <summary>Swings may be started and resolved (tracking, no pending restore). Defeat is handled by the combat layer.</summary>
        public bool CombatAllowed { get; }

        public bool MobVisible { get; }
        public ArMobMotion Motion { get; }
        public double StepMeters { get; }
    }

    /// <summary>
    /// #97: one placed AR mob without player walking — combines <see cref="ArTrackingRecovery"/> (pause on loss, reset
    /// after the configured time), <see cref="ArMobApproach"/> (the mob comes to the camera) and
    /// <see cref="ArCameraRelativePose"/> (restore after a reset at the same camera-relative pose, never at the reticle).
    /// It never touches HP, the reward or any combat progress: the fight object simply is not resolved while
    /// <see cref="ArNoWalkFrame.CombatAllowed"/> is false. No UnityEngine dependency.
    /// </summary>
    public sealed class ArNoWalkEncounter
    {
        public ArNoWalkEncounter(ArNoWalkSettings settings, ArTrackingRecovery recovery, double stopDistanceMeters)
        {
            Recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
            Approach = new ArMobApproach(settings, stopDistanceMeters);
        }

        public ArTrackingRecovery Recovery { get; }
        public ArMobApproach Approach { get; }
        public ArNoWalkSettings Settings => Approach.Settings;

        /// <summary>The mob's pose relative to the camera in the last usable frame.</summary>
        public ArCameraRelativePose LastRelativePose { get; private set; }

        public int RestoreCount { get; private set; }

        /// <summary>
        /// One frame. <paramref name="mobCanMove"/>: approach enabled and the mob is alive (a defeated mob does not walk).
        /// </summary>
        public ArNoWalkFrame Tick(bool tracking, ArYawPose camera, ArYawPose mob, double deltaSeconds, bool mobCanMove)
        {
            ArRecoveryAction recovery = Recovery.Tick(true, tracking, (float)deltaSeconds);
            if (recovery == ArRecoveryAction.ResetSession)
            {
                Approach.Stop();
                return new ArNoWalkFrame(ArNoWalkAction.ResetSession, mob, false, false, false, ArMobMotion.Paused, 0.0);
            }

            if (recovery == ArRecoveryAction.Relocate)
            {
                Approach.Stop();
                ArYawPose restored = LastRelativePose.IsValid
                    ? LastRelativePose.Restore(camera, Settings.minDistanceMeters, Settings.maxRestoreDistanceMeters)
                    : mob;
                // Camera-relative displacement of the restore: only the [d_min, max] clamp can move the mob relative to the camera.
                double jump = LastRelativePose.IsValid
                    ? Math.Abs(LastRelativePose.ClampedDistance(Settings.minDistanceMeters, Settings.maxRestoreDistanceMeters) - LastRelativePose.DistanceMeters)
                    : 0.0;
                return new ArNoWalkFrame(ArNoWalkAction.Restore, restored, LastRelativePose.IsValid, false, false, ArMobMotion.Paused, jump);
            }

            if (!Recovery.MobUsable)
            {
                Approach.Tick(camera.X, camera.Z, mob.X, mob.Z, mob.YawDegrees, deltaSeconds, false);
                return new ArNoWalkFrame(ArNoWalkAction.None, mob, false, false, false, ArMobMotion.Paused, 0.0);
            }

            ArMobStep step = Approach.Tick(camera.X, camera.Z, mob.X, mob.Z, mob.YawDegrees, deltaSeconds, mobCanMove);
            var pose = new ArYawPose(step.X, mob.Y, step.Z, step.YawDegrees);
            bool moved = step.StepMeters > 0.0 || Math.Abs(ArMobApproach.DeltaAngle(step.YawDegrees, mob.YawDegrees)) > 1e-9;
            LastRelativePose = ArCameraRelativePose.Capture(camera, pose);
            return new ArNoWalkFrame(ArNoWalkAction.None, pose, moved, true, true, step.Motion, step.StepMeters);
        }

        /// <summary>The mob was put at the restored pose and anchored again.</summary>
        public void MarkRestored()
        {
            Recovery.MarkRelocated();
            RestoreCount++;
        }
    }
}
