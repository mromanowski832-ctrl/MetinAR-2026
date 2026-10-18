using MetinAR.Location;
using Xunit;

namespace GeoCore.Tests
{
    /// <summary>
    /// R1/R2 (PR #11 review 5565064786): deterministic tests of the production permission/lifecycle
    /// decision code in <see cref="DeviceLocationProviderCore"/> via the <see cref="IDeviceLocationPlatform"/> seam.
    /// </summary>
    public class DeviceLocationProviderCoreTests
    {
        private const string Fine = LocationPermissionRequest.FineLocationPermission;
        private const string Coarse = LocationPermissionRequest.CoarseLocationPermission;
        private const double Timeout = 20.0;

        private static (DeviceLocationProviderCore core, FakeDeviceLocationPlatform platform) Create()
        {
            var platform = new FakeDeviceLocationPlatform();
            var core = new DeviceLocationProviderCore(platform, Timeout, 5f, 1f);
            return (core, platform);
        }

        private static void ServiceComesUp(DeviceLocationProviderCore core, FakeDeviceLocationPlatform platform)
        {
            platform.ServiceStatus = DeviceLocationServiceStatus.Running;
            core.Tick();
        }

        // ---------------- R1: single FINE + COARSE request, aggregated results ----------------

        [Fact]
        public void Start_WithoutPermission_IssuesExactlyOneRequestForFineAndCoarse()
        {
            var (core, platform) = Create();
            core.Start();

            Assert.Single(platform.Requests);
            Assert.Equal(new[] { Fine, Coarse }, platform.RequestedPermissionSets[0]);
            Assert.Equal(LocationProviderState.Starting, core.State);
            Assert.True(core.IsPermissionRequestPending);
            Assert.NotNull(platform.LastRequest.PlatformHandle); // callbacks retained while pending
            Assert.Equal(0, platform.StartServiceCount);
        }

        [Fact]
        public void PendingPermission_KeepsInitTimeoutDisarmed()
        {
            var (core, platform) = Create();
            core.Start();
            for (int i = 0; i < 10; i++)
            {
                platform.RealtimeSeconds += 30.0; // far beyond the 20 s init timeout
                core.Tick();
            }

            Assert.False(core.IsInitTimeoutArmed);
            Assert.Equal(LocationProviderState.Starting, core.State);
            Assert.Single(platform.Requests);
            Assert.Equal(0, platform.StartServiceCount);
        }

        [Fact]
        public void PreciseGrant_StartsOnce_RunsWithPreciseLevel()
        {
            var (core, platform) = Create();
            core.Start();
            platform.UserChooses(fine: true, coarse: true);
            platform.LastRequest.ReportGranted(Fine);
            platform.LastRequest.ReportGranted(Coarse);
            core.Tick();

            Assert.Equal(1, platform.StartServiceCount);
            Assert.True(core.IsInitTimeoutArmed);
            Assert.False(core.IsPermissionRequestPending);
            Assert.Null(platform.LastRequest.PlatformHandle);
            Assert.Equal(LocationPermissionLevel.Precise, core.PermissionLevel);

            ServiceComesUp(core, platform);
            core.Tick();
            Assert.Equal(LocationProviderState.Running, core.State);
            Assert.True(core.TryGetLatestFix(out LocationFix fix));
            Assert.Equal(52.2297, fix.Coordinate.Latitude);
            Assert.Equal(1, platform.StartServiceCount);
            Assert.Single(platform.Requests);
        }

