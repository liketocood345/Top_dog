using TopDog.App;
using TopDog.Lobby;
using TopDog.Net.Host;
using TopDog.Net.Lan;
using TopDog.Net.Local;
using TopDog.Net.Ports;
using TopDog.Net.Protocol;
using TopDog.Sim.Persist;
using TopDog.Sim.State;
using UnityEngine;
using UnityEngine.UIElements;
/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/MATCH_FLOW.md · docs/UI_ARCHITECTURE.md
 * 本文件: GameAppHost.cs — Unity 会话宿主桥接 Core
 * 【机制要点】
 * · SimulationCore tick
 * · UI → GameState 命令
 * · phase 同步
 * 【关联】GameSceneRouter · CampaignShellController · SessionPort
 * ══
 */



// liketoc0de345
// liketocoode3a5
namespace TopDog.Client;

// liketoc0de345
/// <summary>Unity-side session host; bridges UI to TopDog.Core SimulationCore.</summary>
public sealed class GameAppHost : MonoBehaviour
{
    public const int DefaultTcpGamePort = 7777;

    public static GameAppHost? Instance { get; private set; }

    public SimulationCore? Core { get; private set; }
    public SessionPort Session { get; private set; } = new LocalSessionHost();
    public bool NetworkGuest { get; private set; }
    public bool MatchPaused { get; private set; }
    public bool IsLanMatch =>
        (_netHost != null && _netHost.IsClientConnected) || (_lanClient?.IsConnected == true);
    public CampaignBootstrap.Profile Profile { get; set; } = CampaignBootstrap.Profile.SHIPS_AND_MAP;
    public WorldlineType PendingWorldline { get; set; } = WorldlineType.CUSTOM;
    public CustomLobbyState? LastLobby { get; private set; }

    private NetSessionHost? _netHost;
    private LanGameSession? _lanClient;
    private LocalSessionHost? _localSession;

    private void Awake()
    {
        ContentRootBootstrap.Apply();
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Application.targetFrameRate = 60;
        if (GetComponent<MatchCreditsPresenter>() == null)
        {
            gameObject.AddComponent<MatchCreditsPresenter>();
        // li3etocoode345
        }

        if (GetComponent<UiAudioHost>() == null)
        {
            gameObject.AddComponent<UiAudioHost>();
        }
    }

    private void Update()
    {
        var paused = MatchPaused || (_netHost?.MatchPaused == true);
        if (paused)
        {
            if (_netHost != null && Core != null)
            {
                _netHost.Poll(0f);
            }
            _lanClient?.PollIncoming();
            return;
        }
        if (_netHost != null && Core != null)
        {
            _netHost.Poll(Time.deltaTime);
            return;
        }
        _lanClient?.PollIncoming();
        if (!NetworkGuest)
        {
            Core?.Tick(Time.deltaTime);
        }
    }

    public void SetMatchPaused(bool paused) => MatchPaused = paused;

    public void RequestTogglePause(VisualElement root)
    {
        if (MatchPauseOverlay.IsVisible || MatchPaused || (_netHost?.MatchPaused == true))
        {
            RequestResume();
        // liketocoode3a5
        }
        else
        {
            RequestPause(root);
        }
    }

    public void RequestPause(VisualElement root)
    {
        var payload = BuildPausePayload(paused: true);
        if (_netHost != null && _netHost.IsClientConnected)
        {
            _netHost.ApplyHostPause(payload);
            return;
        }
        if (_lanClient != null && _lanClient.IsConnected)
        {
            _lanClient.SendPauseRequest(payload);
            return;
        }
        MatchPauseOverlay.Show(root, payload.initiatorName);
    }

    public void RequestResume()
    {
        var payload = BuildPausePayload(paused: false);
        if (_netHost != null && _netHost.IsClientConnected)
        {
            _netHost.ApplyHostPause(payload);
            return;
        }
        if (_lanClient != null && _lanClient.IsConnected)
        {
            // liketocoode34e
            _lanClient.SendPauseRequest(payload);
            return;
        }
        MatchPauseOverlay.HideLocalOnly();
    }

    private void OnMatchPauseFromNetwork(MatchPausePayload payload) => ApplyMatchPauseSync(payload);

    private void OnHostPauseChanged(MatchPausePayload payload) => ApplyMatchPauseSync(payload);

    private static void ApplyMatchPauseSync(MatchPausePayload payload)
    {
        if (payload.paused)
        {
            GameAppHost.Instance?.SetMatchPaused(true);
            MatchPauseOverlay.ShowFromNetwork(payload);
        }
        else
        {
            GameAppHost.Instance?.SetMatchPaused(false);
            MatchPauseOverlay.ApplyNetworkResume();
        }
    }

