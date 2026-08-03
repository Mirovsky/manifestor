namespace Manifestor.Build
{
    using System;
    using UnityEditor;

    internal static class ManifestorBuildScheduler
    {
        private static Action _tick;
        private static bool _isQueued;

        public static void Initialize(Action tick)
        {
            _tick = tick;
        }

        public static void Queue()
        {
            if (_isQueued)
            {
                return;
            }

            _isQueued = true;
            EditorApplication.update += Process;
        }

        public static void Stop()
        {
            if (!_isQueued)
            {
                return;
            }

            EditorApplication.update -= Process;
            _isQueued = false;
        }

        private static void Process()
        {
            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                BuildPipeline.isBuildingPlayer)
            {
                return;
            }

            _tick?.Invoke();
        }
    }
}
