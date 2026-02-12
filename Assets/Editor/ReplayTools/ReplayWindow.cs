using System;
using System.IO;
using AI;
using Core;
using Core.GAS;
using Core.Replay;
using Gameplay.Characters;
using Network.Local;
using UnityEditor;
using UnityEngine;

namespace ReplayTools
{
    /// <summary>
    /// 回放面板：录制一局 AI 对战 → 存 .dynreplay → 加载 → Tick/回合/指令步进 →
    /// 实时哈希 vs 录制值（红绿）+ 角色状态表 + 事件时间线。
    /// </summary>
    public class ReplayWindow : EditorWindow
    {
        private const string DirKey = "Dynasty.ReplayDir";
        private const double AutoPlayInterval = 0.1;

        [MenuItem("Dynasty/回放面板")]
        public static void Open()
        {
            GetWindow<ReplayWindow>("回放面板");
        }

        // 录制区
        private int _recordPlayers = 4;
        private int _recordSeed = 42;
        private string _recordSummary = "";

        // 加载区
        private ReplayLog _log;
        private string _loadedPath = "";

        // 回放区
        private ReplayPlayer _player;
        private bool _autoPlay;
        private double _lastAuto;

        private void OnGUI()
        {
            DrawRecordSection();
            EditorGUILayout.Separator();
            DrawLoadSection();
            EditorGUILayout.Separator();
            DrawReplaySection();
        }

        private void DrawRecordSection()
        {
            EditorGUILayout.LabelField("录制", EditorStyles.boldLabel);
            _recordPlayers = EditorGUILayout.IntSlider("人数", _recordPlayers, 2, 4);
            _recordSeed = EditorGUILayout.IntField("种子", _recordSeed);
            if (GUILayout.Button("录制一局（AI 对战）"))
                RecordGame();
            if (!string.IsNullOrEmpty(_recordSummary))
                EditorGUILayout.HelpBox(_recordSummary, MessageType.Info);
            if (_log != null && GUILayout.Button("保存 .dynreplay"))
                SaveDialog();
        }