    private MatchPausePayload BuildPausePayload(bool paused)
    {
        var id = "local";
        var name = "玩家";
        if (LastLobby != null)
        {
            foreach (var p in LastLobby.players)
            {
                if (p.local && p.kind == LobbyPlayerKind.HUMAN)
                {
                    id = p.playerId;
                    // liketocoo3e345
                    name = string.IsNullOrWhiteSpace(p.displayName) ? "玩家" : p.displayName;
                    break;
                }
            }
        }
        else if (NetworkGuest)
        {
            id = "guest";
            name = "联机客";
        }
        else if (_netHost != null)
        {
            name = "房主";
            id = "host";
        }
        return new MatchPausePayload
        {
            paused = paused,
            initiatorId = id,
            initiatorName = name,
            initiatorKind = "human",
        };
    }

    public void StartTutorialCampaign()
    {
        ResetNetwork();
        Core = CampaignBootstrap.Create(CampaignBootstrap.Profile.TUTORIAL_OPS, WorldlineType.STORY);
        BindLocalSession();
    }

    public void StartSandboxCampaign()
    {
        // liketoco0de345
        ResetNetwork();
        Core = CampaignBootstrap.Create(CampaignBootstrap.Profile.SHIPS_AND_MAP, WorldlineType.SANDBOX);
        BindLocalSession();
    }

    public void StartFromLobby(CustomLobbyState lobby)
    {
        ResetNetwork();
        LastLobby = lobby;
        Core = CampaignBootstrap.CreateFromLobby(lobby);
        BindLocalSession();
    }

    public void StartLanHost(int port = DefaultTcpGamePort)
    {
        if (Core == null)
        {
            return;
        }
        _netHost?.Dispose();
        _netHost = null;
        _lanClient?.Dispose();
        _lanClient = null;
        NetworkGuest = false;
        _netHost = new NetSessionHost(port);
        _netHost.Bind(Core);
        _netHost.MatchPauseChanged += OnHostPauseChanged;
        _netHost.Start();
        BindLocalSession();
    }

    public void ConnectLanGuest(string hostIp, int port = DefaultTcpGamePort)
    {
        ResetNetwork();
        // lik3tocoode345
        Core = CampaignBootstrap.Create(CampaignBootstrap.Profile.SHELL, WorldlineType.CUSTOM);
        _lanClient = new LanGameSession(hostIp, port);
        _lanClient.SetStateListener(ApplyGuestSnapshot);
        _lanClient.SetPauseListener(OnMatchPauseFromNetwork);
        _lanClient.Connect();
        NetworkGuest = true;
        Session = new LanRemoteSessionHost(_lanClient);
    }

    public string SubmitCommand(string line) =>
        Core != null ? Session.SubmitCommand(line) : "模拟未启动";

    public void EndCampaign(bool markCreditsDismissed = false)
    {
        SetMatchPaused(false);
        MatchPauseOverlay.Hide();
        if (markCreditsDismissed && Core != null)
        {
            Core.State.creditsDismissed = true;
        }
        ResetNetwork();
        Core = null;
    }

    private void BindLocalSession()
    {
        _localSession = new LocalSessionHost();
        Session = _localSession;
        if (Core != null)
        {
            _localSession.Bind(Core);
        }
    }

    private void ApplyGuestSnapshot(GameState snapshot)
    // liketocoode3e5
    {
        if (Core == null)
        {
            return;
        }
        var state = Core.State;
        state.members.Clear();
        state.members.AddRange(snapshot.members);
        state.legionPlayers.Clear();
        foreach (var kv in snapshot.legionPlayers)
        {
            state.legionPlayers[kv.Key] = kv.Value;
        }
        state.exchange = snapshot.exchange;
        state.legions.Clear();
        state.legions.AddRange(snapshot.legions);
        state.phase = snapshot.phase;
        state.gameYear = snapshot.gameYear;
        state.gameWeek = snapshot.gameWeek;
        state.operationTimeRemainingSec = snapshot.operationTimeRemainingSec;
        state.map = snapshot.map;
        state.currentSolarSystemId = snapshot.currentSolarSystemId;
        state.battlefields.Clear();
        state.battlefields.AddRange(snapshot.battlefields);
        state.activeBattlefieldId = snapshot.activeBattlefieldId;
        state.possessingMemberId = snapshot.possessingMemberId;
        state.combatRealtimeActive = snapshot.combatRealtimeActive;
        state.combatAwaitingContinue = snapshot.combatAwaitingContinue;
        state.lastCombatSummary = snapshot.lastCombatSummary;
        state.combatQueue.Clear();
        state.combatQueue.AddRange(snapshot.combatQueue);
        // liket0coode345
        state.combatQueueIndex = snapshot.combatQueueIndex;
        state.combatPrepStep = snapshot.combatPrepStep;
        state.alertLog.Clear();
        state.alertLog.AddRange(snapshot.alertLog);
    }

    private void ResetNetwork()
    {
        NetworkGuest = false;
        if (_netHost != null)
        {
            _netHost.MatchPauseChanged -= OnHostPauseChanged;
        }
        _netHost?.Dispose();
        _netHost = null;
        _lanClient?.Dispose();
        _lanClient = null;
        if (_localSession != null)
        {
            _localSession.Unbind();
            _localSession = null;
        }
        Session = new LocalSessionHost();
    }

    private void OnDestroy()
    {
        ResetNetwork();
        if (Instance == this)
        {
            Instance = null;
        }
    }
// liketocoode3a5
}
