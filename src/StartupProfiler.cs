using System.Collections;
using System.Diagnostics;

namespace NewBeginnings
{
    // Separates New Beginnings' own new-game CPU from whole-frame stalls,
    // which also include other mods and native loading. Diagnostic only.
    internal static class StartupProfiler
    {
        private const float HitchSeconds = .25f, ReportSeconds = 1f, WindowSeconds = 120f, IdleSeconds = 5f;
        private static float startedAt = -1f, lastFrameAt, hitchTotal, longest, longestAt, idleSince = -1f;
        private static string longestStage;
        private static long ownTicks, frameTicks, longestOwnTicks;
        private static int frames, hitches, lastFrame, depth;

        internal static bool Active => startedAt >= 0f;

        internal static void Begin(float now, int frame)
        {
            startedAt = lastFrameAt = now; lastFrame = frame; idleSince = -1f;
            hitchTotal = longest = longestAt = 0f; longestStage = null;
            ownTicks = frameTicks = longestOwnTicks = 0; frames = hitches = 0;
        }

        internal static void Cancel() => startedAt = -1f;

        // Only the outermost measured call counts, so a coroutine started
        // inside a measured Update is not added twice.
        internal static long Enter() => depth++ == 0 && Active ? Stopwatch.GetTimestamp() : 0L;

        internal static void Exit(long started)
        {
            if (depth > 0) --depth;
            if (started == 0L || !Active) return;
            var elapsed = Stopwatch.GetTimestamp() - started;
            ownTicks += elapsed; frameTicks += elapsed;
        }

        internal static IEnumerator Measure(IEnumerator inner)
        {
            while (true)
            {
                var started = Enter();
                bool moved;
                try { moved = inner.MoveNext(); }
                finally { Exit(started); }
                if (!moved) yield break;
                yield return inner.Current;
            }
        }

        // Called once at the start of each frame. It measures the previous one.
        internal static void Tick(float now, int frame, bool loading, bool playing, bool suppliesArmed, string stage)
        {
            if (!Active || frame == lastFrame) return;
            if (loading) { Cancel(); return; }
            var duration = now - lastFrameAt;
            ++frames;
            if (frameTicks > longestOwnTicks) longestOwnTicks = frameTicks;
            if (duration >= HitchSeconds)
            {
                ++hitches; hitchTotal += duration;
                if (duration > longest) { longest = duration; longestAt = lastFrameAt - startedAt; longestStage = stage; }
                if (duration >= ReportSeconds)
                    Plugin.Instance?.Report($"Startup frame took {duration:F1} s at +{lastFrameAt - startedAt:F1} s ({stage}). " +
                        $"New Beginnings used {Milliseconds(frameTicks):F0} ms of it.");
            }
            frameTicks = 0L; lastFrame = frame; lastFrameAt = now;
            idleSince = playing && !suppliesArmed ? (idleSince < 0f ? now : idleSince) : -1f;
            if (idleSince >= 0f && now - idleSince >= IdleSeconds) Finish(now, "gameplay reached");
            else if (now - startedAt >= WindowSeconds) Finish(now, "two-minute window ended");
        }

        internal static void Finish(float now, string outcome)
        {
            if (!Active) return;
            Plugin.Instance?.Report($"Startup profile ({outcome}): {now - startedAt:F1} s since start selection, {frames} frames, " +
                $"{hitches} over {HitchSeconds * 1000f:F0} ms totalling {hitchTotal:F1} s" +
                (hitches == 0 ? "" : $", longest {longest:F1} s at +{longestAt:F1} s ({longestStage})") +
                $". New Beginnings used {Milliseconds(ownTicks):F0} ms CPU, at most {Milliseconds(longestOwnTicks):F0} ms in one frame.");
            Cancel();
        }

        private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }
}