        [Fact]
        public void CoarseOnly_FineDeniedFirst_ThenCoarseGranted_StartsOnceApproximate()
        {
            var (core, platform) = Create();
            core.Start();
            platform.UserChooses(fine: false, coarse: true);

            platform.LastRequest.ReportDenied(Fine);
            core.Tick();
            // Fine denial alone is not final and keeps the request (callbacks) alive.
            Assert.Equal(LocationProviderState.Starting, core.State);
            Assert.True(core.IsPermissionRequestPending);
            Assert.NotNull(platform.LastRequest.PlatformHandle);
            Assert.Equal(0, platform.StartServiceCount);

            platform.LastRequest.ReportGranted(Coarse);
            core.Tick();
            Assert.Equal(1, platform.StartServiceCount);
            Assert.False(core.IsPermissionRequestPending);
            Assert.Equal(LocationPermissionLevel.Approximate, core.PermissionLevel);
            Assert.NotEqual(LocationPermissionLevel.Precise, core.PermissionLevel);

            ServiceComesUp(core, platform);
            Assert.Equal(LocationProviderState.Running, core.State);
            Assert.Single(platform.Requests);
        }

        [Fact]
        public void CoarseOnly_CoarseGrantedFirst_ThenFineDenied_StartsOnceAndFineDenialDoesNotCancel()
        {
            var (core, platform) = Create();
            core.Start();
            platform.UserChooses(fine: false, coarse: true);

            platform.LastRequest.ReportGranted(Coarse);
            core.Tick();
            Assert.Equal(1, platform.StartServiceCount);
            Assert.True(core.IsPermissionRequestPending); // still waiting for the fine result

            platform.LastRequest.ReportDenied(Fine);
            core.Tick();
            Assert.False(core.IsPermissionRequestPending);
            Assert.Equal(LocationProviderState.Starting, core.State); // not PermissionDenied
            Assert.Equal(0, platform.StopServiceCount);
            Assert.Equal(1, platform.StartServiceCount);
            Assert.Equal(LocationPermissionLevel.Approximate, core.PermissionLevel);

            ServiceComesUp(core, platform);
            Assert.Equal(LocationProviderState.Running, core.State);
        }

        [Fact]
        public void CoarseOnly_BothCallbacksBeforeTick_EitherOrder_StartsOnce()
        {
            foreach (bool fineFirst in new[] { true, false })
            {
                var (core, platform) = Create();
                core.Start();
                platform.UserChooses(fine: false, coarse: true);
                if (fineFirst)
                {
                    platform.LastRequest.ReportDenied(Fine);
                    platform.LastRequest.ReportGranted(Coarse);
                }
                else
                {
                    platform.LastRequest.ReportGranted(Coarse);
                    platform.LastRequest.ReportDenied(Fine);
                }

                core.Tick();
                core.Tick();
                Assert.Equal(1, platform.StartServiceCount);
                Assert.Equal(LocationProviderState.Starting, core.State);
                Assert.Equal(LocationPermissionLevel.Approximate, core.PermissionLevel);
            }
        }

        [Fact]
        public void BothDenied_IsFinal_NoStartNoSecondRequest()
        {
            var (core, platform) = Create();
            core.Start();
            platform.LastRequest.ReportDenied(Fine);
            core.Tick();
            Assert.Equal(LocationProviderState.Starting, core.State);

            platform.LastRequest.ReportDenied(Coarse); // e.g. PermissionDeniedAndDontAskAgain
            core.Tick();
            Assert.Equal(LocationProviderState.PermissionDenied, core.State);
            Assert.Equal(LocationPermissionLevel.None, core.PermissionLevel);
            Assert.False(core.IsPermissionRequestPending);

            for (int i = 0; i < 5; i++)
            {
                core.Tick();
            }

            Assert.Single(platform.Requests);
            core.Start(); // Start while denied is an explicit new attempt: one new request, still no service
            Assert.Equal(2, platform.Requests.Count);
            Assert.Equal(0, platform.StartServiceCount);
            Assert.False(core.TryGetLatestFix(out _));
        }

        [Fact]
        public void BothDenied_TicksAndResumes_NeverRePrompt()
        {
            var (core, platform) = Create();
            core.Start();
            platform.LastRequest.ReportDenied(Fine);
            platform.LastRequest.ReportDenied(Coarse);
            core.Tick();
            core.Suspend();
            core.Resume();
            core.Tick();

            Assert.Single(platform.Requests);
            Assert.Equal(LocationProviderState.PermissionDenied, core.State);
            Assert.Equal(0, platform.StartServiceCount);
        }

