using BepInEx.Configuration;
using BepInEx.Logging;

namespace DIT.BetterNetworking10
{
    internal sealed class BNLog
    {
        internal enum LogLevel { Warning, Message, Info }

        private readonly ManualLogSource _logger;
        private readonly ConfigEntry<LogLevel> _level;

        internal BNLog(ManualLogSource logger, ConfigFile config)
        {
            _logger = logger;
            _level = config.Bind("99 - Logging", "Log Level", LogLevel.Message,
                "Warning, Message, or Info (verbose packet compression logging). ");
        }

        internal void Error(object value) => _logger.LogError(value);
        internal void Warning(object value) => _logger.LogWarning(value);
        internal void Message(object value)
        {
            if (_level.Value >= LogLevel.Message) _logger.LogMessage(value);
        }
        internal void Info(object value)
        {
            if (_level.Value >= LogLevel.Info) _logger.LogInfo(value);
        }
        internal bool IsInfo => _level.Value >= LogLevel.Info;
    }
}

