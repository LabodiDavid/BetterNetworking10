using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using PlayFab.Party;
using UnityEngine;

namespace DIT.BetterNetworking10
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInIncompatibility("CW_Jesse.BetterNetworking")]
    [BepInIncompatibility("org.bepinex.plugins.network")]
    [BepInIncompatibility("be.sebastienvercammen.valheim.netcompression")]
    [BepInIncompatibility("com.github.dalayeth.Networkfix")]
    [BepInIncompatibility("Steel.ValheimMod")]
    public sealed class BetterNetworking10 : BaseUnityPlugin
    {
        public const string PluginGuid = "DIT.BetterNetworking10";
        public const string PluginName = "Better Networking 1.0 Safe";
        public const string PluginVersion = VersionInfo.Version;

        internal static BetterNetworking10 Instance { get; private set; }
        internal static BNLog Log { get; private set; }

        internal static ConfigEntry<CompatibilityMode> Mode;
        internal static ConfigEntry<bool> EnableCompression;
        internal static ConfigEntry<bool> EnableQueueSize;
        internal static ConfigEntry<bool> EnableUpdateRate;
        internal static ConfigEntry<bool> EnableSendRate;
        internal static ConfigEntry<bool> EnableConnectionBuffer;
        internal static ConfigEntry<QueueSizeOption> QueueSize;
        internal static ConfigEntry<UpdateRateOption> UpdateRate;
        internal static ConfigEntry<SendRateOption> MinimumSendRate;
        internal static ConfigEntry<SendRateOption> MaximumSendRate;
        internal static ConfigEntry<ForceCrossplayOption> ForceCrossplay;
        internal static ConfigEntry<int> PlayerLimit;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            bool shouldImportLegacyConfig = !File.Exists(Config.ConfigFilePath);
            BindConfig();
            Log = new BNLog(Logger, Config);

            if (shouldImportLegacyConfig)
                LegacyConfigMigration.TryImport(Config, Log);

            GameVersion target = new GameVersion(1, 0, 12);
            if (Version.CurrentVersion != target)
                Log.Warning($"This build targets Valheim {target}; running {Version.CurrentVersion}. Check PATCH FAILED messages and retest networking after game updates.");
            else
                Log.Message($"Valheim target verified: {Version.CurrentVersion}, network version {Version.c_networkVersion}");

            _harmony = new Harmony(PluginGuid);
            PatchInstaller.Install(_harmony);
        }

        private void OnDestroy()
        {
            CompressionState.Clear();
            ConnectionBufferPatch.Clear();
            _harmony?.UnpatchSelf();
        }

        private void BindConfig()
        {
            Mode = Config.Bind("00 - Compatibility", "Mode", CompatibilityMode.Balanced,
                "Balanced: the enabled feature switches below are applied. VanillaSafe: only compression is allowed; ZDO scheduling, queue, and Steam send-rate changes are disabled.");

            EnableCompression = Config.Bind("01 - Features", "Compression", true,
                "Compress traffic only when both peers run this compatible build.");
            EnableQueueSize = Config.Bind("01 - Features", "Queue Size", true,
                "Changes the ZDO send budget directly. Disabled in VanillaSafe mode.");
            EnableUpdateRate = Config.Bind("01 - Features", "Update Rate", true,
                "Allows a lower ZDO update frequency. Disabled in VanillaSafe mode.");
            EnableSendRate = Config.Bind("01 - Features", "Steam Send Rate", true,
                "Changes Steamworks send-rate limits. Has no effect with PlayFab/crossplay. Disabled in VanillaSafe mode.");
            EnableConnectionBuffer = Config.Bind("01 - Features", "New Connection ZDO Buffer", false,
                "Experimental. Buffers early ZDOData packets per connection until ZDOMan registers the peer. OFF by default because stale player/ZDO state is being diagnosed.");

            QueueSize = Config.Bind("02 - Networking", "Queue Size", QueueSizeOption.KB32,
                "ZDO send budget based on Valheim's reported queue size. Vanilla leaves the game's 10 KB budget unchanged.");
            UpdateRate = Config.Bind("02 - Networking", "Update Rate", UpdateRateOption.Percent100,
                "100 = vanilla 20 Hz timer; 75 = 15 Hz; 50 = 10 Hz.");
            MinimumSendRate = Config.Bind("03 - Steamworks", "Minimum Send Rate", SendRateOption.KB256,
                "Steamworks minimum send rate in KB/s. Keep this below the real upload capacity.");
            MaximumSendRate = Config.Bind("03 - Steamworks", "Maximum Send Rate", SendRateOption.KB1024,
                "Steamworks maximum send rate in KB/s.");

            ForceCrossplay = Config.Bind("04 - Dedicated Server", "Force Crossplay", ForceCrossplayOption.Vanilla,
                "Vanilla follows the server command line; PlayFab forces crossplay; Steamworks disables crossplay.");
            PlayerLimit = Config.Bind("04 - Dedicated Server", "Player Limit", 10,
                new ConfigDescription("Dedicated server player limit. Requires restart.", new AcceptableValueRange<int>(1, 127)));
        }

        internal static bool SafeMode => Mode.Value == CompatibilityMode.VanillaSafe;
        internal static bool CompressionActive => EnableCompression.Value;
        internal static bool QueueActive => !SafeMode && EnableQueueSize.Value && QueueSize.Value != QueueSizeOption.Vanilla;
        internal static bool UpdateRateActive => !SafeMode && EnableUpdateRate.Value && UpdateRate.Value != UpdateRateOption.Percent100;
        internal static bool SendRateActive => !SafeMode && EnableSendRate.Value;
        internal static bool ConnectionBufferActive => !SafeMode && EnableConnectionBuffer.Value;

        internal static int QueueBudgetBytes
        {
            get
            {
                switch (QueueSize.Value)
                {
                    case QueueSizeOption.KB80: return 80 * 1024;
                    case QueueSizeOption.KB64: return 64 * 1024;
                    case QueueSizeOption.KB48: return 48 * 1024;
                    case QueueSizeOption.KB32: return 32 * 1024;
                    default: return 10 * 1024;
                }
            }
        }

        internal static float UpdateRateMultiplier
        {
            get
            {
                switch (UpdateRate.Value)
                {
                    case UpdateRateOption.Percent75: return 0.75f;
                    case UpdateRateOption.Percent50: return 0.5f;
                    default: return 1f;
                }
            }
        }

        internal static int RateBytesPerSecond(SendRateOption option) => (int)option * 1024;

        internal static bool IsDedicated => ZNet.instance != null ? ZNet.instance.IsDedicated() : Application.isBatchMode;
    }

    public enum CompatibilityMode { Balanced, VanillaSafe }
    public enum QueueSizeOption { Vanilla = 10, KB32 = 32, KB48 = 48, KB64 = 64, KB80 = 80 }
    public enum UpdateRateOption { Percent100 = 100, Percent75 = 75, Percent50 = 50 }
    public enum SendRateOption { KB150 = 150, KB256 = 256, KB512 = 512, KB768 = 768, KB1024 = 1024 }
    public enum ForceCrossplayOption { Vanilla, PlayFab, Steamworks }

    internal static class PatchInstaller
    {
        private static int _applied;
        private static int _skipped;
        private static int _failed;

        internal static void Install(Harmony harmony)
        {
            if (BetterNetworking10.CompressionActive)
            {
                try
                {
                    CompressionCodec.Initialize();
                    CompressionFrame.SelfTest();
                    InstallCompression(harmony);
                }
                catch (Exception ex)
                {
                    Skip("Compression", $"self-test failed: {ex.GetType().Name}: {ex.Message}");
                    BetterNetworking10.Log.Error("Compression was left disabled because its startup self-test failed");
                }
            }
            else
            {
                Skip("Compression", "disabled by configuration");
            }

            if (BetterNetworking10.QueueActive)
                Patch(harmony, "ZDO queue budget", AccessTools.DeclaredMethod(typeof(ZDOMan), "SendZDOs"), transpiler: nameof(QueueBudgetPatch.Transpiler), patchType: typeof(QueueBudgetPatch));
            else
                Skip("ZDO queue budget", BetterNetworking10.SafeMode ? "VanillaSafe mode" : "vanilla/disabled setting");

            if (BetterNetworking10.UpdateRateActive)
                Patch(harmony, "ZDO update rate", AccessTools.DeclaredMethod(typeof(ZDOMan), "SendZDOToPeers2"), prefix: nameof(UpdateRatePatch.Prefix), patchType: typeof(UpdateRatePatch));
            else
                Skip("ZDO update rate", BetterNetworking10.SafeMode ? "VanillaSafe mode" : "100%/disabled setting");

            if (BetterNetworking10.SendRateActive)
                Patch(harmony, "Steam send rate", AccessTools.DeclaredMethod(typeof(ZSteamSocket), "RegisterGlobalCallbacks"), postfix: nameof(SteamSendRatePatch.Postfix), patchType: typeof(SteamSendRatePatch));
            else
                Skip("Steam send rate", BetterNetworking10.SafeMode ? "VanillaSafe mode" : "disabled by configuration");

            if (BetterNetworking10.ConnectionBufferActive)
                InstallConnectionBuffer(harmony);
            else
                Skip("New connection ZDO buffer", BetterNetworking10.SafeMode ? "VanillaSafe mode" : "disabled by default/configuration");

            InstallDedicatedTweaks(harmony);
            BetterNetworking10.Log.Message($"Patch audit complete: {_applied} applied, {_skipped} skipped, {_failed} failed. Mode={BetterNetworking10.Mode.Value}");
        }

        private static void InstallCompression(Harmony harmony)
        {
            Patch(harmony, "Compression connect", AccessTools.DeclaredMethod(typeof(ZNet), "OnNewConnection"), postfix: nameof(CompressionPatch.OnConnectPostfix), patchType: typeof(CompressionPatch));
            Patch(harmony, "Compression disconnect pre", AccessTools.DeclaredMethod(typeof(ZNet), nameof(ZNet.Disconnect)), prefix: nameof(CompressionPatch.OnDisconnectPrefix), patchType: typeof(CompressionPatch));
            Patch(harmony, "Compression disconnect post", AccessTools.DeclaredMethod(typeof(ZNet), nameof(ZNet.Disconnect)), postfix: nameof(CompressionPatch.OnDisconnectPostfix), patchType: typeof(CompressionPatch));
            Patch(harmony, "Steam compress-once send", AccessTools.DeclaredMethod(typeof(ZSteamSocket), nameof(ZSteamSocket.Send), new[] { typeof(ZPackage) }), prefix: nameof(SteamCompressionPatch.SendPrefix), patchType: typeof(SteamCompressionPatch));
            Patch(harmony, "Steam receive", AccessTools.DeclaredMethod(typeof(ZSteamSocket), nameof(ZSteamSocket.Recv)), postfix: nameof(SteamCompressionPatch.ReceivePostfix), patchType: typeof(SteamCompressionPatch));

            Patch(harmony, "PlayFab socket ctor()", AccessTools.DeclaredConstructor(typeof(ZPlayFabSocket), Type.EmptyTypes), postfix: nameof(PlayFabCompressionPatch.SocketOpenPostfix), patchType: typeof(PlayFabCompressionPatch));
            Patch(harmony, "PlayFab socket ctor(string,callback)", AccessTools.DeclaredConstructor(typeof(ZPlayFabSocket), new[] { typeof(string), typeof(Action<PlayFabMatchmakingServerData>) }), postfix: nameof(PlayFabCompressionPatch.SocketOpenPostfix), patchType: typeof(PlayFabCompressionPatch));
            Patch(harmony, "PlayFab socket ctor(player)", AccessTools.DeclaredConstructor(typeof(ZPlayFabSocket), new[] { typeof(PlayFabPlayer) }), postfix: nameof(PlayFabCompressionPatch.SocketOpenPostfix), patchType: typeof(PlayFabCompressionPatch));
            Patch(harmony, "PlayFab socket dispose", AccessTools.DeclaredMethod(typeof(ZPlayFabSocket), nameof(ZPlayFabSocket.Dispose)), postfix: nameof(PlayFabCompressionPatch.SocketClosePostfix), patchType: typeof(PlayFabCompressionPatch));
            Patch(harmony, "PlayFab compress", AccessTools.DeclaredMethod(typeof(PlayFabZLibWorkQueue), "DoCompress"), prefix: nameof(PlayFabCompressionPatch.CompressPrefix), patchType: typeof(PlayFabCompressionPatch));
            Patch(harmony, "PlayFab decompress", AccessTools.DeclaredMethod(typeof(PlayFabZLibWorkQueue), "DoUncompress"), prefix: nameof(PlayFabCompressionPatch.DecompressPrefix), patchType: typeof(PlayFabCompressionPatch));
            Patch(harmony, "PlayFab direct compress", AccessTools.DeclaredMethod(typeof(PlayFabZLibWorkQueue), "CompressOnThisThread"), prefix: nameof(PlayFabCompressionPatch.DirectCompressPrefix), patchType: typeof(PlayFabCompressionPatch));
            Patch(harmony, "PlayFab direct decompress", AccessTools.DeclaredMethod(typeof(PlayFabZLibWorkQueue), "UncompressOnThisThread"), prefix: nameof(PlayFabCompressionPatch.DirectDecompressPrefix), patchType: typeof(PlayFabCompressionPatch));
        }

        private static void InstallConnectionBuffer(Harmony harmony)
        {
            Patch(harmony, "Connection buffer start", AccessTools.DeclaredMethod(typeof(ZNet), "OnNewConnection"), postfix: nameof(ConnectionBufferPatch.OnNewConnectionPostfix), patchType: typeof(ConnectionBufferPatch));
            Patch(harmony, "Connection buffer replay", AccessTools.DeclaredMethod(typeof(ZDOMan), nameof(ZDOMan.AddPeer)), postfix: nameof(ConnectionBufferPatch.AddPeerPostfix), patchType: typeof(ConnectionBufferPatch));
            Patch(harmony, "Connection buffer clear", AccessTools.DeclaredMethod(typeof(ZNet), nameof(ZNet.Shutdown)), postfix: nameof(ConnectionBufferPatch.ShutdownPostfix), patchType: typeof(ConnectionBufferPatch));
        }

        private static void InstallDedicatedTweaks(Harmony harmony)
        {
            if (BetterNetworking10.ForceCrossplay.Value != ForceCrossplayOption.Vanilla)
                Patch(harmony, "Dedicated force crossplay", AccessTools.DeclaredMethod(typeof(FejdStartup), "ParseServerArguments"), postfix: nameof(DedicatedPatch.ParseArgumentsPostfix), patchType: typeof(DedicatedPatch));
            else
                Skip("Dedicated force crossplay", "vanilla setting");

            if (BetterNetworking10.PlayerLimit.Value != 10)
                Patch(harmony, "Dedicated player limit", AccessTools.DeclaredMethod(typeof(ZNet), "RPC_PeerInfo"), transpiler: nameof(DedicatedPatch.PlayerLimitTranspiler), patchType: typeof(DedicatedPatch));
            else
                Skip("Dedicated player limit", "vanilla value 10");
        }

        private static void Patch(Harmony harmony, string label, MethodBase target, string prefix = null, string postfix = null, string transpiler = null, Type patchType = null)
        {
            if (target == null)
            {
                _failed++;
                BetterNetworking10.Log.Error($"PATCH FAILED {label}: target method not found; feature left vanilla");
                return;
            }

            try
            {
                HarmonyMethod pre = prefix == null ? null : new HarmonyMethod(AccessTools.DeclaredMethod(patchType, prefix));
                HarmonyMethod post = postfix == null ? null : new HarmonyMethod(AccessTools.DeclaredMethod(patchType, postfix));
                HarmonyMethod trans = transpiler == null ? null : new HarmonyMethod(AccessTools.DeclaredMethod(patchType, transpiler));
                if ((prefix != null && pre.method == null) || (postfix != null && post.method == null) || (transpiler != null && trans.method == null))
                    throw new MissingMethodException("patch method not found");

                harmony.Patch(target, pre, post, trans);
                _applied++;
                BetterNetworking10.Log.Message($"PATCH OK {label}: {target.DeclaringType?.Name}.{target.Name}");
            }
            catch (Exception ex)
            {
                _failed++;
                BetterNetworking10.Log.Error($"PATCH FAILED {label}: {ex.GetType().Name}: {ex.Message}; feature left vanilla");
            }
        }

        private static void Skip(string label, string reason)
        {
            _skipped++;
            BetterNetworking10.Log.Message($"PATCH SKIP {label}: {reason}");
        }
    }
}
