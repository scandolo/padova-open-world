#if DEVELOPMENT_BUILD
using UnityEngine;

namespace Padova.World
{
    /// <summary>Agent test runs launch the Development Player hidden, where macOS may never present a frame.
    /// With vertical sync on, Unity's Metal back end then waits for that presentation and the game stalls for
    /// tens of seconds. A run started by the test harness (it always passes -logFile) paces itself by the clock instead.
    /// Normal launches are untouched.</summary>
    static class QaPacing
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            foreach (var arg in System.Environment.GetCommandLineArgs())
                if (arg == "-logFile") { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; return; }
        }
    }
}
#endif
