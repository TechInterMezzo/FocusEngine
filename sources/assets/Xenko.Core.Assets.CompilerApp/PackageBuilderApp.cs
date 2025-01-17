// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.ServiceModel;
using System.Text;

using Mono.Options;
using Xenko.Core.Assets.Diagnostics;
using Xenko.Core.BuildEngine;
using Xenko.Core;
using Xenko.Core.Diagnostics;
using Xenko.Core.Yaml;
using Xenko.Core.VisualStudio;
using Xenko.Assets.Models;
using Xenko.Assets.SpriteFont;
using Xenko.Graphics;
using Xenko.Particles;
using Xenko.Rendering.Materials;
using Xenko.Rendering.ProceduralModels;
using Xenko.SpriteStudio.Offline;
using Xenko.Core.Assets.CompilerApp.Tasks;

namespace Xenko.Core.Assets.CompilerApp
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, UseSynchronizationContext = false)]
    class PackageBuilderApp : IPackageBuilderApp
    {
        private static Stopwatch clock;

        private LogListener globalLoggerOnGlobalMessageLogged;

        private PackageBuilder builder;

        public bool IsSlave { get; private set; }

        public int Run(string[] args)
        {
            // This is used by ExecServer to retrieve the logs directly without using the console redirect (which is not working well
            // in a multi-domain scenario)
            var redirectLogToAppDomainAction = AppDomain.CurrentDomain.GetData("AppDomainLogToAction") as Action<string, ConsoleColor>;

            clock = Stopwatch.StartNew();

            // TODO this is hardcoded. Check how to make this dynamic instead.
            RuntimeHelpers.RunModuleConstructor(typeof(IProceduralModel).Module.ModuleHandle);
            RuntimeHelpers.RunModuleConstructor(typeof(MaterialKeys).Module.ModuleHandle);
            RuntimeHelpers.RunModuleConstructor(typeof(SpriteFontAsset).Module.ModuleHandle);
            RuntimeHelpers.RunModuleConstructor(typeof(ModelAsset).Module.ModuleHandle);
            RuntimeHelpers.RunModuleConstructor(typeof(SpriteStudioAnimationAsset).Module.ModuleHandle);
            RuntimeHelpers.RunModuleConstructor(typeof(ParticleSystem).Module.ModuleHandle);
            //var project = new Package();
            //project.Save("test.xkpkg");

            //Thread.Sleep(10000);
            //var spriteFontAsset = StaticFontAsset.New();
            //Content.Save("test.xkfnt", spriteFontAsset);
            //project.Refresh();

            //args = new string[] { "test.xkpkg", "-o:app_data", "-b:tmp", "-t:1" };

            var exeName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
            var showHelp = false;
            var packMode = false;
            var buildEngineLogger = GlobalLogger.GetLogger("BuildEngine");
            var options = new PackageBuilderOptions(new ForwardingLoggerResult(buildEngineLogger));

            var p = new OptionSet
            {
                "Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp) All Rights Reserved",
                "Xenko Build Tool - Version: "
                +
                String.Format(
                    "{0}.{1}.{2}",
                    typeof(Program).Assembly.GetName().Version.Major,
                    typeof(Program).Assembly.GetName().Version.Minor,
                    typeof(Program).Assembly.GetName().Version.Build) + string.Empty,
                string.Format("Usage: {0} inputPackageFile [options]* -b buildPath", exeName),
                string.Empty,
                "=== Options ===",
                string.Empty,
                { "h|help", "Show this message and exit", v => showHelp = v != null },
                { "v|verbose", "Show more verbose progress logs", v => options.Verbose = v != null },
                { "d|debug", "Show debug logs (imply verbose)", v => options.Debug = v != null },
                { "log", "Enable file logging", v => options.EnableFileLogging = v != null },
                { "disable-auto-compile", "Disable auto-compile of projects", v => options.DisableAutoCompileProjects = v != null},
                { "project-configuration=", "Project configuration", v => options.ProjectConfiguration = v },
                { "platform=", "Platform name", v => options.Platform = (PlatformType)Enum.Parse(typeof(PlatformType), v) },
                { "solution-file=", "Solution File Name", v => options.SolutionFile = v },
                { "package-id=", "Package Id from the solution file", v => options.PackageId = Guid.Parse(v) },
                { "package-file=", "Input Package File Name", v => options.PackageFile = v },
                { "o|output-path=", "Output path", v => options.OutputDirectory = v },
                { "b|build-path=", "Build path", v => options.BuildDirectory = v },
                { "log-file=", "Log build in a custom file.", v =>
                {
                    options.EnableFileLogging = v != null;
                    options.CustomLogFileName = v;
                } },
                { "log-pipe=", "Log pipe.", v =>
                {
                    if (!string.IsNullOrEmpty(v))
                        options.LogPipeNames.Add(v);
                } },
                { "monitor-pipe=", "Monitor pipe.", v =>
                {
                    if (!string.IsNullOrEmpty(v))
                        options.MonitorPipeNames.Add(v);
                } },
                { "slave=", "Slave pipe", v => options.SlavePipe = v }, // Benlitz: I don't think this should be documented
                { "server=", "This Compiler is launched as a server", v => { } },
                { "pack", "Special mode to copy assets and resources in a folder for NuGet packaging", v => packMode = true },
                { "t|threads=", "Number of threads to create. Default value is the number of hardware threads available.", v => options.ThreadCount = int.Parse(v) },
                { "test=", "Run a test session.", v => options.TestName = v },
                { "property:", "Properties. Format is name1=value1;name2=value2", v =>
                {
                    if (!string.IsNullOrEmpty(v))
                    {
                        foreach (var nameValue in v.Split(new [] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var equalIndex = nameValue.IndexOf('=');
                            if (equalIndex == -1)
                                throw new OptionException("Expect name1=value1;name2=value2 format.", "property");

                            options.Properties.Add(nameValue.Substring(0, equalIndex), nameValue.Substring(equalIndex + 1));
                        }
                    }
                }
                },
                { "compile-property:", "Compile properties. Format is name1=value1;name2=value2", v =>
                {
                    if (!string.IsNullOrEmpty(v))
                    {
                        if (options.ExtraCompileProperties == null)
                            options.ExtraCompileProperties = new Dictionary<string, string>();

                        foreach (var nameValue in v.Split(new [] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var equalIndex = nameValue.IndexOf('=');
                            if (equalIndex == -1)
                                throw new OptionException("Expect name1=value1;name2=value2 format.", "property");

                            options.ExtraCompileProperties.Add(nameValue.Substring(0, equalIndex), nameValue.Substring(equalIndex + 1));
                        }
                    }
                }
                },
                {
                    "reattach-debugger=", "Reattach to a Visual Studio debugger", v =>
                    {
                        int debuggerProcessId;
                        if (!string.IsNullOrEmpty(v) && int.TryParse(v, out debuggerProcessId))
                        {
                            if (!Debugger.IsAttached)
                            {
                                using (var debugger = VisualStudioDebugger.GetByProcess(debuggerProcessId))
                                {
                                    debugger?.Attach();
                                }
                            }
                        }
                    }
                },
            };

            TextWriterLogListener fileLogListener = null;

            BuildResultCode exitCode;

            RemoteLogForwarder assetLogger = null;

            try
            {
                var unexpectedArgs = p.Parse(args);

                // Set remote logger
                assetLogger = new RemoteLogForwarder(options.Logger, options.LogPipeNames);
                GlobalLogger.GlobalMessageLogged += assetLogger;

                // Activate proper log level
                buildEngineLogger.ActivateLog(options.LoggerType);

                // Output logs to the console with colored messages
                if (options.SlavePipe == null && !options.LogPipeNames.Any())
                {
                    if (redirectLogToAppDomainAction != null)
                    {
                        globalLoggerOnGlobalMessageLogged = new LogListenerRedirectToAction(redirectLogToAppDomainAction);
                    }
                    else
                    {
                        globalLoggerOnGlobalMessageLogged = new ConsoleLogListener();
                    }
                    globalLoggerOnGlobalMessageLogged.TextFormatter = FormatLog;
                    GlobalLogger.GlobalMessageLogged += globalLoggerOnGlobalMessageLogged;
                }

                if (unexpectedArgs.Any())
                {
                    throw new OptionException("Unexpected arguments [{0}]".ToFormat(string.Join(", ", unexpectedArgs)), "args");
                }
                try
                {
                    options.ValidateOptions();
                }
                catch (ArgumentException ex)
                {
                    throw new OptionException(ex.Message, ex.ParamName);
                }

                if (showHelp)
                {
                    p.WriteOptionDescriptions(Console.Out);
                    return (int)BuildResultCode.Successful;
                }
                else if (packMode)
                {
                    PackageSessionPublicHelper.FindAndSetMSBuildVersion();

                    var csprojFile = options.PackageFile;
                    var intermediatePackagePath = options.BuildDirectory;
                    var generatedItems = new List<(string SourcePath, string PackagePath)>();
                    var logger = new LoggerResult();
                    if (!PackAssetsHelper.Run(logger, csprojFile, intermediatePackagePath, generatedItems))
                    {
                        foreach (var message in logger.Messages)
                        {
                            Console.WriteLine(message);
                        }
                        return (int)BuildResultCode.BuildError;
                    }
                    foreach (var generatedItem in generatedItems)
                    {
                        Console.WriteLine($"{generatedItem.SourcePath}|{generatedItem.PackagePath}");
                    }
                    return (int)BuildResultCode.Successful;
                }

                // Also write logs from master process into a file
                if (options.SlavePipe == null)
                {
                    if (options.EnableFileLogging)
                    {
                        string logFileName = options.CustomLogFileName;
                        if (string.IsNullOrEmpty(logFileName))
                        {
                            string inputName = Path.GetFileNameWithoutExtension(options.PackageFile);
                            logFileName = "Logs/Build-" + inputName + "-" + DateTime.Now.ToString("yy-MM-dd-HH-mm") + ".txt";
                        }

                        string dirName = Path.GetDirectoryName(logFileName);
                        if (dirName != null)
                            Directory.CreateDirectory(dirName);

                        fileLogListener = new TextWriterLogListener(new FileStream(logFileName, FileMode.Create)) { TextFormatter = FormatLog };
                        GlobalLogger.GlobalMessageLogged += fileLogListener;
                    }

                    options.Logger.Info("BuildEngine arguments: " + string.Join(" ", args));
                    options.Logger.Info("Starting builder.");

                    try
                    {
                        var baseDirectory = Path.Combine(options.BuildDirectory, @"../../../../../");
                        var changeFile = baseDirectory + "/files_changed";
                        var buildFile = baseDirectory + "/files_built";
                        var skipFile = baseDirectory + "/always_build";
                        long files_changed_ticks = 0, files_built_ticks = 0;
                        string[] buildlines = null;
                        string platform = options.Platform.ToString() + "-" + options.ProjectConfiguration;

                        if (File.Exists(changeFile))
                            long.TryParse(File.ReadAllText(changeFile), out files_changed_ticks);

                        if (File.Exists(buildFile))
                        {
                            buildlines = File.ReadAllLines(buildFile);
                            for (int i = 0; i < buildlines.Length; i += 2)
                            {
                                if (buildlines[i] == platform)
                                {
                                    long.TryParse(buildlines[i + 1], out files_built_ticks);
                                    // also update with now time
                                    buildlines[i + 1] = System.DateTime.Now.Ticks.ToString();
                                }
                            }
                        }

                        if (File.Exists(skipFile))
                        {
                            options.Logger.Info("Found always_build, so always building assets.");
                        }
                        else if (Process.GetProcessesByName("Focus.GameStudio").Length == 0)
                        {
                            options.Logger.Warning("Focus.GameStudio does not appear to be running, so the AssetCompiler will always rebuild assets.");
                        }
                        else if (files_changed_ticks > 0 && files_built_ticks > 0 && files_built_ticks > files_changed_ticks)
                        {
                            options.Logger.Info("All Assets/ and Resources/ appear up date for " + platform + ", so skipping recompilation. If this is a mistake, delete files_changed and files_built in the root project directory. If you want to always build assets, make a file called always_build in the root project directory.");
                            return 0;
                        }

                        // if there was no changefile, do it now
                        if (File.Exists(changeFile) == false)
                        {
                            File.WriteAllText(changeFile, System.DateTime.Now.Ticks.ToString());
                        }

                        // update file with changes
                        if (File.Exists(buildFile) == false)
                        {
                            File.WriteAllText(buildFile, platform + "\n" + System.DateTime.Now.Ticks.ToString());
                        }
                        else
                        {
                            File.WriteAllLines(buildFile, buildlines);
                            if (files_built_ticks <= 0) File.AppendAllText(buildFile, platform + "\n" + System.DateTime.Now.Ticks.ToString());
                        }
                    }
                    catch (Exception)
                    {
                        options.Logger.Warning("Error reading/writing files_changed and/or files_built to see if we can skip asset compilation. Reverting to building them.");
                    }
                }
                else
                {
                    IsSlave = true;
                }

                if (!string.IsNullOrEmpty(options.TestName))
                {
                    var test = new TestSession();
                    test.RunTest(options.TestName, options.Logger);
                    exitCode = BuildResultCode.Successful;
                }
                else
                {
                    builder = new PackageBuilder(options);
                    if (!IsSlave && redirectLogToAppDomainAction == null)
                    {
                        Console.CancelKeyPress += OnConsoleOnCancelKeyPress;
                    }
                    exitCode = builder.Build();
                }
            }
            catch (OptionException e)
            {
                options.Logger.Error($"Command option '{e.OptionName}': {e.Message}");
                exitCode = BuildResultCode.CommandLineError;
            }
            catch (Exception e)
            {
                options.Logger.Error($"Unhandled exception", e);
                exitCode = BuildResultCode.BuildError;
            }
            finally
            {
                // Flush and close remote logger
                if (assetLogger != null)
                {
                    GlobalLogger.GlobalMessageLogged -= assetLogger;
                    assetLogger.Dispose();
                }

                if (fileLogListener != null)
                {
                    GlobalLogger.GlobalMessageLogged -= fileLogListener;
                    fileLogListener.LogWriter.Close();
                }

                // Output logs to the console with colored messages
                if (globalLoggerOnGlobalMessageLogged != null)
                {
                    GlobalLogger.GlobalMessageLogged -= globalLoggerOnGlobalMessageLogged;
                }
                if (builder != null && !IsSlave && redirectLogToAppDomainAction == null)
                {
                    Console.CancelKeyPress -= OnConsoleOnCancelKeyPress;
                }

                // Reset cache hold by YamlSerializer
                YamlSerializer.Default.ResetCache();
            }
            return (int)exitCode;
        }

        private void OnConsoleOnCancelKeyPress(object _, ConsoleCancelEventArgs e)
        {
            e.Cancel = builder.Cancel();
        }

        private static string FormatLog(ILogMessage message)
        {
            //$filename($row,$column): $error_type $error_code: $error_message
            //C:\Code\Xenko\sources\assets\Xenko.Core.Assets.CompilerApp\PackageBuilder.cs(89,13,89,70): warning CS1717: Assignment made to same variable; did you mean to assign something else?
            var builder = new StringBuilder();
            var assetLogMessage = message as AssetLogMessage;
            // Location
            if (assetLogMessage != null)
                builder.Append($"{assetLogMessage.File}({assetLogMessage.Line + 1},{assetLogMessage.Character + 1}): ");
            // Message type
            builder.Append(message.Type.ToString().ToLowerInvariant()).Append(" ");
            builder.Append((clock.ElapsedMilliseconds * 0.001).ToString("0.000"));
            builder.Append("s: ");
            builder.Append($"[{message.Module ?? "AssetCompiler"}] ");
            builder.Append(message.Text);
            var exceptionInfo = message.ExceptionInfo;
            if (exceptionInfo != null)
            {
                builder.Append(". Exception: ");
                builder.Append(exceptionInfo);
            }
            return builder.ToString();
        }
    }
}
