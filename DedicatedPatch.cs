using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DIT.BetterNetworking10
{
    internal static class DedicatedPatch
    {
        internal static void ParseArgumentsPostfix()
        {
            if (!BetterNetworking10.IsDedicated) return;
            switch (BetterNetworking10.ForceCrossplay.Value)
            {
                case ForceCrossplayOption.PlayFab:
                    ZNet.m_onlineBackend = OnlineBackendType.PlayFab;
                    ZPlayFabMatchmaking.LookupPublicIP();
                    break;
                case ForceCrossplayOption.Steamworks:
                    ZNet.m_onlineBackend = OnlineBackendType.Steamworks;
                    break;
            }
        }

        internal static int GetPlayerLimit() => BetterNetworking10.PlayerLimit.Value;

        internal static IEnumerable<CodeInstruction> PlayerLimitTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> source = instructions.ToList();
            MethodInfo getPlayers = AccessTools.DeclaredMethod(typeof(ZNet), "GetNrOfPlayers");
            MethodInfo getLimit = AccessTools.DeclaredMethod(typeof(DedicatedPatch), nameof(GetPlayerLimit));
            int match = -1;

            for (int i = 1; i < source.Count; i++)
            {
                bool isTen = source[i].Is(OpCodes.Ldc_I4_S, (sbyte)10) || source[i].Is(OpCodes.Ldc_I4, 10);
                if (isTen && source[i - 1].Calls(getPlayers))
                {
                    if (match != -1)
                    {
                        BetterNetworking10.Log.Error("Player-limit transpiler found multiple guard constants; leaving RPC_PeerInfo unmodified");
                        return source;
                    }
                    match = i;
                }
            }

            if (match == -1)
            {
                BetterNetworking10.Log.Error("Player-limit transpiler could not identify the 1.0 server-full guard; leaving RPC_PeerInfo unmodified");
                return source;
            }

            source[match].opcode = OpCodes.Call;
            source[match].operand = getLimit;
            BetterNetworking10.Log.Message($"Player-limit transpiler verified 1/1 IL match; limit={BetterNetworking10.PlayerLimit.Value}");
            return source;
        }
    }
}
