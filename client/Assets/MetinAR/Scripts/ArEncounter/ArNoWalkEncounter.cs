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

        /// <summary>After a restore: still waiting for a plane within tolerance of the restored height to ease onto (#97 Q2).</summary>
        public bool PlaneSettlePending { get; private set; }

        /// <summary>Height the mob was restored at (recorded camera drop), the reference of the plane tolerance.</summary>
        public double RestoredY { get; private set; }

        /// <summary>Tracking came back after a loss, but not yet for <see cref="ArNoWalkSettings.combatResumeStableSeconds"/> (#97 Q2).</summary>
        public bool CombatResumePending { get; private set; }

        /// <summary>Seconds of uninterrupted tracking since the last frame without it.</summary>
        public double StableTrackingSeconds { get; private set; }

        /// <summary>
        /// One frame. <paramref name="mobCanMove"/>: approach enabled and the mob is alive (a defeated mob does not walk).
        /// </summary>
        /// <param name="planeY">Height of a horizontal plane under the mob this frame, or null (only used after a restore).</param>
        public ArNoWalkFrame Tick(bool tracking, ArYawPose camera, ArYawPose mob, double deltaSeconds, bool mobCanMove, double? planeY = null)
        {
            if (!tracking)
            {
                CombatResumePending = true;
                StableTrackingSeconds = 0.0;
            }

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
                // Lead decision 04:42 (#97 Q2): restore at once at the recorded camera drop; a plane found later may refine it.
                PlaneSettlePending = LastRelativePose.IsValid;
                RestoredY = restored.Y;
                CombatResumePending = true;
                StableTrackingSeconds = 0.0;
                // #97 Q5: this one-time correction may exceed the step limit ONLY because the mob is hidden and combat paused.
                return new ArNoWalkFrame(ArNoWalkAction.Restore, restored, LastRelativePose.IsValid, false, false, ArMobMotion.Paused, jump);
            }

            if (!Recovery.MobUsable)
            {
                Approach.Tick(camera.X, camera.Z, mob.X, mob.Z, mob.YawDegrees, deltaSeconds, false);
                return new ArNoWalkFrame(ArNoWalkAction.None, mob, false, false, false, ArMobMotion.Paused, 0.0);
            }

            if (CombatResumePending)
            {
                if (deltaSeconds > 0.0 && !double.IsInfinity(deltaSeconds))
                {
                    StableTrackingSeconds += deltaSeconds;
                }

                if (StableTrackingSeconds >= Settings.combatResumeStableSeconds)
                {
                    CombatResumePending = false;
                }
            }

            // #97 Q6: after tracking returns, a mob outside [d_min, reach] walks / backs off smoothly to d_stop (ArMobApproach).
            ArMobStep step = Approach.Tick(camera.X, camera.Z, mob.X, mob.Z, mob.YawDegrees, deltaSeconds, mobCanMove);
            double y = mob.Y;
            double vertical = 0.0;
            if (PlaneSettlePending && planeY.HasValue && !double.IsNaN(planeY.Value) && !double.IsInfinity(planeY.Value)
                && Math.Abs(planeY.Value - RestoredY) <= Settings.planeSettleToleranceMeters + 1e-9)
            {
                // #97 Q2 + Q5: visible → ease onto the plane with what is left of this frame's step limit (no jump).
                double budget = Math.Max(0.0, Settings.StepLimitMeters(deltaSeconds) - step.StepMeters);
                double diff = planeY.Value - y;
                vertical = Math.Min(Math.Abs(diff), budget);
                y += Math.Sign(diff) * vertical;
                if (Math.Abs(planeY.Value - y) <= 1e-9)
                {
                    y = planeY.Value;
                    PlaneSettlePending = false;
                }
            }

            var pose = new ArYawPose(step.X, y, step.Z, step.YawDegrees);
            bool moved = step.StepMeters > 0.0 || vertical > 0.0 || Math.Abs(ArMobApproach.DeltaAngle(step.YawDegrees, mob.YawDegrees)) > 1e-9;
            LastRelativePose = ArCameraRelativePose.Capture(camera, pose);
            return new ArNoWalkFrame(ArNoWalkAction.None, pose, moved, !CombatResumePending, true, step.Motion, step.StepMeters + vertical);
        }

        /// <summary>The mob was put at the restored pose and anchored again.</summary>
        public void MarkRestored()
        {
            Recovery.MarkRelocated();
            RestoreCount++;
        }
    }
}
