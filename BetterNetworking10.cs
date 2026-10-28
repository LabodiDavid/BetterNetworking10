using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using PlayFab.Party;
using UnityEngine;

namespace DIT.BetterNetworking10
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(ConditionalConfigSyncAPI.ConfigSync.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
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
        private const string MinimumSyncCompatibleVersion = "1.3.0";

        internal static BetterNetworking10 Instance { get; private set; }
        internal static BNLog Log { get; private set; }

        internal static ConfigEntry<bool> LockConfiguration;
        internal static ConfigEntry<CompatibilityMode> Mode;
        internal static ConfigEntry<bool> EnableCompression;
        internal static ConfigEntry<bool> EnableQueueSize;
        internal static ConfigEntry<bool> EnableUpdateRate;
        internal static ConfigEntry<bool> EnableSendRate;
        internal static ConfigEntry<bool> EnableConnectionBuffer;
        internal static ConfigEntry<bool> EnablePortalGhostFix;
        internal static ConfigEntry<QueueSizeOption> QueueSize;
        internal static ConfigEntry<UpdateRateOption> UpdateRate;
        internal static ConfigEntry<SendRateOption> MinimumSendRate;
        internal static ConfigEntry<SendRateOption> MaximumSendRate;
        internal static ConfigEntry<ForceCrossplayOption> ForceCrossplay;
        internal static ConfigEntry<int> PlayerLimit;
        internal static bool CompressionRuntimeAvailable { get; set; }

        private Harmony _harmony;
        private ConditionalConfigSyncAPI.ConfigSync _configSync;

        private void Awake()
        {
            Instance = this;
            bool shouldImportLegacyConfig = !File.Exists(Config.ConfigFilePath);
            _configSync = new ConditionalConfigSyncAPI.ConfigSync(
                PluginGuid,
                PluginName,
                PluginVersion,
                minimumRequiredVersion: MinimumSyncCompatibleVersion,
                modRequired: false,
                requirementMode: ConditionalConfigSyncAPI.RequirementMode.Conditional,
                allowClientConfigUpdatesWhenUnlocked: true,
                logger: Logger);
            BindConfig();
            Log = new BNLog(Logger, Config);

            if (shouldImportLegacyConfig)
                LegacyConfigMigration.TryImport(Config, Log);

            GameVersion targetVersion = new GameVersion(1, 0, 21);
            if (Version.CurrentVersion < targetVersion || Version.CurrentVersion > targetVersion)
                Log.Warning($"This build targets Valheim {targetVersion}, compiled against network version {Version.c_networkVersion}; running {Version.CurrentVersion}. Check PATCH FAILED messages and retest networking after game updates.");
            else
                Log.Message($"Valheim target version matched: {Version.CurrentVersion}, compiled against network version {Version.c_networkVersion}");

            _harmony = new Harmony(PluginGuid);
            PatchInstaller.Install(_harmony);
            PortalGhostRefresh.Initialize();
            RegisterRuntimeConfigHandlers();

            if (_configSync.IsActive)
                Log.Message($"Conditional Config Sync active (optional integration, CCS >= {ConditionalConfigSyncAPI.ConfigSync.MinimumCcsVersion})");
            else if (Chainloader.PluginInfos.ContainsKey(ConditionalConfigSyncAPI.ConfigSync.PluginGuid))
                Log.Warning($"Conditional Config Sync is installed but optional synchronization is inactive; CCS {ConditionalConfigSyncAPI.ConfigSync.MinimumCcsVersion} or newer is required");
        }

        private void Update() => PortalGhostRefresh.Tick();

        private void OnDestroy()
        {
            UnregisterRuntimeConfigHandlers();
            CompressionState.Clear();
            ConnectionBufferPatch.Clear();
            PortalGhostRefresh.Clear();
            SteamSendRatePatch.Clear();
            _harmony?.UnpatchSelf();
        }

        private void BindConfig()
        {
            LockConfiguration = _configSync.BindLocking(Config, "00 - Compatibility", "Lock Configuration", true,
                "When Conditional Config Sync is installed, only server administrators may change server-controlled settings. Has no effect without CCS.");
            Mode = _configSync.Bind(Config, "00 - Compatibility", "Mode", CompatibilityMode.Balanced,
                "Balanced: the enabled feature switches below are applied. VanillaSafe: only compression is allowed; ZDO scheduling, queue, and Steam send-rate changes are disabled.",
                ConditionalConfigSyncAPI.SyncMode.Conditional, serverControlledByDefault: true);

            EnableCompression = _configSync.Bind(Config, "01 - Features", "Compression", true,
                "Compress traffic only when both peers run this compatible build.",
                ConditionalConfigSyncAPI.SyncMode.Conditional, serverControlledByDefault: true);
            EnableQueueSize = _configSync.Bind(Config, "01 - Features", "Queue Size", true,
                "Changes the ZDO send budget directly. Disabled in VanillaSafe mode.",
                ConditionalConfigSyncAPI.SyncMode.Conditional, serverControlledByDefault: true);
            EnableUpdateRate = _configSync.Bind(Config, "01 - Features", "Update Rate", true,
                "Allows a lower ZDO update frequency. Disabled in VanillaSafe mode.",
                ConditionalConfigSyncAPI.SyncMode.Conditional, serverControlledByDefault: true);
            EnableSendRate = _configSync.Bind(Config, "01 - Features", "Steam Send Rate", true,
                "Changes Steamworks send-rate limits. Has no effect with PlayFab/crossplay. Disabled in VanillaSafe mode.",
                ConditionalConfigSyncAPI.SyncMode.Conditional, serverControlledByDefault: true);
            EnableConnectionBuffer = _configSync.Bind(Config, "01 - Features", "New Connection ZDO Buffer", false,
                "Experimental and client-local because it runs before server config synchronization. Buffers early ZDOData packets per connection until ZDOMan registers the peer. OFF by default.",
                ConditionalConfigSyncAPI.SyncMode.AlwaysClientControlled);
            EnablePortalGhostFix = _configSync.Bind(Config, "01 - Features", "Portal Ghost Fix", false,
                "Optional server-side portal observer refresh. Re-sends a teleported player's ZDO and related networked status-effect ZDOs; vanilla clients are supported. Disable the standalone Sfantul mod when using this.",
                ConditionalConfigSyncAPI.SyncMode.AlwaysServerControlled);

            QueueSize = _configSync.Bind(Config, "02 - Networking", "Queue Size", QueueSizeOption.KB32,
                "ZDO send budget based on Valheim's reported queue size. Vanilla leaves the game's 10 KB budget unchanged.",
                ConditionalConfigSyncAPI.SyncMode.Conditional, serverControlledByDefault: true);
            UpdateRate = _configSync.Bind(Config, "02 - Networking", "Update Rate", UpdateRateOption.Percent100,
                "100 = vanilla 20 Hz timer; 75 = 15 Hz; 50 = 10 Hz.",
                ConditionalConfigSyncAPI.SyncMode.Conditional, serverControlledByDefault: true);
            MinimumSendRate = _configSync.Bind(Config, "03 - Steamworks", "Minimum Send Rate", SendRateOption.KB256,
                "Steamworks minimum send rate in KB/s. Keep this below the real upload capacity.",
                ConditionalConfigSyncAPI.SyncMode.Conditional, serverControlledByDefault: true);
            MaximumSendRate = _configSync.Bind(Config, "03 - Steamworks", "Maximum Send Rate", SendRateOption.KB1024,
                "Steamworks maximum send rate in KB/s.",
                ConditionalConfigSyncAPI.SyncMode.Conditional, serverControlledByDefault: true);

            ForceCrossplay = _configSync.Bind(Config, "04 - Dedicated Server", "Force Crossplay", ForceCrossplayOption.Vanilla,
                "Vanilla follows the server command line; PlayFab or Steamworks overrides the backend before server initialization. Requires restart and is disabled in VanillaSafe mode.",
                ConditionalConfigSyncAPI.SyncMode.AlwaysServerControlled);
            PlayerLimit = _configSync.Bind(Config, "04 - Dedicated Server", "Player Limit", 10,
                new ConfigDescription("Dedicated server player limit. Requires restart.", new AcceptableValueRange<int>(1, 127)),
                ConditionalConfigSyncAPI.SyncMode.AlwaysServerControlled);
        }

        private void RegisterRuntimeConfigHandlers()
        {
            EnableCompression.SettingChanged += OnCompressionSettingChanged;

            Mode.SettingChanged += OnNetworkRuntimeSettingChanged;
            EnableSendRate.SettingChanged += OnNetworkRuntimeSettingChanged;
            MinimumSendRate.SettingChanged += OnNetworkRuntimeSettingChanged;
            MaximumSendRate.SettingChanged += OnNetworkRuntimeSettingChanged;
            EnableConnectionBuffer.SettingChanged += OnNetworkRuntimeSettingChanged;
        }

        private void UnregisterRuntimeConfigHandlers()
        {
            if (EnableCompression != null)
                EnableCompression.SettingChanged -= OnCompressionSettingChanged;

            if (Mode != null)
                Mode.SettingChanged -= OnNetworkRuntimeSettingChanged;
            if (EnableSendRate != null)
                EnableSendRate.SettingChanged -= OnNetworkRuntimeSettingChanged;
            if (MinimumSendRate != null)
                MinimumSendRate.SettingChanged -= OnNetworkRuntimeSettingChanged;
            if (MaximumSendRate != null)
                MaximumSendRate.SettingChanged -= OnNetworkRuntimeSettingChanged;
            if (EnableConnectionBuffer != null)
                EnableConnectionBuffer.SettingChanged -= OnNetworkRuntimeSettingChanged;
        }

        private static void OnCompressionSettingChanged(object sender, EventArgs eventArgs)
        {
            CompressionPatch.RefreshNegotiation();
        }

        private static void OnNetworkRuntimeSettingChanged(object sender, EventArgs eventArgs)
        {
            SteamSendRatePatch.Refresh();
            if (!ConnectionBufferActive)
                ConnectionBufferPatch.Clear();
        }

        internal static bool SafeMode => Mode.Value == CompatibilityMode.VanillaSafe;
        internal static bool CompressionActive => CompressionRuntimeAvailable && EnableCompression.Value;
        internal static bool QueueActive => !SafeMode && EnableQueueSize.Value && QueueSize.Value != QueueSizeOption.Vanilla;
        internal static bool UpdateRateActive => !SafeMode && EnableUpdateRate.Value && UpdateRate.Value != UpdateRateOption.Percent100;
        internal static bool SendRateActive => !SafeMode && EnableSendRate.Value;
        internal static bool ConnectionBufferActive => !SafeMode && EnableConnectionBuffer.Value;
        internal static bool PortalGhostFixActive => EnablePortalGhostFix.Value;

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
            BetterNetworking10.CompressionRuntimeAvailable = false;
            try
            {
                CompressionCodec.Initialize();
                CompressionFrame.SelfTest();
                BetterNetworking10.CompressionRuntimeAvailable = true;
                InstallCompression(harmony);
            }
            catch (Exception ex)
            {
                Skip("Compression", $"self-test failed: {ex.GetType().Name}: {ex.Message}");
                BetterNetworking10.Log.Error("Compression was left disabled because its startup self-test failed");
            }

            Patch(harmony, "ZDO queue budget (runtime guarded)", AccessTools.DeclaredMethod(typeof(ZDOMan), "SendZDOs"), transpiler: nameof(QueueBudgetPatch.Transpiler), patchType: typeof(QueueBudgetPatch));
            Patch(harmony, "ZDO update rate (runtime guarded)", AccessTools.DeclaredMethod(typeof(ZDOMan), "SendZDOToPeers2"), prefix: nameof(UpdateRatePatch.Prefix), patchType: typeof(UpdateRatePatch));
            Patch(harmony, "Steam send rate (runtime guarded)", AccessTools.DeclaredMethod(typeof(ZSteamSocket), "RegisterGlobalCallbacks"), postfix: nameof(SteamSendRatePatch.Postfix), patchType: typeof(SteamSendRatePatch));
            InstallConnectionBuffer(harmony);

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
            Patch(harmony, "Dedicated backend selection (runtime guarded)", AccessTools.DeclaredMethod(typeof(ZNet), nameof(ZNet.SetServer), new[] { typeof(bool), typeof(bool), typeof(bool), typeof(string), typeof(string), typeof(World) }), prefix: nameof(DedicatedPatch.SetServerPrefix), patchType: typeof(DedicatedPatch));
            Patch(harmony, "Dedicated player limit (runtime value)", AccessTools.DeclaredMethod(typeof(ZNet), "RPC_PeerInfo"), transpiler: nameof(DedicatedPatch.PlayerLimitTranspiler), patchType: typeof(DedicatedPatch));
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
