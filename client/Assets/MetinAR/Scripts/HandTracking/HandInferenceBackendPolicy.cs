using System;
using System.Text;

namespace MetinAR.HandTracking
{
    /// <summary>Inference backend family chosen for the hand-landmark models (maps to Sentis BackendType at the call site).</summary>
    public enum HandInferenceBackend
    {
        Cpu = 0,
        GpuCompute = 1,
    }

    /// <summary>
    /// METINAR-007: pure backend choice + failure handling for on-device hand inference. GPU compute is used only when
    /// it is preferred, the device reports compute shaders, the graphics API is not OpenGL ES (unless explicitly allowed),
    /// and GPU has not already failed in this session. Any GPU failure falls back to CPU once and is never retried.
    /// </summary>
    public sealed class HandInferenceBackendPolicy
    {
        public HandInferenceBackendPolicy(bool preferGpu, bool allowGpuOnOpenGles)
        {
            PreferGpu = preferGpu;
            AllowGpuOnOpenGles = allowGpuOnOpenGles;
        }

        /// <summary>Recognises Galaxy A31 variants without any Unity dependency (testable on desktop).</summary>
        public static bool IsGalaxyA31(string deviceModel)
        {
            return !string.IsNullOrWhiteSpace(deviceModel)
                   && (deviceModel.IndexOf("SM-A315", StringComparison.OrdinalIgnoreCase) >= 0
                       || deviceModel.IndexOf("Galaxy A31", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public bool PreferGpu { get; }

        public bool AllowGpuOnOpenGles { get; }

        /// <summary>True after a GPU init/inference failure; GPU is not tried again this session.</summary>
        public bool GpuFailed { get; private set; }

        /// <summary>True when the CPU backend has failed too: tracking stays unavailable.</summary>
        public bool CpuFailed { get; private set; }

        public HandInferenceBackend Choose(bool supportsComputeShaders, bool isOpenGles)
        {
            bool gpu = PreferGpu && supportsComputeShaders && !GpuFailed && (!isOpenGles || AllowGpuOnOpenGles);
            return gpu ? HandInferenceBackend.GpuCompute : HandInferenceBackend.Cpu;
        }

        /// <summary>
        /// Records a failure of <paramref name="backend"/>. Returns true when the caller should dispose the current
        /// worker and recreate it on CPU (first GPU failure only); false when there is nothing left to fall back to.
        /// </summary>
        public bool ReportFailure(HandInferenceBackend backend)
        {
            if (backend == HandInferenceBackend.GpuCompute)
            {
                if (GpuFailed)
                {
                    return false;
                }

                GpuFailed = true;
                return true;
            }

            CpuFailed = true;
            return false;
        }

        /// <summary>Log throttle: log the 1st occurrence of an error and then every <paramref name="every"/>-th one.</summary>
        public static bool ShouldLogFailure(int failureCount, int every = 300)
        {
            return failureCount == 1 || (every > 0 && failureCount % every == 0);
        }

        /// <summary>One-line "Type: message --> InnerType: inner message ..." chain (no stack traces), max depth 8.</summary>
        public static string DescribeChain(Exception ex)
        {
            if (ex == null)
            {
                return "(null)";
            }

            var sb = new StringBuilder();
            int depth = 0;
            for (Exception e = ex; e != null && depth < 8; e = e.InnerException, depth++)
            {
                if (depth > 0)
                {
                    sb.Append(" --> ");
                }

                sb.Append(e.GetType().FullName).Append(": ").Append(e.Message);
            }

            return sb.ToString();
        }
    }
}
