using MetinAR.Location;
using Xunit;

namespace GeoCore.Tests
{
    /// <summary>
    /// R2 (PR #11 review 5565064786): production <see cref="ForegroundLocationLifecycle"/> policy used by
    /// PlayerLocationService, with the real <see cref="MockLocationProvider"/> and the real
    /// <see cref="DeviceLocationProviderCore"/> (via the fake platform seam).
    /// </summary>
    public class ForegroundLocationLifecycleTests
    {
        private const string Fine = LocationPermissionRequest.FineLocationPermission;
        private const string Coarse = LocationPermissionRequest.CoarseLocationPermission;
        private static readonly GeoCoordinate Start = new GeoCoordinate(10.0, 20.0);

        private static ForegroundLocationLifecycle EnabledWith(ILocationProvider provider, bool startOnEnable = true)
        {
            var lifecycle = new ForegroundLocationLifecycle();
            lifecycle.AttachProvider(provider);
            lifecycle.SetComponentEnabled(true);
            if (startOnEnable)
            {
                lifecycle.RequestStart();
            }

            return lifecycle;
        }

        private static (ForegroundLocationLifecycle lifecycle, DeviceLocationProviderCore core, FakeDeviceLocationPlatform platform) DeviceSetup()
        {
            var platform = new FakeDeviceLocationPlatform();
            var core = new DeviceLocationProviderCore(platform, 20.0, 5f, 1f);
            return (EnabledWith(core), core, platform);
        }

        [Fact]
        public void Mock_PauseStopsAndInvalidatesFix_ResumeRestartsAtSameCoordinate()
        {
            var mock = new MockLocationProvider(Start);
            var lifecycle = EnabledWith(mock);
            Assert.True(mock.TryGetLatestFix(out _));

            lifecycle.SetApplicationPaused(true);
            Assert.Equal(LocationProviderState.Stopped, mock.State);
            Assert.False(mock.TryGetLatestFix(out _));
            Assert.True(lifecycle.AcquisitionRequested);

            lifecycle.SetApplicationPaused(false);
            Assert.Equal(LocationProviderState.Running, mock.State);
            Assert.True(mock.TryGetLatestFix(out LocationFix fix));
            Assert.Equal(Start.Latitude, fix.Coordinate.Latitude);
            Assert.Equal(Start.Longitude, fix.Coordinate.Longitude);
        }

        [Fact]
        public void Mock_ManualStopThenResume_StaysStopped()
        {
            var mock = new MockLocationProvider(Start);
            var lifecycle = EnabledWith(mock);
            lifecycle.RequestStop();
            lifecycle.SetApplicationPaused(true);
            lifecycle.SetApplicationPaused(false);

            Assert.Equal(LocationProviderState.Stopped, mock.State);
            Assert.False(lifecycle.AcquisitionRequested);
        }

        [Fact]
        public void Mock_StopWhilePaused_NotRestartedOnResume()
        {
            var mock = new MockLocationProvider(Start);
            var lifecycle = EnabledWith(mock);
            lifecycle.SetApplicationPaused(true);
            lifecycle.RequestStop();
            lifecycle.SetApplicationPaused(false);

            Assert.Equal(LocationProviderState.Stopped, mock.State);
        }

        [Fact]
        public void Mock_DisabledComponent_NotRestartedOnResume()
        {
            var mock = new MockLocationProvider(Start);
            var lifecycle = EnabledWith(mock);
            lifecycle.SetComponentEnabled(false);
            lifecycle.SetApplicationPaused(true);
            lifecycle.SetApplicationPaused(false);

            Assert.Equal(LocationProviderState.Stopped, mock.State);
            Assert.False(lifecycle.AcquisitionRequested);
        }

        [Fact]
        public void Mock_DisabledWhilePaused_NotRestartedOnResume()
        {
            var mock = new MockLocationProvider(Start);
            var lifecycle = EnabledWith(mock);
            lifecycle.SetApplicationPaused(true);
            lifecycle.SetComponentEnabled(false);
            lifecycle.SetApplicationPaused(false);

            Assert.Equal(LocationProviderState.Stopped, mock.State);
        }

        [Fact]
        public void Mock_StartRequestedWhilePaused_IsDeferredUntilResume()
        {
            var mock = new MockLocationProvider(Start);
            var lifecycle = EnabledWith(mock, startOnEnable: false);
            lifecycle.SetApplicationPaused(true);
            lifecycle.RequestStart();
            Assert.Equal(LocationProviderState.Stopped, mock.State);

            lifecycle.SetApplicationPaused(false);
            Assert.Equal(LocationProviderState.Running, mock.State);
        }

        [Fact]
        public void RepeatedPauseNotifications_AreIdempotent()
        {
            var (lifecycle, core, platform) = DeviceSetup();
            platform.UserChooses(fine: true, coarse: true);
            lifecycle.RequestStop();
            lifecycle.RequestStart();
            platform.ServiceStatus = DeviceLocationServiceStatus.Running;
            core.Tick();

            lifecycle.SetApplicationPaused(true);
            lifecycle.SetApplicationPaused(true);
            Assert.Equal(1, platform.StopServiceCount);
            lifecycle.SetApplicationPaused(false);
            lifecycle.SetApplicationPaused(false);
            Assert.Equal(2, platform.StartServiceCount);
        }