        [Fact]
        public void Dismissed_WithNothingGranted_IsDenied_NotStuckStarting_NoRePrompt()
        {
            var (core, platform) = Create();
            core.Start();
            platform.LastRequest.ReportDismissed(Fine);
            core.Tick();

            Assert.Equal(LocationProviderState.PermissionDenied, core.State);
            Assert.False(core.IsPermissionRequestPending);
            core.Tick();
            core.Suspend();
            core.Resume();
            Assert.Single(platform.Requests);
            Assert.Equal(0, platform.StartServiceCount);
        }

        [Fact]
        public void Dismissed_AfterCoarseGrant_KeepsTheGrantAndStartsOnce()
        {
            var (core, platform) = Create();
            core.Start();
            platform.UserChooses(fine: false, coarse: true);
            platform.LastRequest.ReportGranted(Coarse);
            platform.LastRequest.ReportDismissed(Fine);
            core.Tick();

            Assert.Equal(1, platform.StartServiceCount);
            Assert.Equal(LocationProviderState.Starting, core.State);
            Assert.False(core.IsPermissionRequestPending);
        }

        [Fact]
        public void LateCallbacksAfterResolution_AreIgnored_NoDuplicateStart()
        {
            var (core, platform) = Create();
            core.Start();
            platform.UserChooses(fine: true, coarse: true);
            LocationPermissionRequest request = platform.LastRequest;
            request.ReportGranted(Fine);
            request.ReportGranted(Coarse);
            core.Tick();

            request.ReportDenied(Fine);
            request.ReportGranted(Coarse);
            request.ReportDismissed(Fine);
            core.Tick();
            core.Tick();

            Assert.Equal(1, platform.StartServiceCount);
            Assert.Equal(LocationProviderState.Starting, core.State);
            Assert.Equal(LocationPermissionResult.Granted, request.Snapshot().Fine);
        }

        [Fact]
        public void AlreadyGranted_NoPrompt_StartsImmediatelyOnce()
        {
            var (core, platform) = Create();
            platform.UserChooses(fine: true, coarse: true);
            core.Start();
            core.Start(); // idempotent while Starting

            Assert.Empty(platform.Requests);
            Assert.Equal(1, platform.StartServiceCount);
            Assert.True(core.IsInitTimeoutArmed);
            Assert.Equal(LocationPermissionLevel.Precise, core.PermissionLevel);
            ServiceComesUp(core, platform);
            core.Start(); // idempotent while Running
            Assert.Equal(LocationProviderState.Running, core.State);
            Assert.Equal(1, platform.StartServiceCount);
        }

        [Fact]
        public void AlreadyGrantedCoarseOnly_IsApproximate_NotPrecise()
        {
            var (core, platform) = Create();
            platform.UserChooses(fine: false, coarse: true);
            core.Start();

            Assert.Empty(platform.Requests);
            Assert.Equal(1, platform.StartServiceCount);
            Assert.Equal(LocationPermissionLevel.Approximate, core.PermissionLevel);
        }

        [Fact]
        public void AlreadyGranted_LocationOff_IsLocationDisabled()
        {
            var (core, platform) = Create();
            platform.UserChooses(fine: true, coarse: true);
            platform.IsLocationEnabledByUser = false;
            core.Start();

            Assert.Equal(LocationProviderState.LocationDisabled, core.State);
            Assert.Empty(platform.Requests);
            Assert.Equal(0, platform.StartServiceCount);
        }

        [Fact]
        public void StopThenLateGrant_DoesNotStartAcquisition()
        {
            var (core, platform) = Create();
            core.Start();
            core.Stop();
            platform.UserChooses(fine: true, coarse: true);
            platform.LastRequest.ReportGranted(Fine);
            platform.LastRequest.ReportGranted(Coarse);
            core.Tick();
            core.Tick();

            Assert.Equal(LocationProviderState.Stopped, core.State);
            Assert.Equal(0, platform.StartServiceCount);
            Assert.False(core.IsPermissionRequestPending);
            Assert.False(core.TryGetLatestFix(out _));
        }

