using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Ionic.Zlib;
using ZstdSharp;

namespace DIT.BetterNetworking10
{
    internal static class CompressionCodec
    {
        private const string DictionaryResource = "DIT.BetterNetworking10.dict.small";
        private static Compressor _compressor;
        private static Decompressor _decompressor;
        private static readonly object Sync = new object();

        internal static void Initialize()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DictionaryResource))
            {
                if (stream == null) throw new InvalidOperationException("Embedded Zstd dictionary is missing");
                byte[] dictionary = new byte[stream.Length];
                int read = 0;
                while (read < dictionary.Length)
                {
                    int current = stream.Read(dictionary, read, dictionary.Length - read);
                    if (current == 0) throw new EndOfStreamException("Could not read embedded Zstd dictionary");
                    read += current;
                }

                _compressor = new Compressor(1);
                _compressor.LoadDictionary(dictionary);
                _decompressor = new Decompressor();
                _decompressor.LoadDictionary(dictionary);
            }
        }

        internal static byte[] Compress(byte[] input)
        {
            byte[] output;
            lock (Sync) output = _compressor.Wrap(input).ToArray();
            return output;
        }

        internal static byte[] Decompress(byte[] input)
        {
            byte[] output;
            lock (Sync) output = _decompressor.Unwrap(input).ToArray();
            return output;
        }
    }

    internal static class CompressionFrame
    {
        private static readonly byte[] Magic = { 0x42, 0x4E, 0x31, 0x30 }; // BN10
        private const int HeaderSize = 5;
        private const byte RawPayload = 0;
        private const byte CompressedPayload = 1;

        internal static byte[] Encode(byte[] input)
        {
            return Encode(input, true);
        }

        private static byte[] Encode(byte[] input, bool diagnostics)
        {
            if (HasHeader(input))
            {
                if (diagnostics)
                    BetterNetworking10.Log.Warning("Prevented duplicate Better Networking compression frame");
                return input;
            }

            byte[] compressed = CompressionCodec.Compress(input);
            bool useCompressed = compressed.Length < input.Length;
            byte[] payload = useCompressed ? compressed : input;
            byte[] framed = new byte[HeaderSize + payload.Length];
            Buffer.BlockCopy(Magic, 0, framed, 0, Magic.Length);
            framed[Magic.Length] = useCompressed ? CompressedPayload : RawPayload;
            Buffer.BlockCopy(payload, 0, framed, HeaderSize, payload.Length);

            if (diagnostics && BetterNetworking10.Log.IsInfo && input.Length > 256)
            {
                if (useCompressed)
                    BetterNetworking10.Log.Info($"Compression sent {input.Length} B -> {framed.Length} B ({100f * framed.Length / input.Length:0}%, framed)");
                else
                    BetterNetworking10.Log.Info($"Compression bypassed for {input.Length} B packet because it would grow ({framed.Length} B framed)");
            }

            return framed;
        }

        internal static bool TryDecode(byte[] input, out byte[] output)
        {
            output = null;
            if (!HasHeader(input)) return false;

            int payloadLength = input.Length - HeaderSize;
            byte[] payload = new byte[payloadLength];
            Buffer.BlockCopy(input, HeaderSize, payload, 0, payloadLength);

            byte type = input[Magic.Length];
            if (type == RawPayload)
            {
                output = payload;
            }
            else if (type == CompressedPayload)
            {
                output = CompressionCodec.Decompress(payload);
            }
            else
            {
                throw new InvalidDataException($"Unknown Better Networking frame type {type}");
            }

            if (BetterNetworking10.Log.IsInfo && output.Length > 256)
                BetterNetworking10.Log.Info($"Compression received {input.Length} B -> {output.Length} B (framed)");
            return true;
        }

        internal static bool HasHeader(byte[] input)
        {
            if (input == null || input.Length < HeaderSize) return false;
            for (int i = 0; i < Magic.Length; i++)
                if (input[i] != Magic[i]) return false;
            return true;
        }

        internal static void SelfTest()
        {
            byte[] original = Enumerable.Repeat((byte)0x5A, 1024).ToArray();
            byte[] framed = Encode(original);
            if (!TryDecode(framed, out byte[] decoded) || !original.SequenceEqual(decoded))
                throw new InvalidDataException("Compression frame round-trip failed");
            if (!ReferenceEquals(framed, Encode(framed, false)))
                throw new InvalidDataException("Compression frame duplicate guard failed");
        }
    }

    internal static class CompressionState
    {
        internal const int ProtocolVersion = 8;
        private static readonly ConcurrentDictionary<ISocket, PeerState> Peers = new ConcurrentDictionary<ISocket, PeerState>();

        internal sealed class PeerState
        {
            internal int Version;
            internal bool Enabled;
            internal volatile bool Sending;
            internal volatile bool Receiving;
            internal volatile bool WarnedUnframed;
        }

        internal static void Add(ISocket socket)
        {
            if (socket == null) return;
            Peers[socket] = new PeerState();
        }

        internal static void Remove(ISocket socket)
        {
            if (socket != null) Peers.TryRemove(socket, out _);
        }

        internal static bool TryGet(ISocket socket, out PeerState state)
        {
            state = null;
            return socket != null && Peers.TryGetValue(socket, out state);
        }
        internal static void Clear() => Peers.Clear();
    }

    internal static class PeerLookup
    {
        internal static ZNetPeer ByRpc(ZRpc rpc)
        {
            if (rpc == null || ZNet.instance == null) return null;
            return ZNet.instance.GetPeers().FirstOrDefault(peer => peer.m_rpc == rpc);
        }

        internal static string Name(ZNetPeer peer)
        {
            if (peer == null) return "[unknown]";
            if (peer.m_server) return "[server]";
            string host = peer.m_socket == null ? "?" : peer.m_socket.GetHostName();
            return $"{peer.m_playerName}[{host}]";
        }
    }

    internal static class CompressionPatch
    {
        private const string RpcVersion = "CW_Jesse.BetterNetworking.CompressionVersion";
        private const string RpcEnabled = "CW_Jesse.BetterNetworking.CompressionEnabled";
        private const string RpcStarted = "CW_Jesse.BetterNetworking.CompressedStarted";

        internal static void OnConnectPostfix(ZNetPeer peer)
        {
            if (peer?.m_socket == null || peer.m_rpc == null) return;
            CompressionState.Add(peer.m_socket);
            peer.m_rpc.Register<int>(RpcVersion, ReceiveVersion);
            peer.m_rpc.Register<bool>(RpcEnabled, ReceiveEnabled);
            peer.m_rpc.Register<bool>(RpcStarted, ReceiveStarted);
            peer.m_rpc.Invoke(RpcVersion, new object[] { CompressionState.ProtocolVersion });
            BetterNetworking10.Log.Message($"Compression peer connected: {PeerLookup.Name(peer)}");
        }

        internal static void OnDisconnectPrefix(ZNetPeer peer)
        {
            BetterNetworking10.Log.Message($"Compression peer disconnected: {PeerLookup.Name(peer)}");
        }

        internal static void OnDisconnectPostfix(ZNetPeer peer)
        {
            CompressionState.Remove(peer?.m_socket);
        }

        private static void ReceiveVersion(ZRpc rpc, int version)
        {
            ZNetPeer peer = PeerLookup.ByRpc(rpc);
            if (peer == null || !CompressionState.TryGet(peer.m_socket, out CompressionState.PeerState state)) return;
            state.Version = version;
            if (version == CompressionState.ProtocolVersion)
            {
                BetterNetworking10.Log.Message($"Compression compatible with {PeerLookup.Name(peer)} (protocol {version})");
                SendEnabled(peer);
            }
            else if (version > 0)
            {
                BetterNetworking10.Log.Warning($"Compression disabled with {PeerLookup.Name(peer)}: protocol {version}, expected {CompressionState.ProtocolVersion}");
            }
        }

        private static void ReceiveEnabled(ZRpc rpc, bool enabled)
        {
            ZNetPeer peer = PeerLookup.ByRpc(rpc);
            if (peer == null || !CompressionState.TryGet(peer.m_socket, out CompressionState.PeerState state)) return;
            state.Enabled = enabled;
            SendStarted(peer, BetterNetworking10.CompressionActive && enabled && state.Version == CompressionState.ProtocolVersion);
        }

        private static void ReceiveStarted(ZRpc rpc, bool started)
        {
            ZNetPeer peer = PeerLookup.ByRpc(rpc);
            if (peer == null || !CompressionState.TryGet(peer.m_socket, out CompressionState.PeerState state)) return;
            state.Receiving = started;
            state.WarnedUnframed = false;
            BetterNetworking10.Log.Message($"Compression from {PeerLookup.Name(peer)}: {started}");
        }

        private static void SendEnabled(ZNetPeer peer)
        {
            peer.m_rpc.Invoke(RpcEnabled, new object[] { BetterNetworking10.CompressionActive });
            if (CompressionState.TryGet(peer.m_socket, out CompressionState.PeerState state))
                SendStarted(peer, BetterNetworking10.CompressionActive && state.Enabled && state.Version == CompressionState.ProtocolVersion);
        }

        private static void SendStarted(ZNetPeer peer, bool started)
        {
            if (!CompressionState.TryGet(peer.m_socket, out CompressionState.PeerState state) || state.Sending == started) return;
            peer.m_rpc.Invoke(RpcStarted, new object[] { started });
            Flush(peer.m_socket);
            state.Sending = started;
            BetterNetworking10.Log.Message($"Compression to {PeerLookup.Name(peer)}: {started}");
        }

        private static void Flush(ISocket socket)
        {
            if (socket is ZSteamSocket)
            {
                socket.Flush();
                return;
            }
            if (socket is ZPlayFabSocket playFab) PlayFabCompressionPatch.Flush(playFab);
        }
    }

    internal static class SteamCompressionPatch
    {
        internal static void SendPrefix(ZSteamSocket __instance, ref ZPackage pkg)
        {
            if (pkg == null || !CompressionState.TryGet(__instance, out CompressionState.PeerState state) || !state.Sending) return;
            pkg = new ZPackage(CompressionFrame.Encode(pkg.GetArray()));
        }

        internal static void ReceivePostfix(ZSteamSocket __instance, ref ZPackage __result)
        {
            if (__result == null || !CompressionState.TryGet(__instance, out CompressionState.PeerState state)) return;
            byte[] packet = __result.GetArray();
            try
            {
                if (CompressionFrame.TryDecode(packet, out byte[] decoded))
                {
                    __result = new ZPackage(decoded);
                    state.WarnedUnframed = false;
                    if (!state.Receiving)
                    {
                        state.Receiving = true;
                        BetterNetworking10.Log.Warning("Received framed Steamworks packet before compression-start state; recovered automatically");
                    }
                }
                else if (state.Receiving && !state.WarnedUnframed)
                {
                    state.WarnedUnframed = true;
                    BetterNetworking10.Log.Warning("Received an unframed Steamworks packet while compression was active; packet preserved");
                }
            }
            catch (Exception ex)
            {
                BetterNetworking10.Log.Error($"Invalid Better Networking Steamworks frame dropped: {ex.Message}");
                __result = null;
            }
        }
    }

    internal static class PlayFabCompressionPatch
    {
        private static readonly ConcurrentDictionary<PlayFabZLibWorkQueue, ZPlayFabSocket> Sockets = new ConcurrentDictionary<PlayFabZLibWorkQueue, ZPlayFabSocket>();
        private static readonly FieldInfo WorkQueueField = AccessTools.Field(typeof(ZPlayFabSocket), "m_zlibWorkQueue");
        private static readonly FieldInfo WorkLockField = AccessTools.Field(typeof(PlayFabZLibWorkQueue), "m_lock");
        private static readonly FieldInfo WorkersField = AccessTools.Field(typeof(PlayFabZLibWorkQueue), "s_workers");
        private static readonly MethodInfo ExecuteMethod = AccessTools.Method(typeof(PlayFabZLibWorkQueue), "Execute");
        private static readonly MethodInfo LateUpdateMethod = AccessTools.Method(typeof(ZPlayFabSocket), "LateUpdate");

        internal static void SocketOpenPostfix(ZPlayFabSocket __instance, PlayFabZLibWorkQueue ___m_zlibWorkQueue)
        {
            if (__instance == null || ___m_zlibWorkQueue == null) return;
            Sockets[___m_zlibWorkQueue] = __instance;
        }

        internal static void SocketClosePostfix(PlayFabZLibWorkQueue ___m_zlibWorkQueue)
        {
            if (___m_zlibWorkQueue != null) Sockets.TryRemove(___m_zlibWorkQueue, out _);
        }

        internal static bool CompressPrefix(PlayFabZLibWorkQueue __instance, Queue<byte[]> ___m_inCompress, Queue<byte[]> ___m_outCompress)
        {
            if (!Sockets.TryGetValue(__instance, out ZPlayFabSocket socket)) return true;
            if (!CompressionState.TryGet(socket, out CompressionState.PeerState state) || !state.Sending) return true;

            while (___m_inCompress.Count > 0)
            {
                byte[] data = ___m_inCompress.Dequeue();
                try { ___m_outCompress.Enqueue(CompressionFrame.Encode(data)); }
                catch (Exception ex) { BetterNetworking10.Log.Error($"PlayFab compression failed: {ex.Message}"); }
            }
            return false;
        }

        internal static bool DecompressPrefix(PlayFabZLibWorkQueue __instance, Queue<byte[]> ___m_inDecompress, Queue<byte[]> ___m_outDecompress)
        {
            if (!Sockets.TryGetValue(__instance, out ZPlayFabSocket socket)) return true;
            CompressionState.TryGet(socket, out CompressionState.PeerState state);

            while (___m_inDecompress.Count > 0)
            {
                byte[] data = ___m_inDecompress.Dequeue();
                bool isBnFrame = CompressionFrame.HasHeader(data);
                try
                {
                    if (CompressionFrame.TryDecode(data, out byte[] decoded))
                    {
                        ___m_outDecompress.Enqueue(decoded);
                        if (state != null && !state.Receiving)
                        {
                            state.Receiving = true;
                            BetterNetworking10.Log.Warning("Received framed PlayFab packet before compression-start state; recovered automatically");
                        }
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    BetterNetworking10.Log.Error($"Invalid Better Networking PlayFab frame dropped: {ex.Message}");
                    continue;
                }

                if (isBnFrame) continue;

                try
                {
                    byte[] vanilla = ZlibStream.UncompressBuffer(data);
                    ___m_outDecompress.Enqueue(vanilla);
                }
                catch
                {
                    BetterNetworking10.Log.Warning("PlayFab packet was neither BN-framed nor vanilla-zlib; preserving raw data");
                    ___m_outDecompress.Enqueue(data);
                }
            }
            return false;
        }

        internal static bool DirectCompressPrefix(PlayFabZLibWorkQueue __instance, byte[] payload, ref byte[] __result)
        {
            if (!Sockets.TryGetValue(__instance, out ZPlayFabSocket socket)) return true;
            if (!CompressionState.TryGet(socket, out CompressionState.PeerState state) || !state.Sending) return true;
            __result = CompressionFrame.Encode(payload);
            return false;
        }

        internal static bool DirectDecompressPrefix(PlayFabZLibWorkQueue __instance, byte[] payload, ref byte[] __result)
        {
            if (!Sockets.TryGetValue(__instance, out ZPlayFabSocket socket)) return true;
            if (!CompressionFrame.HasHeader(payload)) return true;
            try
            {
                if (!CompressionFrame.TryDecode(payload, out __result)) return true;
                if (CompressionState.TryGet(socket, out CompressionState.PeerState state) && !state.Receiving)
                {
                    state.Receiving = true;
                    BetterNetworking10.Log.Warning("Recovered an early/direct BN-compressed PlayFab packet");
                }
                return false;
            }
            catch
            {
                return true;
            }
        }

        internal static void Flush(ZPlayFabSocket socket)
        {
            if (socket == null || WorkQueueField == null || WorkLockField == null || WorkersField == null || ExecuteMethod == null || LateUpdateMethod == null) return;
            _ = (PlayFabZLibWorkQueue)WorkQueueField.GetValue(socket);
            object syncRoot = WorkLockField.GetValue(null);
            List<PlayFabZLibWorkQueue> workers = (List<PlayFabZLibWorkQueue>)WorkersField.GetValue(null);
            lock (syncRoot)
            {
                foreach (PlayFabZLibWorkQueue worker in workers.ToArray()) ExecuteMethod.Invoke(worker, Array.Empty<object>());
            }
            LateUpdateMethod.Invoke(socket, null);
        }
    }
}
