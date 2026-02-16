using System;

namespace Eitan.EasyMic.Runtime
{
    public sealed partial class MicSystem
    {
        internal enum LogLevel
        {
            Info,
            Warning,
            Error,
        }
        private ILogger _logger;

        internal void SetLogger(ILogger logger)
        {
            this._logger = logger;
        }

        internal void Log(string message, LogLevel type)
        {
            if (this._logger == null)
            {
                this._logger = new Mono.UnityLogger();
                // throw new NullReferenceException("MicSystem has no logger set up. Set a logger first.");
            }
            switch (type)
            {
                case LogLevel.Info:
                    _logger.LogInfo(message);
                    break;
                case LogLevel.Warning:
                    _logger.LogWarning(message);
                    break;
                case LogLevel.Error:
                    _logger.LogError(message);
                    break;
                default:
                    throw new System.Exception($"{type} logtype not support.");
            }
        }
    }
}