using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Steamworks;

namespace DIT.BetterNetworking10
{
    internal static class QueueBudgetPatch
    {
        internal static int GetBudget() => BetterNetworking10.QueueActive
            ? BetterNetworking10.QueueBudgetBytes
            : 10 * 1024;

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> source = instructions.ToList();
            int matches = source.Count(i => i.Is(OpCodes.Ldc_I4, 10240));
            if (matches != 2)
            {
                BetterNetworking10.Log.Error($"Queue transpiler expected exactly 2 vanilla 10240 constants but found {matches}; leaving SendZDOs unmodified");
                return source;
            }

            MethodInfo getter = AccessTools.DeclaredMethod(typeof(QueueBudgetPatch), nameof(GetBudget));
            foreach (CodeInstruction instruction in source)
            {
                if (!instruction.Is(OpCodes.Ldc_I4, 10240)) continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = getter;
            }
            BetterNetworking10.Log.Message($"Queue transpiler verified 2/2 IL matches; budget={BetterNetworking10.QueueBudgetBytes / 1024} KB");
            return source;
        }
    }

    internal static class UpdateRatePatch
    {
        internal static void Prefix(ref float dt)
        {
            if (BetterNetworking10.UpdateRateActive)
                dt *= BetterNetworking10.UpdateRateMultiplier;
        }
    }

    internal static class SteamSendRatePatch
    {
        private static bool _callbacksRegistered;
        private static bool _originalValuesCaptured;
        private static bool _customValuesApplied;
        private static int _originalMinimum;
        private static int _originalMaximum;

        internal static void Postfix()
        {
            _callbacksRegistered = true;
            CaptureOriginalValues();
            ApplyCurrent();
        }

        internal static void Refresh()
        {
            if (_callbacksRegistered)
                ApplyCurrent();
        }

        internal static void Clear()
        {
            if (_callbacksRegistered && _customValuesApplied && _originalValuesCaptured)
            {
                Set(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, _originalMinimum, "restored");
                Set(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, _originalMaximum, "restored");
            }

            _callbacksRegistered = false;
            _originalValuesCaptured = false;
            _customValuesApplied = false;
            _originalMinimum = 0;
            _originalMaximum = 0;
        }

        private static void ApplyCurrent()
        {
            if (!BetterNetworking10.SendRateActive)
            {
                if (_customValuesApplied && _originalValuesCaptured)
                {
                    Set(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, _originalMinimum, "restored");
                    Set(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, _originalMaximum, "restored");
                }

                _customValuesApplied = false;
                return;
            }

            int min = BetterNetworking10.RateBytesPerSecond(BetterNetworking10.MinimumSendRate.Value);
            int max = BetterNetworking10.RateBytesPerSecond(BetterNetworking10.MaximumSendRate.Value);
            if (min > max)
            {
                int swap = min;
                min = max;
                max = swap;
                BetterNetworking10.Log.Warning("Steam send-rate minimum exceeded maximum; values were swapped at runtime");
            }
            Set(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, min, "configured");
            Set(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, max, "configured");
            _customValuesApplied = true;
        }

        private static void CaptureOriginalValues()
        {
            if (_originalValuesCaptured)
                return;

            if (!TryGet(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, out _originalMinimum) ||
                !TryGet(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, out _originalMaximum))
            {
                BetterNetworking10.Log.Warning("Could not capture the original Steamworks send-rate limits; live disabling will leave the latest configured values in place until restart");
                return;
            }

            _originalValuesCaptured = true;
        }

        private static bool TryGet(ESteamNetworkingConfigValue key, out int value)
        {
            value = 0;
            ulong valueSize = sizeof(int);
            byte[] valueBuffer = new byte[sizeof(int)];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(valueBuffer, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                ESteamNetworkingConfigDataType dataType;
                ESteamNetworkingGetConfigValueResult result = BetterNetworking10.IsDedicated
                    ? SteamGameServerNetworkingUtils.GetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, out dataType, handle.AddrOfPinnedObject(), ref valueSize)
                    : SteamNetworkingUtils.GetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, out dataType, handle.AddrOfPinnedObject(), ref valueSize);

                if ((result != ESteamNetworkingGetConfigValueResult.k_ESteamNetworkingGetConfigValue_OK &&
                     result != ESteamNetworkingGetConfigValueResult.k_ESteamNetworkingGetConfigValue_OKInherited) ||
                    dataType != ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32 || valueSize < sizeof(int))
                    return false;

                value = BitConverter.ToInt32(valueBuffer, 0);
                return true;
            }
            catch (Exception ex)
            {
                BetterNetworking10.Log.Warning($"Could not read Steamworks {key}: {ex.Message}");
                return false;
            }
            finally
            {
                handle.Free();
            }
        }

        private static void Set(ESteamNetworkingConfigValue key, int value, string action)
        {
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(value, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                if (BetterNetworking10.IsDedicated)
                    SteamGameServerNetworkingUtils.SetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, handle.AddrOfPinnedObject());
                else
                    SteamNetworkingUtils.SetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, handle.AddrOfPinnedObject());
                BetterNetworking10.Log.Message($"Steamworks {key} {action}: {value / 1024} KB/s");
            }
            catch (Exception ex)
            {
                BetterNetworking10.Log.Error($"Could not set Steamworks {key}: {ex.Message}");
            }
            finally { handle.Free(); }
        }
    }
}
