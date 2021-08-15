// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using Xenko.Core.BuildEngine;
using ServiceWire.NamedPipes;
using Xenko.Core.Diagnostics;
using Xenko.Core.Presentation.ViewModel;

namespace Xenko.GameStudio.Logs
{
    public sealed class BuildLogViewModel : LoggerViewModel, IForwardSerializableLogRemote
    {
        public BuildLogViewModel(IViewModelServiceProvider serviceProvider)
            : base(serviceProvider)
        {
        }

        public void ForwardSerializableLog(SerializableLogMessage message)
        {
            foreach (var logger in Loggers.Keys)
            {
                // print out shader errors first
                if (Xenko.Rendering.EffectSystem.ShaderCompilerErrors.Count > 0)
                {
                    foreach (string err in Xenko.Rendering.EffectSystem.ShaderCompilerErrors)
                    {
                        logger.Error(err + "\n{{end of shader error history entry}}");
                    }
                    Xenko.Rendering.EffectSystem.ShaderCompilerErrors.Clear();
                }

                logger.Log(message);
            }
        }

        /// <inheritdoc/>
        public override void Destroy()
        {
            base.Destroy();
        }
    }
}