        private void RecordGame()
        {
            int n = _recordPlayers;
            var state = new WorldState(n, _recordSeed);
            var ais = new SimpleAI[n];
            for (int i = 0; i < n; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.StartGame();
            var recorder = ReplayRecorder.Attach(lockstep, _recordSeed, new int[n]);
            try
            {
                int turns = 0;
                while (state.Phase != TurnPhase.GameOver && turns < GameConfig.MaxTurns)
                {
                    lockstep.RunTurn();
                    turns++;
                }
            }
            finally
            {
                // 无论正常结束还是异常，都卸载旁观钩子并重置回放状态（ReplayRecorder 文档契约）
                _log = recorder.Finish();
                _player = null;
                _autoPlay = false;
            }
            _recordSummary =
                $"回合 {state.CurrentTurn} / Tick {_log.TotalTicks} / 事件 {_log.Events.Count} / " +
                $"指令 {_log.Actions.Count} / 判定轮 {_log.Chips.Count}";
        }

        private void SaveDialog()
        {
            string dir = EditorPrefs.GetString(DirKey, Application.dataPath);
            string path = EditorUtility.SaveFilePanel("保存回放", dir,
                $"replay-seed{_recordSeed}.dynreplay", "dynreplay");
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString(DirKey, Path.GetDirectoryName(path));
            try
            {
                ReplaySerializer.Save(_log, path);
                _loadedPath = path;
                Debug.Log($"[Replay] 已保存 {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Replay] 保存失败: {ex.Message}");
            }
        }

        private void DrawLoadSection()
        {
            EditorGUILayout.LabelField("加载", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开 .dynreplay"))
                LoadDialog();
            if (!string.IsNullOrEmpty(_loadedPath))
                EditorGUILayout.LabelField(Path.GetFileName(_loadedPath));
            EditorGUILayout.EndHorizontal();
            if (_log != null)
            {
                EditorGUILayout.LabelField(
                    $"人数 {_log.PlayerCount} / 种子 {_log.Seed} / MaxTurns {_log.MaxTurns} / " +
                    $"TotalTicks {_log.TotalTicks}");
                if (_player == null && GUILayout.Button("开始回放"))
                {
                    try
                    {
                        _player = new ReplayPlayer(_log);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Replay] 回放初始化失败: {ex.Message}");
                        _player = null;
                    }
                }
            }
        }

        private void LoadDialog()
        {
            string dir = EditorPrefs.GetString(DirKey, Application.dataPath);
            string path = EditorUtility.OpenFilePanel("打开回放", dir, "dynreplay");
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString(DirKey, Path.GetDirectoryName(path));
            try
            {
                _log = ReplaySerializer.Load(path);
                _player = null;
                _loadedPath = path;
                Debug.Log($"[Replay] 已加载 {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Replay] 加载失败: {ex.Message}");
            }
        }

        private void DrawReplaySection()
        {
            EditorGUILayout.LabelField("回放", EditorStyles.boldLabel);
            if (_player == null) return;

            bool deviated = _player.HasDeviation;
            Color prev = GUI.color;
            GUI.color = deviated ? Color.red : Color.green;
            EditorGUILayout.LabelField($"哈希：{_player.CurrentHash} (期望 {_player.ExpectedHash})");
            GUI.color = prev;
            if (deviated)
                EditorGUILayout.HelpBox($"偏差发生在 Tick {_player.DeviationTick}", MessageType.Error);
            EditorGUILayout.LabelField(
                $"Tick {_player.State.CurrentTick} / {_log.TotalTicks}  回合 {_player.State.CurrentTurn}  " +
                $"Phase {_player.State.Phase}  存活 {_player.State.AliveCharacterCount}");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+1 Tick")) _player.StepTick();
            if (GUILayout.Button("+1 回合")) _player.StepTurn();
            if (GUILayout.Button("+1 指令")) _player.StepCommand();
            _autoPlay = GUILayout.Toggle(_autoPlay, "自动播放");
            EditorGUILayout.EndHorizontal();

            DrawStateTable();
            DrawEventTimeline();
        }

        private void DrawStateTable()
        {
            EditorGUILayout.LabelField("角色状态", EditorStyles.boldLabel);
            for (int i = 0; i < _player.State.PlayerCount; i++)
            {
                Character c = _player.State.GetCharacterByPlayer(i);
                if (c == null) continue;
                EditorGUILayout.LabelField(
                    $"P{i} {c.Definition?.Name}  Sanity {c.Sanity:F1}  AP {c.ActionPoints}  " +
                    $"侵蚀 {c.ErosionLimit:F1}  地点 {c.CurrentLocationId}  " +
                    $"死亡 {c.IsDead}  标签 {c.ASC.Tags.Mask:X}");
            }
        }

        private void DrawEventTimeline()
        {
            EditorGUILayout.LabelField($"事件时间线（Tick {_player.State.CurrentTick}）", EditorStyles.boldLabel);
            bool any = false;
            for (int i = 0; i < _log.Events.Count; i++)
            {
                ReplayEventRecord e = _log.Events[i];
                if (e.Tick != _player.State.CurrentTick) continue;
                any = true;
                EditorGUILayout.LabelField(
                    $"[回合{e.Turn}] {EventName(e.EventType)}  源{e.SourceId}→目标{e.TargetId}  " +
                    $"数值 {e.Magnitude:F2}  int {e.IntArg1}/{e.IntArg2}");
            }
            if (!any)
                EditorGUILayout.LabelField("（无）");
        }

        private static string EventName(int type)
        {
            switch (type)
            {
                case GameplayEventTypes.SkillUsed: return "技能释放";
                case GameplayEventTypes.OnGEApplied: return "GE 施加";
                case GameplayEventTypes.OnGERemoved: return "GE 移除";
                case GameplayEventTypes.DamageDealt: return "伤害";
                case GameplayEventTypes.OnDamageTaken: return "受击";
                case GameplayEventTypes.OnHealReceived: return "治疗";
                case GameplayEventTypes.OnDeath: return "死亡事件";
                case GameplayEventTypes.EntityMoved: return "移动";
                case GameplayEventTypes.PlayerDied: return "死亡";
                case GameplayEventTypes.JudgmentResolved: return "判定结算";
                case GameplayEventTypes.JudgmentPhaseStart: return "判定阶段开始";
                case GameplayEventTypes.ActionPhaseStart: return "行动阶段开始";
                case GameplayEventTypes.APChanged: return "AP 变更";
                case GameplayEventTypes.TurnStart: return "回合开始";
                case GameplayEventTypes.TurnEnd: return "回合结束";
                case GameplayEventTypes.GameOver: return "游戏结束";
                case GameplayEventTypes.OnShieldGranted: return "获盾";
                case GameplayEventTypes.OnShieldBroken: return "破盾";
                case GameplayEventTypes.NukeLaunched: return "核弹";
                case GameplayEventTypes.TrapTriggered: return "陷阱";
                case GameplayEventTypes.ThornDotTick: return "荆棘素";
                case GameplayEventTypes.TidePulse: return "潮汐";
                case GameplayEventTypes.TurretFired: return "炮塔";
                default: return type.ToString();
            }
        }

        private void Update()
        {
            if (_autoPlay && _player != null && !_player.Finished && !_player.HasDeviation)
            {
                // 自动播放：约 10 Tick/秒
                if (EditorApplication.timeSinceStartup - _lastAuto > AutoPlayInterval)
                {
                    _lastAuto = EditorApplication.timeSinceStartup;
                    _player.StepTick();
                    Repaint();
                }
            }
            else if (_autoPlay && _player != null)
            {
                _autoPlay = false; // 播放到终点或偏差：自动关闭
                Repaint();
            }
        }
    }
}