        [Fact]
        public void StopThenLateDenial_StaysStopped_NotPermissionDenied()
        {
            var (core, platform) = Create();
            core.Start();
            core.Stop();
            platform.LastRequest.ReportDenied(Fine);
            platform.LastRequest.ReportDenied(Coarse);
            core.Tick();

            Assert.Equal(LocationProviderState.Stopped, core.State);
            Assert.Equal(0, platform.StartServiceCount);
        }

        [Fact]
        public void StopThenStartWhileDialogOutstanding_ReusesRequest_NoDuplicatePrompt_StartsOnce()
        {
            var (core, platform) = Create();
            core.Start();
            core.Stop();
            core.Start();

            Assert.Single(platform.Requests);
            Assert.Equal(LocationProviderState.Starting, core.State);
            platform.UserChooses(fine: true, coarse: true);
            platform.LastRequest.ReportGranted(Fine);
            platform.LastRequest.ReportGranted(Coarse);
            core.Tick();
            core.Tick();
            Assert.Equal(1, platform.StartServiceCount);
        }

        [Fact]
        public void StopAfterLateResolvedDenial_ThenStart_IsANewAttemptWithOneNewRequest()
        {
            var (core, platform) = Create();
            core.Start();
            core.Stop();
            LocationPermissionRequest old = platform.LastRequest;
            old.ReportDenied(Fine);
            old.ReportDenied(Coarse);
            core.Start(); // cancelled attempt's denial is not applied to the new attempt

            Assert.Equal(2, platform.Requests.Count);
            Assert.Equal(LocationProviderState.Starting, core.State);
            old.ReportGranted(Coarse); // ignored (old request resolved/dropped)
            core.Tick();
            Assert.Equal(0, platform.StartServiceCount);
        }

        [Fact]
        public void RepeatedStartAndTicksWhilePending_IssueOnlyOneRequest()
        {
            var (core, platform) = Create();
            core.Start();
            core.Start();
            core.Tick();
            core.Start();
            core.Tick();

            Assert.Single(platform.Requests);
            Assert.Equal(0, platform.StartServiceCount);
        }

        [Fact]
        public void InitTimeout_ArmedOnlyAfterServiceStart_FailsAndStopsService()
        {
            var (core, platform) = Create();
            platform.UserChooses(fine: true, coarse: true);
            core.Start();
            platform.RealtimeSeconds += Timeout - 1.0;
            core.Tick();
            Assert.Equal(LocationProviderState.Starting, core.State);
            platform.RealtimeSeconds += 1.0;
            core.Tick();

            Assert.Equal(LocationProviderState.Failed, core.State);
            Assert.Equal(1, platform.StopServiceCount);
            Assert.False(core.IsServiceStarted);
        }

        [Fact]
        public void NonRuntimePermissionPlatform_StartsServiceDirectly_LevelUnknown()
        {
            var (core, platform) = Create();
            platform.RequiresRuntimePermission = false;
            platform.IsLocationEnabledByUser = false; // iOS: not gated before the first start
            core.Start();

            Assert.Empty(platform.Requests);
            Assert.Equal(1, platform.StartServiceCount);
            Assert.Equal(LocationPermissionLevel.Unknown, core.PermissionLevel);
        }

        [Fact]
        public void RequestedAccuracy_IsTheDesiredValueOnly()
        {
            var (core, platform) = Create();
            platform.UserChooses(fine: false, coarse: true);
            core.Start();
            Assert.Equal(5f, platform.LastDesiredAccuracy);
            Assert.Equal(LocationPermissionLevel.Approximate, core.PermissionLevel);
        }

