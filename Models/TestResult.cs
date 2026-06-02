using System;
using System.Collections.Generic;

namespace PlaywrightPrototype.models
{
    public enum TestStatus { Passed, Failed, Skipped }

    public class StepResult
    {
        public string Action { get; set; } = "";
        public string Detail { get; set; } = "";
        public TestStatus Status { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Intent { get; set; } = "";
        public string UsedSelector { get; set; } = "";
        public bool Repaired { get; set; }
    }

    public class TestResult
    {
        public string TestName { get; set; } = "";
        public string Category { get; set; } = "";
        public TestStatus Status { get; set; }
        public List<StepResult> StepResults { get; set; } = new();
        public string AssertionResult { get; set; } = "";
        public string ScreenshotPath { get; set; } = "";
        public TimeSpan Duration { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Intent { get; set; } = "";
        public string UsedSelector { get; set; } = "";
        public FailureInsight? FailureInsight { get; set; }
    }
}
