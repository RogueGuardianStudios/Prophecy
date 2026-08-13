using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Rokkan.Prophecy.Editor
{
    /// <summary>
    /// Runs the EditMode suite from inside an already-open Editor and writes a plain-text
    /// summary to <see cref="ResultPath"/>.
    ///
    /// <para><b>Why this exists.</b> Unity refuses <c>-batchmode -runTests</c> against a project
    /// an Editor already has open ("Multiple Unity instances cannot open the same project"), so
    /// headless verification is unavailable during normal development. This gives the same
    /// machine-readable result without closing anything: click the menu item, and the summary
    /// lands in a file.</para>
    ///
    /// <para>Results go to <c>&lt;project&gt;/Logs/test-results.txt</c> — inside the Unity project
    /// so it sits beside Unity's own logs, and already covered by the <c>/[Ll]ogs/</c> ignore
    /// rule so it never gets committed.</para>
    /// </summary>
    public static class TestRunnerMenu
    {
        /// <summary>Absolute path of the summary file. `Application.dataPath` ends in "/Assets".</summary>
        public static string ResultPath =>
            Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Logs", "test-results.txt");

        [MenuItem("Prophecy/Tests/Run EditMode Tests", priority = 0)]
        public static void RunEditMode()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new SummaryWriter());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
            Debug.Log($"[Prophecy] EditMode run started. Summary will be written to:\n{ResultPath}");
        }

        [MenuItem("Prophecy/Tests/Reveal Results File", priority = 1)]
        public static void RevealResults()
        {
            if (File.Exists(ResultPath)) EditorUtility.RevealInFinder(ResultPath);
            else Debug.LogWarning($"[Prophecy] No results yet at {ResultPath}. Run the tests first.");
        }

        private sealed class SummaryWriter : ICallbacks
        {
            private readonly StringBuilder _failures = new StringBuilder();
            private readonly System.Collections.Generic.Dictionary<string, int> _perFixture =
                new System.Collections.Generic.Dictionary<string, int>();
            private int _passed, _failed, _skipped, _inconclusive;

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                // Only count leaf tests; suites aggregate their children and would double-count.
                if (result.Test.IsSuite) return;

                // Per-fixture totals, because the handoff quotes them and counting `[Test]`
                // attributes by hand gets it wrong — a `[TestCase]` method is several tests.
                string fixture = FixtureOf(result.Test.FullName);
                _perFixture.TryGetValue(fixture, out int count);
                _perFixture[fixture] = count + 1;

                switch (result.TestStatus)
                {
                    case TestStatus.Passed: _passed++; break;
                    case TestStatus.Skipped: _skipped++; break;
                    case TestStatus.Inconclusive: _inconclusive++; break;
                    case TestStatus.Failed:
                        _failed++;
                        _failures.AppendLine($"FAILED  {result.Test.FullName}");
                        if (!string.IsNullOrWhiteSpace(result.Message))
                            _failures.AppendLine($"        {result.Message.Trim()}");
                        if (!string.IsNullOrWhiteSpace(result.StackTrace))
                            _failures.AppendLine($"        {FirstLine(result.StackTrace)}");
                        break;
                }
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Prophecy EditMode test run");
                sb.AppendLine($"finished : {result.EndTime}");
                sb.AppendLine($"duration : {result.Duration:F2}s");
                sb.AppendLine($"result   : {(_failed == 0 ? "GREEN" : "RED")}");
                sb.AppendLine();
                sb.AppendLine($"passed       : {_passed}");
                sb.AppendLine($"failed       : {_failed}");
                sb.AppendLine($"skipped      : {_skipped}");
                sb.AppendLine($"inconclusive : {_inconclusive}");

                if (_failed > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("--- failures ---");
                    sb.Append(_failures);
                }

                if (_perFixture.Count > 0)
                {
                    var fixtures = new System.Collections.Generic.List<string>(_perFixture.Keys);
                    fixtures.Sort();

                    sb.AppendLine();
                    sb.AppendLine("--- per fixture ---");
                    foreach (var fixture in fixtures)
                        sb.AppendLine($"{_perFixture[fixture],4}  {fixture}");
                }

                var path = ResultPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, sb.ToString());

                if (_failed == 0)
                    Debug.Log($"[Prophecy] Tests GREEN — {_passed} passed. Summary: {path}");
                else
                    Debug.LogError($"[Prophecy] Tests RED — {_failed} failed, {_passed} passed. Summary: {path}");
            }

            /// <summary>
            /// The fixture name out of a full test name — "Rokkan.Prophecy.Tests.DodgeTests.Foo"
            /// becomes "DodgeTests". Parameterised cases carry an argument list, which is dropped
            /// so every case of a `[TestCase]` method lands in the same bucket.
            /// </summary>
            private static string FixtureOf(string fullName)
            {
                if (string.IsNullOrEmpty(fullName)) return "(unknown)";

                int parenthesis = fullName.IndexOf('(');
                if (parenthesis >= 0) fullName = fullName.Substring(0, parenthesis);

                int last = fullName.LastIndexOf('.');
                if (last <= 0) return fullName;

                int previous = fullName.LastIndexOf('.', last - 1);
                return previous < 0 ? fullName.Substring(0, last)
                                    : fullName.Substring(previous + 1, last - previous - 1);
            }

            private static string FirstLine(string s)
            {
                int i = s.IndexOf('\n');
                return (i < 0 ? s : s.Substring(0, i)).Trim();
            }
        }
    }
}
