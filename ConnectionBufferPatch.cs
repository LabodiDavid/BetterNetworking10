using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DIT.BetterNetworking10
{
    internal static class ConnectionBufferPatch
    {
        private const int MaximumPacketsPerConnection = 256;
        private static readonly ConcurrentDictionary<ZRpc, List<ZPackage>> Buffers = new ConcurrentDictionary<ZRpc, List<ZPackage>>();
        private static readonly MethodInfo ReceiveZdoData = AccessTools.DeclaredMethod(typeof(ZDOMan), "RPC_ZDOData");

        internal static void OnNewConnectionPostfix(ZNet __instance, ZNetPeer peer)
        {
            if (__instance == null || peer?.m_rpc == null || __instance.IsServer()) return;
            List<ZPackage> buffer = Buffers.GetOrAdd(peer.m_rpc, _ => new List<ZPackage>());
            peer.m_rpc.Register<ZPackage>("ZDOData", (rpc, package) =>
            {
                lock (buffer)
                {
                    if (buffer.Count >= MaximumPacketsPerConnection)
                    {
                        BetterNetworking10.Log.Error("Connection ZDO buffer reached 256 packets; dropping additional early packets");
                        return;
                    }
                    buffer.Add(new ZPackage(package.GetArray()));
                }
            });
        }

        internal static void AddPeerPostfix(ZDOMan __instance, ZNetPeer netPeer)
        {
            if (__instance == null || netPeer?.m_rpc == null || ReceiveZdoData == null) return;
            if (!Buffers.TryRemove(netPeer.m_rpc, out List<ZPackage> buffer)) return;
            lock (buffer)
            {
                BetterNetworking10.Log.Warning($"Replaying {buffer.Count} early ZDOData packet(s) for one connection");
                foreach (ZPackage package in buffer) ReceiveZdoData.Invoke(__instance, new object[] { netPeer.m_rpc, package });
            }
        }

        internal static void ShutdownPostfix() => Clear();
        internal static void Clear() => Buffers.Clear();
    }
}

