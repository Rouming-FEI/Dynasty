using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SkillAcceptance
{
    /// <summary>
    /// 技能验收面板：一键跑全部验收，按角色 → 技能 → 检查项展示红绿树状报告。
    /// 行着色：绿 Pass / 红 Fail+Error / 黄 NotImplemented / 灰 Skip。
    /// </summary>
    public class SkillAcceptanceWindow : EditorWindow
    {
        private AcceptanceReport _report;
        private Vector2 _scroll;
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

        [MenuItem("Dynasty/技能验收面板")]
        public static void Open() => GetWindow<SkillAcceptanceWindow>("技能验收面板");

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全部运行")) _report = SkillAcceptanceRunner.RunAll();
            if (GUILayout.Button("仅逻辑层")) _report = SkillAcceptanceRunner.RunLogicOnly();
            if (GUILayout.Button("仅链路层")) _report = SkillAcceptanceRunner.RunTurnScripts();
            EditorGUILayout.EndHorizontal();

            if (_report == null)
            {
                GUILayout.Label("尚未运行。点击「全部运行」生成报告。");
                return;
            }

            GUILayout.Label(_report.Summary, EditorStyles.boldLabel);

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (AcceptanceSection section in _report.Sections)
            {
                if (!Foldout(section.CharacterName)) continue;
                foreach (SkillResult skill in section.Skills)
                {
                    if (!Foldout($"{section.CharacterName}/{skill.SkillName}", $"{section.CharacterName}/{skill.SkillName}  [失败 {skill.FailCount}]")) continue;
                    foreach (CheckResult check in skill.Checks)
                        DrawCheck(check);
                }
            }
            GUILayout.EndScrollView();
        }

        private bool Foldout(string key, string label)
        {
            if (!_foldouts.TryGetValue(key, out bool open)) open = true;
            open = EditorGUILayout.Foldout(open, label);
            _foldouts[key] = open;
            return open;
        }

        private static void DrawCheck(CheckResult check)
        {
            switch (check.Status)
            {
                case CheckStatus.Pass: GUI.color = new Color(0.35f, 0.85f, 0.35f); break;
                case CheckStatus.Fail:
                case CheckStatus.Error: GUI.color = new Color(0.95f, 0.35f, 0.35f); break;
                case CheckStatus.NotImplemented: GUI.color = new Color(0.95f, 0.85f, 0.3f); break;
                case CheckStatus.Skip: GUI.color = Color.gray; break;
                default: GUI.color = Color.white; break;
            }
            string message = string.IsNullOrEmpty(check.Message) ? "" : $" — {check.Message}";
            GUILayout.Label($"    [{check.Status}] {check.Name}{message}");
            GUI.color = Color.white;
        }
    }
}
