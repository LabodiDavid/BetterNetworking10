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
        internal static int GetBudget() => BetterNetworking10.QueueBudgetBytes;

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
        internal static void Prefix(ref float dt) => dt *= BetterNetworking10.UpdateRateMultiplier;
    }

    internal static class SteamSendRatePatch
    {
        internal static void Postfix()
        {
            int min = BetterNetworking10.RateBytesPerSecond(BetterNetworking10.MinimumSendRate.Value);
            int max = BetterNetworking10.RateBytesPerSecond(BetterNetworking10.MaximumSendRate.Value);
            if (min > max)
            {
                int swap = min;
                min = max;
                max = swap;
                BetterNetworking10.Log.Warning("Steam send-rate minimum exceeded maximum; values were swapped at runtime");
            }
            Set(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, min);
            Set(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, max);
        }

        private static void Set(ESteamNetworkingConfigValue key, int value)
        {
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(value, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                if (BetterNetworking10.IsDedicated)
                    SteamGameServerNetworkingUtils.SetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, handle.AddrOfPinnedObject());
                else
                    SteamNetworkingUtils.SetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, handle.AddrOfPinnedObject());
                BetterNetworking10.Log.Message($"Steamworks {key} set to {value / 1024} KB/s");
            }
            catch (Exception ex)
            {
                BetterNetworking10.Log.Error($"Could not set Steamworks {key}: {ex.Message}");
            }
            finally { handle.Free(); }
        }
    }
}
