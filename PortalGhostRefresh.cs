using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using UnityEngine;

namespace DIT.BetterNetworking10
{
    internal static class PortalGhostRefresh
    {
        private const string StandalonePluginGuid = "sfantul.portalobserverfix";
        private const float CheckIntervalSeconds = 0.25f;
        private const float DelayedRefreshSeconds = 0.75f;
        private const float MinimumJumpDistance = 64f;
        private const float MinimumJumpDistanceSquared = MinimumJumpDistance * MinimumJumpDistance;

        private static readonly Dictionary<ZDOID, Vector3> LastPositions = new Dictionary<ZDOID, Vector3>();
        private static readonly Dictionary<ZDOID, float> DelayedRefreshes = new Dictionary<ZDOID, float>();
        private static readonly HashSet<ZDOID> ActivePlayers = new HashSet<ZDOID>();
        private static readonly List<ZDOID> TemporaryIds = new List<ZDOID>();

        private static ZDOMan _manager;
        private static float _nextCheck;
        private static bool _externalModInstalled;
        private static bool _runtimeDisabled;

        internal static void Initialize()
        {
            _externalModInstalled = Chainloader.PluginInfos.ContainsKey(StandalonePluginGuid);
            _runtimeDisabled = false;

            if (!BetterNetworking10.PortalGhostFixActive)
            {
                BetterNetworking10.Log.Message("FEATURE SKIP Portal ghost fix: disabled by configuration");
                return;
            }

            if (_externalModInstalled)
            {
                BetterNetworking10.Log.Warning("FEATURE SKIP Portal ghost fix: standalone Sfantul-PortalGhostFix detected; built-in refresh disabled to avoid duplicate sends");
                return;
            }

            BetterNetworking10.Log.Message("FEATURE ON Portal ghost fix: server-side player and linked status-effect refresh; vanilla clients supported");
        }

        internal static void Tick()
        {
            if (!BetterNetworking10.PortalGhostFixActive || _externalModInstalled || _runtimeDisabled)
                return;

            ZNet net = ZNet.instance;
            ZDOMan manager = ZDOMan.instance;
            if (net == null || !net.IsServer() || manager == null)
            {
                if (_manager != null)
                    ClearRuntimeState();
                return;
            }

            if (_manager != manager)
            {
                ClearRuntimeState();
                _manager = manager;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextCheck)
                return;

            _nextCheck = now + CheckIntervalSeconds;

            try
            {
                ActivePlayers.Clear();

                foreach (ZNet.PlayerInfo player in net.GetPlayerList())
                {
                    ZDOID characterId = player.m_characterID;
                    if (characterId == ZDOID.None)
                        continue;

                    ActivePlayers.Add(characterId);
                    ZDO character = manager.GetZDO(characterId);
                    if (character == null)
                    {
                        LastPositions.Remove(characterId);
                        DelayedRefreshes.Remove(characterId);
                        continue;
                    }

                    Vector3 currentPosition = character.GetPosition();
                    if (LastPositions.TryGetValue(characterId, out Vector3 previousPosition))
                    {
                        Vector3 movement = currentPosition - previousPosition;
                        if (movement.sqrMagnitude >= MinimumJumpDistanceSquared)
                        {
                            int linkedEffects = ForceRefresh(manager, characterId);
                            DelayedRefreshes[characterId] = now + DelayedRefreshSeconds;
                            BetterNetworking10.Log.Message(
                                $"Portal ghost refresh queued for {SafePlayerName(player.m_name)} ({characterId}) after {movement.magnitude:F1} m movement; linked effects={linkedEffects}");
                        }
                    }

                    if (DelayedRefreshes.TryGetValue(characterId, out float due) && now >= due)
                    {
                        ForceRefresh(manager, characterId);
                        DelayedRefreshes.Remove(characterId);
                    }

                    LastPositions[characterId] = currentPosition;
                }

                TemporaryIds.Clear();
                foreach (ZDOID characterId in LastPositions.Keys)
                {
                    if (!ActivePlayers.Contains(characterId))
                        TemporaryIds.Add(characterId);
                }

                foreach (ZDOID characterId in TemporaryIds)
                {
                    LastPositions.Remove(characterId);
                    DelayedRefreshes.Remove(characterId);
                }
            }
            catch (Exception ex)
            {
                _runtimeDisabled = true;
                ClearRuntimeState();
                BetterNetworking10.Log.Error($"FEATURE FAILED Portal ghost fix: {ex.GetType().Name}: {ex.Message}. Disabled until restart; networking remains otherwise unchanged");
            }
        }

        internal static void Clear()
        {
            ClearRuntimeState();
            _externalModInstalled = false;
            _runtimeDisabled = false;
        }

        private static int ForceRefresh(ZDOMan manager, ZDOID characterId)
        {
            manager.ForceSendZDO(characterId);

            int linkedEffects = 0;
            List<ZDOID> candidates = ZDOExtraData.GetAllZDOIDsWithHash(
                ZDOExtraData.Type.Long, ZDOVars.s_ownerZDOUserId);

            foreach (ZDOID candidateId in candidates)
            {
                if (candidateId == characterId)
                    continue;

                ZDO candidate = manager.GetZDO(candidateId);
                if (candidate == null ||
                    candidate.GetLong(ZDOVars.s_ownerZDOUserId) != characterId.UserID ||
                    candidate.GetLong(ZDOVars.s_ownerZDOId) != characterId.ID)
                    continue;

                manager.ForceSendZDO(candidateId);
                linkedEffects++;
            }

            return linkedEffects;
        }

        private static void ClearRuntimeState()
        {
            LastPositions.Clear();
            DelayedRefreshes.Clear();
            ActivePlayers.Clear();
            TemporaryIds.Clear();
            _manager = null;
            _nextCheck = 0f;
        }

        private static string SafePlayerName(string name) => string.IsNullOrEmpty(name) ? "unknown player" : name;
    }
}