        // ---------------- R2: foreground-only suspend / resume ----------------

        [Fact]
        public void PauseResume_ActiveProvider_StopsHardwareInvalidatesFix_ThenRestartsWithoutPrompt()
        {
            var (core, platform) = Create();
            platform.UserChooses(fine: true, coarse: true);
            core.Start();
            ServiceComesUp(core, platform);
            Assert.True(core.TryGetLatestFix(out _));

            core.Suspend();
            Assert.True(core.IsSuspended);
            Assert.Equal(1, platform.StopServiceCount);
            Assert.False(core.TryGetLatestFix(out _));
            Assert.NotEqual(LocationProviderState.Running, core.State);
            Assert.False(core.IsInitTimeoutArmed);

            platform.RealtimeSeconds += 600.0; // long background: no timeout, no polling
            platform.ServiceStatus = DeviceLocationServiceStatus.Running; // even if the OS reports running
            core.Tick();
            Assert.False(core.TryGetLatestFix(out _));
            Assert.Equal(LocationProviderState.Starting, core.State);
            Assert.Equal(1, platform.StartServiceCount);

            core.Resume();
            Assert.False(core.IsSuspended);
            Assert.Equal(2, platform.StartServiceCount);
            Assert.Empty(platform.Requests);
            ServiceComesUp(core, platform);
            Assert.Equal(LocationProviderState.Running, core.State);
            Assert.True(core.TryGetLatestFix(out _));
        }

        [Fact]
        public void PauseDuringServiceInit_DisarmsTimeout_ResumeRearms()
        {
            var (core, platform) = Create();
            platform.UserChooses(fine: true, coarse: true);
            core.Start();
            core.Suspend();
            platform.RealtimeSeconds += 100.0;
            core.Resume();
            core.Tick();

            Assert.Equal(LocationProviderState.Starting, core.State);
            Assert.True(core.IsInitTimeoutArmed);
            Assert.Equal(2, platform.StartServiceCount);
        }

        [Fact]
        public void ManualStop_ThenPauseResume_StaysStopped()
        {
            var (core, platform) = Create();
            platform.UserChooses(fine: true, coarse: true);
            core.Start();
            ServiceComesUp(core, platform);
            core.Stop();
            core.Suspend();
            core.Resume();
            core.Tick();

            Assert.Equal(LocationProviderState.Stopped, core.State);
            Assert.Equal(1, platform.StartServiceCount);
            Assert.Empty(platform.Requests);
        }

        [Fact]
        public void PermissionDialogPausesApp_GrantArrivesWhilePaused_ResumeStartsOnce_NoSecondRequest()
        {
            var (core, platform) = Create();
            core.Start();
            core.Suspend(); // Android: the permission dialog pauses the Unity activity
            Assert.True(core.IsPermissionRequestPending);
            Assert.Equal(0, platform.StopServiceCount); // nothing was started

            platform.UserChooses(fine: true, coarse: true);
            platform.LastRequest.ReportGranted(Fine);
            platform.LastRequest.ReportGranted(Coarse);
            core.Tick(); // no player loop while paused, but even if ticked: nothing starts in background
            Assert.Equal(0, platform.StartServiceCount);

            core.Resume();
            Assert.Equal(1, platform.StartServiceCount);
            Assert.Single(platform.Requests);
            core.Tick();
            Assert.Equal(1, platform.StartServiceCount);
        }

        [Fact]
        public void PermissionDialog_ResumeBeforeCallback_WaitsWithoutRePrompt_ThenStartsOnce()
        {
            var (core, platform) = Create();
            core.Start();
            core.Suspend();
            core.Resume(); // callbacks not delivered yet
            core.Tick();
            Assert.Single(platform.Requests);
            Assert.Equal(LocationProviderState.Starting, core.State);
            Assert.False(core.IsInitTimeoutArmed);

            // Repeated pause/resume transitions while the dialog result is outstanding.
            core.Suspend();
            core.Resume();
            core.Suspend();
            core.Resume();
            Assert.Single(platform.Requests);

            platform.UserChooses(fine: false, coarse: true);
            platform.LastRequest.ReportGranted(Coarse);
            core.Tick();
            platform.LastRequest.ReportDenied(Fine);
            core.Tick();
            Assert.Equal(1, platform.StartServiceCount);
            Assert.Equal(LocationPermissionLevel.Approximate, core.PermissionLevel);
        }

