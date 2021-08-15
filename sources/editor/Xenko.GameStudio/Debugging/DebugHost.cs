// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Diagnostics;
using ServiceWire.NamedPipes;
using Xenko.Core.Diagnostics;
using Xenko.Core.VisualStudio;
using Xenko.Debugger.Target;

namespace Xenko.GameStudio.Debugging
{
    /// <summary>
    /// Controls a <see cref="GameDebuggerHost"/>, the spawned process and its IPC communication.
    /// </summary>
    class DebugHost : IDisposable
    {
        public NpHost ServiceHost { get; private set; }
        public GameDebuggerHost GameHost { get; private set; }

        public void Start(string workingDirectory, Process debuggerProcess, LoggerResult logger)
        {
            var gameHostAssembly = typeof(GameDebuggerTarget).Assembly.Location;

            var address = "Xenko/Debugger/" + Guid.NewGuid();
            var arguments = $"--host=\"{address}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = gameHostAssembly,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            // Start ServiceWire pipe
            var gameDebuggerHost = new GameDebuggerHost(logger);
            ServiceHost = new NpHost(address, null, null);
            ServiceHost.AddService<IGameDebuggerHost>(gameDebuggerHost);
            ServiceHost.Open();

            var process = new Process { StartInfo = startInfo };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            GameHost = gameDebuggerHost;
        }

        public void Stop()
        {
            if (ServiceHost != null)
            {
                ServiceHost.Close();
                ServiceHost = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
