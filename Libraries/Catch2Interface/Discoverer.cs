﻿/** Basic Info **

Copyright: 2018 Johnny Hendriks

Author : Johnny Hendriks
Year   : 2018
Project: VSTestAdapter for Catch2
Licence: MIT

Notes: None

** Basic Info **/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Catch2Interface
{
/*YAML
Class :
  Description : >
    This class is intended for use in discovering tests via Catch2 test executables.
*/
    public class Discoverer
    {
        #region Fields

        private StringBuilder _logbuilder = new StringBuilder();
        private Settings      _settings;
        private Regex         _rgx_filter;

        private static readonly Regex _rgxDefaultFirstLine = new Regex(@"^All available test cases:|^Matching test cases:");
        private static readonly Regex _rgxDefaultTestCaseLine = new Regex(@"^[ ]{2}([^ ].*)");
        private static readonly Regex _rgxDefaultTestCaseLineExtented = new Regex(@"^[ ]{4}([^ ].*)");
        private static readonly Regex _rgxDefaultTagsLine = new Regex(@"^[ ]{6}([^ ].*)");

        #endregion // Fields

        #region Properties

        public string Log { get; private set; } = string.Empty;

        #endregion // Properties

        #region Constructor

        public Discoverer(Settings settings)
        {
            _settings = settings ?? new Settings();
            _rgx_filter = new Regex(_settings.FilenameFilter);
        }

        #endregion // Constructor

        #region Public Methods

        public List<TestCase> GetTests(IEnumerable<string> sources)
        {
            _logbuilder.Clear();

            var tests = new List<TestCase>();

            // Make sure the discovery commandline to be used is valid
            if( _settings.Disabled || !_settings.HasValidDiscoveryCommandline )
            {
                LogDebug("Test adapter disabled or invalid discovery commandline, should not be able to get here via Test Explorer");
                return tests;
            }

            // Retrieve test cases for each provided source
            foreach (var source in sources)
            {
                LogVerbose($"Source: {source}{Environment.NewLine}");
                if (!File.Exists(source))
                {
                    LogVerbose($"  File not found.{Environment.NewLine}");
                }
                else if (CheckSource(source))
                {
                    var foundtests = ExtractTestCases(source);
                    LogVerbose($"  Testcase count: {foundtests.Count}{Environment.NewLine}");
                    tests.AddRange(foundtests);
                }
                else
                {
                    LogVerbose($"  Invalid source.{Environment.NewLine}");
                }
                LogDebug($"  Accumulated Testcase count: {tests.Count}{Environment.NewLine}");
            }

            Log = _logbuilder.ToString();

            return tests;
        }

        #endregion // Public Methods

        #region Private Methods

        private bool CheckSource(string source)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(source);

                LogDebug($"CheckSource name: {name}{Environment.NewLine}");

                return _rgx_filter.IsMatch(name) && File.Exists(source);
            }
            catch(Exception e)
            {
                LogDebug($"CheckSource Exception: {e.Message}{Environment.NewLine}");
            }

            return false;
        }

        private List<TestCase> ExtractTestCases(string source)
        {
            var output = GetTestCaseInfo(source);
            if(_settings.UseXmlDiscovery)
            {
                LogDebug($"  XML Discovery:{Environment.NewLine}{output}");
                return ProcessXmlOutput(output, source);
            }
            else
            {
                LogDebug($"  Default Discovery:{Environment.NewLine}{output}");
                return ProcessDefaultOutput(output, source);
            }
        }

        private string GetTestCaseInfo(string source)
        {
            // Retrieve test cases
            var process = new Process();
            process.StartInfo.FileName = source;
            process.StartInfo.Arguments = _settings.DiscoverCommandLine;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();

            var output = process.StandardOutput.ReadToEndAsync();
            var erroroutput = process.StandardError.ReadToEndAsync();

            if(_settings.DiscoverTimeout > 0)
            {
                process.WaitForExit(_settings.DiscoverTimeout);
            }
            else
            {
                process.WaitForExit();
            }

            if( !process.HasExited )
            {
                process.Kill();
                LogNormal($"  Warning: Discovery timeout for {source}{Environment.NewLine}");
                if(output.Result.Length == 0)
                {
                    LogVerbose($"  Killed process. There was no output.{Environment.NewLine}");
                }
                else
                {
                    LogVerbose($"  Killed process. Threw away following output:{Environment.NewLine}{output.Result}");
                }
                return string.Empty;
            }
            else
            {
                if(!string.IsNullOrEmpty(erroroutput.Result))
                {
                    LogNormal($"  Error Occurred (exit code {process.ExitCode}):{Environment.NewLine}{erroroutput.Result}");
                    LogDebug($"  output:{Environment.NewLine}{output.Result}");
                    return string.Empty;
                }

                if (!string.IsNullOrEmpty(output.Result))
                {
                    return output.Result;
                }
                else
                {
                    LogDebug($"  No output{Environment.NewLine}");
                    return string.Empty;
                }
            }
        }

        private List<TestCase> ProcessDefaultOutput(string output, string source)
        {
            var tests = new List<TestCase>();

            var reader = new StringReader(output);
            var line = reader.ReadLine();

            if(_settings.UsesTestNameOnlyDiscovery)
            {
                while(line != null)
                {
                    var testcase = new TestCase();
                    testcase.Name = line;
                    testcase.Source = source;

                    tests.Add(testcase);

                    line = reader.ReadLine();
                }
            }
            else
            {
                // Check first line
                if( line == null || !_rgxDefaultFirstLine.IsMatch(line))
                {
                    return tests;
                }

                line = reader.ReadLine();
                // Extract test cases
                while(line != null)
                {
                    if(_rgxDefaultTestCaseLine.IsMatch(line))
                    {
                        // Contrsuct Test Case name
                        var match = _rgxDefaultTestCaseLine.Match(line);
                        string testcasename = match.Groups[1].Value;

                        line = reader.ReadLine();
                        if(line != null && _rgxDefaultTestCaseLineExtented.IsMatch(line))
                        {
                            if(testcasename.EndsWith("-"))
                            {
                                if(testcasename.Length == 77)
                                {
                                    testcasename = testcasename.Substring(0,76);
                                }
                            }
                            else
                            {
                                testcasename += " ";
                            }
                            match = _rgxDefaultTestCaseLineExtented.Match(line);
                            string extend = match.Groups[1].Value;
                            line = reader.ReadLine();
                            while(line != null && _rgxDefaultTestCaseLineExtented.IsMatch(line))
                            {
                                if(extend.EndsWith("-"))
                                {
                                    if(extend.Length == 75)
                                    {
                                        extend = extend.Substring(0,74);
                                    }
                                }
                                else
                                {
                                    extend += " ";
                                }
                                testcasename += extend;
                                match = _rgxDefaultTestCaseLineExtented.Match(line);
                                extend = match.Groups[1].Value;

                                line = reader.ReadLine();
                            }
                            testcasename += extend;
                        }

                        // Create testcase
                        var testcase = new TestCase();
                        testcase.Name = testcasename;
                        testcase.Source = source;

                        // Add Tags
                        {
                            string tagstr = "";
                            while(line != null && _rgxDefaultTagsLine.IsMatch(line))
                            {
                                var matchtag = _rgxDefaultTagsLine.Match(line);
                                string tagline = matchtag.Groups[1].Value;

                                if(tagline.EndsWith("]"))
                                {
                                    tagstr += tagline;
                                }
                                else
                                {
                                    if(tagline.EndsWith("-"))
                                    {
                                        if(tagline.Length == 73)
                                        {
                                            tagstr += tagline.Substring(0,72);
                                        }
                                        else
                                        {
                                            tagstr += tagline;
                                        }
                                    }
                                    else
                                    {
                                        tagstr += tagline + " ";
                                    }
                                }

                                line = reader.ReadLine();
                            }
                            var tags = Reporter.TestCase.ExtractTags(tagstr);
                            testcase.Tags = tags;
                        }

                        // Add testcase
                        if(CanAddTestCase(testcase))
                        {
                            tests.Add(testcase);
                        }
                    }
                    else
                    {
                        line = reader.ReadLine();
                    }
                }
            }

            return tests;
        }

        private List<TestCase> ProcessXmlOutput(string output, string source)
        {
            var tests = new List<TestCase>();

            try
            {
                // Parse Xml
                var xml = new XmlDocument();
                xml.LoadXml(output);

                // Get TestCases
                var nodeGroup = xml.SelectSingleNode("//Group");

                var reportedTestCases = new List<Reporter.TestCase>();
                foreach(XmlNode child in nodeGroup)
                {
                    if(child.Name == Constants.NodeName_TestCase)
                    {
                        reportedTestCases.Add(new Reporter.TestCase(child));
                    }
                }

                // Convert found Xml testcases and add them to TestCase list to be returned
                foreach (var reportedTestCase in reportedTestCases)
                {
                    // Create testcase
                    var testcase = new TestCase();
                    testcase.Name = reportedTestCase.Name;
                    testcase.Source = source;
                    testcase.Filename = reportedTestCase.Filename;
                    testcase.Line = reportedTestCase.Line;
                    testcase.Tags = reportedTestCase.Tags;

                    // Add testcase
                    if(CanAddTestCase(testcase))
                    {
                        tests.Add(testcase);
                    }
                }
            }
            catch(XmlException)
            {
                // For now ignore Xml parsing errors
            }

            return tests;
        }

        private bool CanAddTestCase(TestCase testcase)
        {
            if(_settings.IncludeHidden)
            {
                return true;
            }

            // Check tags for hidden signature
            foreach(var tag in testcase.Tags)
            {
                if(Constants.Rgx_IsHiddenTag.IsMatch(tag))
                {
                    return false;
                }
            }
            return true;
        }

        #endregion // Private Methods

        #region Private Logging Methods

        private void LogDebug(string msg)
        {
            if (_settings == null
             || _settings.LoggingLevel == LoggingLevels.Debug)
            {
                _logbuilder.Append(msg);
            }
        }

        private void LogNormal(string msg)
        {
            if (_settings == null
             || _settings.LoggingLevel == LoggingLevels.Normal
             || _settings.LoggingLevel == LoggingLevels.Verbose
             || _settings.LoggingLevel == LoggingLevels.Debug)
            {
                _logbuilder.Append(msg);
            }
        }

        private void LogVerbose(string msg)
        {
            if (_settings == null
             || _settings.LoggingLevel == LoggingLevels.Verbose
             || _settings.LoggingLevel == LoggingLevels.Debug)
            {
                _logbuilder.Append(msg);
            }
        }

        #endregion // Private Logging Methods

    }
}
