﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace CatchVsTestAdapter
{
    [ExtensionUri(CatchTestExecutor.ExecutorUriString)]
    public class CatchTestExecutor : ITestExecutor
    {
        #region ITestExecutor

        /// <summary>
        /// Cancel test execution.
        /// </summary>
        public void Cancel()
        {
            state_ = ExecutorState.Cancelling;
        }


        /// <summary>
        /// Runs the tests.
        /// </summary>
        /// <param name="sources">Where to look for tests to be run.</param>
        /// <param name="runContext">Context in which to run tests.</param>
        /// <param param name="frameworkHandle">Where results should be stored.</param>
        public void RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            var tests = CatchTestDiscoverer.listTestsInBinaries(sources);
            RunTests(tests, runContext, frameworkHandle);
        }
        
        
        /// <summary>
        /// Runs the tests.
        /// </summary>
        /// <param name="tests">Which tests should be run.</param>
        /// <param name="runContext">Context in which to run tests.</param>
        /// <param param name="frameworkHandle">Where results should be stored.</param>
        public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            state_ = ExecutorState.Running;

            foreach (TestCase test in tests)
            {
                if (state_ == ExecutorState.Cancelling)
                {
                    state_ = ExecutorState.Cancelled;
                    break;
                }

                // Run the tests...
                RunTest(test, runContext, frameworkHandle);
            }
        }

        /// <summary>
        /// Runs a single test and returns its result.
        /// </summary>
        /// <param name="test">The test case to run.</param>
        /// <param name="context">The context under which to run the test.</param>
        /// <returns></returns>
        internal static void RunTest(TestCase test, IRunContext context, IFrameworkHandle framework)
        {
            var result = new TestResult(test);

            framework.RecordStart(test);

            var output = "";
            if (context != null && context.IsBeingDebugged)
            {
                var cwd = Directory.GetCurrentDirectory();
                var exePath = Path.Combine(cwd, test.Source);
                var outputPath = Path.GetTempFileName();
                var arguments = Utility.escapeArguments(test.FullyQualifiedName, "--reporter", "xml", "--break", "--out", outputPath);
                var debuggee = Process.GetProcessById(framework.LaunchProcessWithDebuggerAttached(exePath, cwd, arguments, null));
                debuggee.WaitForExit();
                output = File.ReadAllText(outputPath);
                File.Delete(outputPath);
            }
            else
            {
                output = Utility.runExe(test.Source, test.FullyQualifiedName, "--reporter", "xml");
            }

            var testCaseElement = getTestCaseElement(XDocument.Parse(output), test.FullyQualifiedName);

            result.Outcome = getTestOutcome(testCaseElement);

            framework.RecordEnd(test, result.Outcome);

            if (result.Outcome == TestOutcome.Failed)
            {
                result.ErrorMessage = getErrorMessage(testCaseElement);

                try
                {
                    // TODO: Modify catch so this information is available to us before a failure.
                    var location = getFailureLocation(testCaseElement);
                    result.TestCase.CodeFilePath = location.Item1;
                    result.TestCase.LineNumber = location.Item2;
                }
                catch (Exception ex)
                {
                    // Log it and move on. Don't let a lack of file/line hold up reporting test in status.
                    Console.WriteLine("Couldn't figure out file and line number of failure. Error: " + ex.Message);
                }
            }

            framework.RecordResult(result);
        }

        internal static XElement getTestCaseElement(XDocument testOutputDoc, string testName)
        {
            var testCaseElememt =
                from el in testOutputDoc.Descendants("TestCase")
                where el.Attribute("name").Value == testName
                select el;

            return testCaseElememt.First<XElement>();
        }

        internal static TestOutcome getTestOutcome(XElement testCaseElememt)
        {
            var overallResult = testCaseElememt.Descendants("OverallResult").ToList<XElement>();

            if (!overallResult.Any())
            {
                return TestOutcome.None;
            }

            var status = overallResult.First().Attribute("success").Value;
            if (status.Equals("true", StringComparison.InvariantCultureIgnoreCase))
            {
                return TestOutcome.Passed;
            }
            else
            {
                return TestOutcome.Failed;
            }
        }

        internal static Tuple<string, int> getFailureLocation(XElement testCaseElememt)
        {
            var locations = (from el in testCaseElememt.Descendants("Expression")
                            where el.Attribute("success").Value == "false"
                            select new
                            {
                                file = el.Attribute("filename").Value,
                                line = int.Parse(el.Attribute("line").Value)
                            }).ToList();

            if (!locations.Any())
            {
                throw new Exception("Could not find expression descendent when looking for filename.");
            }

            var location = locations.First();
            return new Tuple<string, int>(location.file, location.line);
        }

        internal static string getErrorMessage(XElement testCaseElememt)
        {
            return ""; // TODO TODO TODO TODO
        }

        #endregion

        /// <summary>
        /// URI string of this executor.
        /// </summary>
        public const string ExecutorUriString = "executor://CatchTestExecutor";


        /// <summary>
        /// URI of this executor.
        /// </summary>
        public static readonly Uri ExecutorUri = new Uri(CatchTestExecutor.ExecutorUriString);


        /// <summary>
        /// The state of this executor.
        /// </summary>
        private enum ExecutorState { Stopped, Running, Cancelling, Cancelled }
        private ExecutorState state_ = ExecutorState.Stopped;

    }
}