        [Fact]
        public void Device_PauseResumeActiveProvider_SuspendsAndResumesWithoutPrompt()
        {
            var platform = new FakeDeviceLocationPlatform();
            platform.UserChooses(fine: true, coarse: true);
            var core = new DeviceLocationProviderCore(platform, 20.0, 5f, 1f);
            var lifecycle = EnabledWith(core);
            platform.ServiceStatus = DeviceLocationServiceStatus.Running;
            core.Tick();
            Assert.True(core.TryGetLatestFix(out _));

            lifecycle.SetApplicationPaused(true);
            Assert.True(core.IsSuspended);
            Assert.False(core.TryGetLatestFix(out _));
            Assert.Equal(1, platform.StopServiceCount);

            lifecycle.SetApplicationPaused(false);
            Assert.False(core.IsSuspended);
            Assert.Equal(2, platform.StartServiceCount);
            Assert.Empty(platform.Requests);
            platform.ServiceStatus = DeviceLocationServiceStatus.Running;
            core.Tick();
            Assert.True(core.TryGetLatestFix(out _));
        }

        [Fact]
        public void Device_ManualStopThenResume_StaysStopped()
        {
            var platform = new FakeDeviceLocationPlatform();
            platform.UserChooses(fine: true, coarse: true);
            var core = new DeviceLocationProviderCore(platform, 20.0, 5f, 1f);
            var lifecycle = EnabledWith(core);
            lifecycle.RequestStop();
            lifecycle.SetApplicationPaused(true);
            lifecycle.SetApplicationPaused(false);
            core.Tick();

            Assert.Equal(LocationProviderState.Stopped, core.State);
            Assert.Equal(1, platform.StartServiceCount);
        }

        [Fact]
        public void Device_DisabledComponentThenResume_StaysStopped()
        {
            var platform = new FakeDeviceLocationPlatform();
            platform.UserChooses(fine: true, coarse: true);
            var core = new DeviceLocationProviderCore(platform, 20.0, 5f, 1f);
            var lifecycle = EnabledWith(core);
            lifecycle.SetApplicationPaused(true);
            lifecycle.SetComponentEnabled(false);
            lifecycle.SetApplicationPaused(false);
            core.Tick();

            Assert.Equal(LocationProviderState.Stopped, core.State);
            Assert.Equal(1, platform.StartServiceCount);
        }

        [Fact]
        public void Device_PermissionDialogPauseResumeCycles_OneRequest_OneStart()
        {
            var (lifecycle, core, platform) = DeviceSetup();
            Assert.Single(platform.Requests);

            // Dialog shows (pause), focus flickers (resume/pause), user answers while paused.
            lifecycle.SetApplicationPaused(true);
            lifecycle.SetApplicationPaused(false);
            core.Tick();
            lifecycle.SetApplicationPaused(true);
            platform.UserChooses(fine: false, coarse: true);
            platform.LastRequest.ReportDenied(Fine);
            platform.LastRequest.ReportGranted(Coarse);
            Assert.Equal(0, platform.StartServiceCount);
            lifecycle.SetApplicationPaused(false);
            core.Tick();
            core.Tick();

            Assert.Single(platform.Requests);
            Assert.Equal(1, platform.StartServiceCount);
            Assert.Equal(LocationPermissionLevel.Approximate, core.PermissionLevel);
        }

        [Fact]
        public void Device_DeniedPermission_NotRestartedOrRePromptedByResume()
        {
            var (lifecycle, core, platform) = DeviceSetup();
            lifecycle.SetApplicationPaused(true);
            platform.LastRequest.ReportDenied(Fine);
            platform.LastRequest.ReportDenied(Coarse);
            lifecycle.SetApplicationPaused(false);
            core.Tick();
            lifecycle.SetApplicationPaused(true);
            lifecycle.SetApplicationPaused(false);
            core.Tick();

            Assert.Equal(LocationProviderState.PermissionDenied, core.State);
            Assert.Single(platform.Requests);
            Assert.Equal(0, platform.StartServiceCount);
        }

        [Fact]
        public void Device_StopDuringDialog_LateGrant_NoStartOnResume()
        {
            var (lifecycle, core, platform) = DeviceSetup();
            lifecycle.SetApplicationPaused(true);
            lifecycle.RequestStop();
            platform.UserChooses(fine: true, coarse: true);
            platform.LastRequest.ReportGranted(Fine);
            platform.LastRequest.ReportGranted(Coarse);
            lifecycle.SetApplicationPaused(false);
            core.Tick();

            Assert.Equal(LocationProviderState.Stopped, core.State);
            Assert.Equal(0, platform.StartServiceCount);
            Assert.Single(platform.Requests);
        }

        [Fact]
        public void Device_ProviderAttachedWhilePaused_IsSuspended()
        {
            var lifecycle = new ForegroundLocationLifecycle();
            lifecycle.SetComponentEnabled(true);
            lifecycle.SetApplicationPaused(true);
            var platform = new FakeDeviceLocationPlatform();
            var core = new DeviceLocationProviderCore(platform, 20.0, 5f, 1f);
            lifecycle.AttachProvider(core);
            lifecycle.RequestStart();

            Assert.True(core.IsSuspended);
            Assert.Empty(platform.Requests);
            lifecycle.SetApplicationPaused(false);
            Assert.Single(platform.Requests);
        }
    }
}