        [Fact]
        public void PermissionDialog_DenialArrivesWhilePaused_ResumeDoesNotStartOrRePrompt()
        {
            var (core, platform) = Create();
            core.Start();
            core.Suspend();
            platform.LastRequest.ReportDenied(Fine);
            platform.LastRequest.ReportDenied(Coarse);
            core.Resume();
            core.Tick();

            Assert.Equal(LocationProviderState.PermissionDenied, core.State);
            Assert.Single(platform.Requests);
            Assert.Equal(0, platform.StartServiceCount);
        }

        [Fact]
        public void StopWhilePausedDuringDialog_LateGrantThenResume_DoesNotStart()
        {
            var (core, platform) = Create();
            core.Start();
            core.Suspend();
            core.Stop();
            platform.UserChooses(fine: true, coarse: true);
            platform.LastRequest.ReportGranted(Fine);
            platform.LastRequest.ReportGranted(Coarse);
            core.Resume();
            core.Tick();

            Assert.Equal(LocationProviderState.Stopped, core.State);
            Assert.Equal(0, platform.StartServiceCount);
        }

        [Fact]
        public void StartWhileSuspended_IsDeferredToResume_NoBackgroundPromptOrService()
        {
            var (core, platform) = Create();
            core.Suspend();
            core.Start();
            core.Tick();
            Assert.Empty(platform.Requests);
            Assert.Equal(0, platform.StartServiceCount);

            core.Resume();
            Assert.Single(platform.Requests);
            Assert.Equal(LocationProviderState.Starting, core.State);
        }

        [Fact]
        public void PermissionRevokedWhileSuspended_ResumeDoesNotSilentlyRePrompt()
        {
            var (core, platform) = Create();
            platform.UserChooses(fine: true, coarse: true);
            core.Start();
            ServiceComesUp(core, platform);
            core.Suspend();
            platform.UserChooses(fine: false, coarse: false);
            core.Resume();

            Assert.Equal(LocationProviderState.PermissionDenied, core.State);
            Assert.Empty(platform.Requests);
            Assert.Equal(1, platform.StartServiceCount);
        }

        [Fact]
        public void LocationDisabledOrFailed_AreNotRestartedByResume()
        {
            var (core, platform) = Create();
            platform.UserChooses(fine: true, coarse: true);
            platform.IsLocationEnabledByUser = false;
            core.Start();
            core.Suspend();
            platform.IsLocationEnabledByUser = true;
            core.Resume();
            Assert.Equal(LocationProviderState.LocationDisabled, core.State);
            Assert.Equal(0, platform.StartServiceCount);

            var (core2, platform2) = Create();
            platform2.UserChooses(fine: true, coarse: true);
            core2.Start();
            platform2.ServiceStatus = DeviceLocationServiceStatus.Failed;
            core2.Tick();
            Assert.Equal(LocationProviderState.Failed, core2.State);
            core2.Suspend();
            core2.Resume();
            Assert.Equal(LocationProviderState.Failed, core2.State);
            Assert.Equal(1, platform2.StartServiceCount);
        }

        [Fact]
        public void SuspendAndResume_AreIdempotent()
        {
            var (core, platform) = Create();
            platform.UserChooses(fine: true, coarse: true);
            core.Start();
            ServiceComesUp(core, platform);
            core.Suspend();
            core.Suspend();
            Assert.Equal(1, platform.StopServiceCount);
            core.Resume();
            core.Resume();
            Assert.Equal(2, platform.StartServiceCount);
        }
    }
}
