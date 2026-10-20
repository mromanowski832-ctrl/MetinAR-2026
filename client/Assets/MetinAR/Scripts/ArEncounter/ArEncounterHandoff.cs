namespace MetinAR.ArEncounter
{
    /// <summary>
    /// In-process handoff of the pending <see cref="ArEncounterRequest"/> from the world-map scene to
    /// the AR encounter scene (prototype; no backend, no persistence). Pure C#.
    /// </summary>
    public static class ArEncounterHandoff
    {
        private static ArEncounterRequest _pending;

        /// <summary>Number of requests submitted since the last <see cref="Reset"/> (diagnostics/tests).</summary>
        public static int SubmittedCount { get; private set; }

        public static bool HasPending => _pending != null;

        public static ArEncounterRequest Pending => _pending;

        /// <summary>Stores the request for the AR scene. A newer request replaces an unconsumed older one.</summary>
        public static void Submit(ArEncounterRequest request)
        {
            if (request == null)
            {
                throw new System.ArgumentNullException(nameof(request));
            }

            _pending = request;
            SubmittedCount++;
        }

        /// <summary>Takes the pending request (once). Returns false when nothing is pending.</summary>
        public static bool TryConsume(out ArEncounterRequest request)
        {
            request = _pending;
            _pending = null;
            return request != null;
        }

        public static void Clear()
        {
            _pending = null;
        }

        /// <summary>Clears the pending request and the counter.</summary>
        public static void Reset()
        {
            _pending = null;
            SubmittedCount = 0;
        }
    }
}
