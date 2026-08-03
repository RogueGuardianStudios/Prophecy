using RGS.GOAP.Core;
using UnityEngine;

namespace Rokkan.Prophecy.Goap
{
    /// <summary>
    /// Reports what the planner is actually doing, into the same file the enemy trace uses.
    ///
    /// <para><b>Because "the enemy is not planning" has several causes that look identical.</b> No
    /// brain, no state, a goal the planner cannot reach, an agent that never enabled — from
    /// outside, every one of them is a capsule running the fallback loop. This says which.</para>
    ///
    /// <para>Editor-only, and a diagnostic rather than a feature: it reads the agent and writes a
    /// line, nothing more.</para>
    /// </summary>
    [RequireComponent(typeof(GoapAgent))]
    public sealed class GoapTraceProbe : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField, Tooltip("Seconds between lines.")]
        private float _interval = 0.5f;

        private GoapAgent _agent;
        private float _next;

        private static readonly System.Collections.Generic.List<string> Lines =
            new System.Collections.Generic.List<string>();

        private static string Path => System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath)!.FullName, "Logs", "goap-trace.txt");

        private void Awake() => _agent = GetComponent<GoapAgent>();

        // The planner explains itself through Debug.Log, and this editor's log file is not where
        // the documentation says it is. Rather than hunt for it, take delivery of the messages
        // here and put them in the same file as everything else about this enemy.
        private void OnEnable()
        {
            // Remove-then-add, because Capture is static and every probe in the scene subscribes
            // the same delegate: five enemies meant five copies of every line, which read as one
            // agent pressing five times on the same tick. A diagnostic that inflates what it
            // measures is worse than none.
            Application.logMessageReceived -= Capture;
            Application.logMessageReceived += Capture;
        }

        private static void Capture(string message, string stack, LogType type)
        {
            if (message.IndexOf("Goap", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                message.IndexOf("plan", System.StringComparison.OrdinalIgnoreCase) < 0)
                return;

            // One line each — the planner is chatty per frame and the useful part is the first line.
            int cut = message.IndexOf('\n');
            Lines.Add("    LOG " + (cut > 0 ? message.Substring(0, cut) : message));
        }

        private void Update()
        {
            if (_agent == null || Time.time < _next) return;
            _next = Time.time + Mathf.Max(0.05f, _interval);

            string brain = _agent.Brain != null ? _agent.Brain.name : "<none>";
            string state = _agent.CurrentState != null ? _agent.CurrentState.Name : "<none>";
            string goal = _agent.CurrentGoalInstance != null ? _agent.CurrentGoalInstance.Name : "<none>";
            string action = _agent.CurrentAction != null ? _agent.CurrentAction.Name : "<none>";
            int plan = _agent.ActivePlan != null ? _agent.ActivePlan.Count : -1;

            // The blackboard revision separates the two ways this can look identical: a revision
            // stuck at its initial value means the sensors never wrote and every belief is
            // dangling; a climbing revision means perception is fine and the planner is the
            // thing that is stuck.
            string bb = _agent.Blackboard == null ? "<null>" : $"rev{_agent.Blackboard.ContentRevision}";

            Lines.Add($"t={Time.time,7:F1} enabled={_agent.IsEnabled,-5} brain={brain,-20} " +
                      $"state={state,-8} goal={goal,-14} action={action,-10} planSteps={plan} bb={bb}");

            if (Lines.Count % 10 == 0) Flush();
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= Capture;
            Flush();
        }

        private static void Flush()
        {
            if (Lines.Count == 0) return;

            var text = new System.Text.StringBuilder();
            text.AppendLine("Prophecy GOAP trace");
            text.AppendLine("goal/action <none> with a brain present means the planner found no plan.");
            text.AppendLine();

            int start = Mathf.Max(0, Lines.Count - 200);
            for (int i = start; i < Lines.Count; i++) text.AppendLine(Lines[i]);

            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                System.IO.File.WriteAllText(Path, text.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Prophecy] could not write the GOAP trace: {e.Message}");
            }
        }
#endif
    }
}
