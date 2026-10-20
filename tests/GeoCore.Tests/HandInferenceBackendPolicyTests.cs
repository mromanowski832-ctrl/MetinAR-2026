using MetinAR.HandTracking;
using Xunit;

namespace GeoCore.Tests;

/// <summary>METINAR-007: backend selection / GPU→CPU fallback / log throttle for on-device hand inference.</summary>
public class HandInferenceBackendPolicyTests
{
    [Theory]
    [InlineData("SM-A315F")]
    [InlineData("samsung SM-A315G")]
    [InlineData("SM-A315N")]
    [InlineData("Galaxy A31")]
    public void GalaxyA31Profile_RecognisesVariants(string model)
    {
        Assert.True(HandInferenceBackendPolicy.IsGalaxyA31(model));
    }

    [Theory]
    [InlineData("SM-A325F")]
    [InlineData("SM-A310F")]
    [InlineData("")]
    [InlineData(null)]
    public void GalaxyA31Profile_DoesNotSelectOtherDevices(string model)
    {
        Assert.False(HandInferenceBackendPolicy.IsGalaxyA31(model));
    }

    [Fact]
    public void OpenGles_DefaultsToCpu_EvenWithComputeShaders()
    {
        var p = new HandInferenceBackendPolicy(preferGpu: true, allowGpuOnOpenGles: false);
        Assert.Equal(HandInferenceBackend.Cpu, p.Choose(supportsComputeShaders: true, isOpenGles: true));
    }

    [Fact]
    public void Vulkan_WithCompute_UsesGpu()
    {
        var p = new HandInferenceBackendPolicy(true, false);
        Assert.Equal(HandInferenceBackend.GpuCompute, p.Choose(true, false));
    }

    [Fact]
    public void OpenGles_GpuAllowedExplicitly()
    {
        var p = new HandInferenceBackendPolicy(true, true);
        Assert.Equal(HandInferenceBackend.GpuCompute, p.Choose(true, true));
    }

    [Fact]
    public void NoComputeOrNotPreferred_UsesCpu()
    {
        Assert.Equal(HandInferenceBackend.Cpu, new HandInferenceBackendPolicy(true, true).Choose(false, false));
        Assert.Equal(HandInferenceBackend.Cpu, new HandInferenceBackendPolicy(false, true).Choose(true, false));
    }

    [Fact]
    public void GpuFailure_FallsBackOnce_AndNeverRetriesGpu()
    {
        var p = new HandInferenceBackendPolicy(true, false);
        Assert.True(p.ReportFailure(HandInferenceBackend.GpuCompute));
        Assert.True(p.GpuFailed);
        Assert.Equal(HandInferenceBackend.Cpu, p.Choose(true, false));
        Assert.False(p.ReportFailure(HandInferenceBackend.GpuCompute));
    }

    [Fact]
    public void CpuFailure_HasNoFallback()
    {
        var p = new HandInferenceBackendPolicy(true, false);
        Assert.False(p.ReportFailure(HandInferenceBackend.Cpu));
        Assert.True(p.CpuFailed);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(299, false)]
    [InlineData(300, true)]
    [InlineData(600, true)]
    public void FailureLogging_IsThrottled(int count, bool expected)
    {
        Assert.Equal(expected, HandInferenceBackendPolicy.ShouldLogFailure(count));
    }

    [Fact]
    public void DescribeChain_IncludesInnerExceptions()
    {
        var ex = new System.TypeInitializationException("Unity.InferenceEngine.ComputeFunctions",
            new System.InvalidOperationException("kernel missing"));
        string s = HandInferenceBackendPolicy.DescribeChain(ex);
        Assert.Contains("System.TypeInitializationException", s);
        Assert.Contains(" --> System.InvalidOperationException: kernel missing", s);
        Assert.Equal("(null)", HandInferenceBackendPolicy.DescribeChain(null));
    }
}
