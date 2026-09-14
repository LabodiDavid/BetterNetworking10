using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;

namespace DIT.BetterNetworking10
{
    internal static class LegacyConfigMigration
    {
        private const string LegacyFileName = "CW_Jesse.BetterNetworking.cfg";

        internal static void TryImport(ConfigFile config, BNLog log)
        {
            string legacyPath = Path.Combine(Paths.ConfigPath, LegacyFileName);
            if (!File.Exists(legacyPath))
                return;

            try
            {
                Dictionary<string, string> values = ReadValues(legacyPath);
                int imported = 0;

                if (TryGet(values, "Networking", "Compression Enabled", out string compression) &&
                    TryParseBoolean(compression, out bool compressionEnabled))
                {
                    BetterNetworking10.EnableCompression.Value = compressionEnabled;
                    imported++;
                }

                if (TryGet(values, "Networking", "Queue Size", out string queue) &&
                    TryParseQueueSize(queue, out QueueSizeOption queueSize))
                {
                    BetterNetworking10.EnableQueueSize.Value = true;
                    BetterNetworking10.QueueSize.Value = queueSize;
                    imported++;
                }

                if (TryGet(values, "Networking", "Update Rate", out string updateRate) &&
                    TryParseUpdateRate(updateRate, out UpdateRateOption rate))
                {
                    BetterNetworking10.EnableUpdateRate.Value = true;
                    BetterNetworking10.UpdateRate.Value = rate;
                    imported++;
                }

                if (TryGet(values, "Networking (Steamworks)", "Minimum Send Rate", out string minimumRate) &&
                    TryParseSendRate(minimumRate, out SendRateOption minimum))
                {
                    BetterNetworking10.EnableSendRate.Value = true;
                    BetterNetworking10.MinimumSendRate.Value = minimum;
                    imported++;
                }

                if (TryGet(values, "Networking (Steamworks)", "Maximum Send Rate", out string maximumRate) &&
                    TryParseSendRate(maximumRate, out SendRateOption maximum))
                {
                    BetterNetworking10.EnableSendRate.Value = true;
                    BetterNetworking10.MaximumSendRate.Value = maximum;
                    imported++;
                }

                if (TryGet(values, "Dedicated Server", "Force Crossplay", out string crossplay) &&
                    TryParseCrossplay(crossplay, out ForceCrossplayOption forceCrossplay))
                {
                    BetterNetworking10.ForceCrossplay.Value = forceCrossplay;
                    imported++;
                }

                if (TryGet(values, "Dedicated Server", "Player Limit", out string playerLimit) &&
                    int.TryParse(playerLimit.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit))
                {
                    BetterNetworking10.PlayerLimit.Value = Math.Max(1, Math.Min(127, limit));
                    imported++;
                }

                if (TryGet(values, "Logging", "Log Level", out string logLevel) &&
                    TryParseLogLevel(logLevel, out BNLog.LogLevel level))
                {
                    config.Bind("99 - Logging", "Log Level", BNLog.LogLevel.Message).Value = level;
                    imported++;
                }

                // The legacy connection buffer had no safe user-facing switch. It is intentionally
                // not imported and remains disabled in the new configuration.
                BetterNetworking10.EnableConnectionBuffer.Value = false;
                config.Save();

                log.Message($"Imported {imported} setting(s) from legacy config {LegacyFileName}. The old file was left unchanged.");
            }
            catch (Exception ex)
            {
                log.Warning($"Legacy config import skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static Dictionary<string, string> ReadValues(string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = string.Empty;

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                values[MakeKey(section, key)] = value;
            }

            return values;
        }

        private static bool TryGet(Dictionary<string, string> values, string section, string key, out string value) =>
            values.TryGetValue(MakeKey(section, key), out value);

        private static string MakeKey(string section, string key) => section + "\n" + key;

        private static string Normalize(string value) =>
            new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        private static bool TryParseBoolean(string value, out bool result)
        {
            string normalized = Normalize(value);
            if (normalized == "true" || normalized == "enabled")
            {
                result = true;
                return true;
            }

            if (normalized == "false" || normalized == "disabled")
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static bool TryParseQueueSize(string value, out QueueSizeOption result)
        {
            string normalized = Normalize(value);
            if (normalized.Contains("80")) result = QueueSizeOption.KB80;
            else if (normalized.Contains("64")) result = QueueSizeOption.KB64;
            else if (normalized.Contains("48")) result = QueueSizeOption.KB48;
            else if (normalized.Contains("32")) result = QueueSizeOption.KB32;
            else if (normalized.Contains("vanilla") || normalized.Contains("10")) result = QueueSizeOption.Vanilla;
            else { result = default; return false; }
            return true;
        }

        private static bool TryParseUpdateRate(string value, out UpdateRateOption result)
        {
            string normalized = Normalize(value);
            if (normalized.Contains("100")) result = UpdateRateOption.Percent100;
            else if (normalized.Contains("75")) result = UpdateRateOption.Percent75;
            else if (normalized.Contains("50")) result = UpdateRateOption.Percent50;
            else { result = default; return false; }
            return true;
        }

        private static bool TryParseSendRate(string value, out SendRateOption result)
        {
            string normalized = Normalize(value);
            if (normalized.Contains("1024")) result = SendRateOption.KB1024;
            else if (normalized.Contains("768")) result = SendRateOption.KB768;
            else if (normalized.Contains("512")) result = SendRateOption.KB512;
            else if (normalized.Contains("256")) result = SendRateOption.KB256;
            else if (normalized.Contains("150")) result = SendRateOption.KB150;
            else { result = default; return false; }
            return true;
        }

        private static bool TryParseCrossplay(string value, out ForceCrossplayOption result)
        {
            string normalized = Normalize(value);
            if (normalized.Contains("playfab") || normalized.Contains("enabled")) result = ForceCrossplayOption.PlayFab;
            else if (normalized.Contains("steamworks") || normalized.Contains("disabled")) result = ForceCrossplayOption.Steamworks;
            else if (normalized.Contains("vanilla")) result = ForceCrossplayOption.Vanilla;
            else { result = default; return false; }
            return true;
        }

        private static bool TryParseLogLevel(string value, out BNLog.LogLevel result)
        {
            string normalized = Normalize(value);
            if (normalized.Contains("info")) result = BNLog.LogLevel.Info;
            else if (normalized.Contains("message")) result = BNLog.LogLevel.Message;
            else if (normalized.Contains("warning")) result = BNLog.LogLevel.Warning;
            else { result = default; return false; }
            return true;
        }
    }
}
