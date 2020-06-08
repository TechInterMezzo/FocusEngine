// <auto-generated>
// Do not edit this file yourself!
//
// This code was generated by Xenko Shader Mixin Code Generator.
// To generate it yourself, please install Xenko.VisualStudio.Package .vsix
// and re-save the associated .xkfx.
// </auto-generated>

using System;
using Xenko.Core;
using Xenko.Rendering;
using Xenko.Graphics;
using Xenko.Shaders;
using Xenko.Core.Mathematics;
using Buffer = Xenko.Graphics.Buffer;

namespace Xenko.Rendering
{
    internal static partial class FogEffectKeys
    {
        public static readonly ValueParameterKey<Color4> FogColor = ParameterKeys.NewValue<Color4>(new Color4(1,1,1,1));
        public static readonly ValueParameterKey<float> Density = ParameterKeys.NewValue<float>(1.0f);
        public static readonly ValueParameterKey<float> FogStart = ParameterKeys.NewValue<float>(0f);
        public static readonly ValueParameterKey<bool> skipBG = ParameterKeys.NewValue<bool>(false);
        public static readonly ValueParameterKey<float> zFar = ParameterKeys.NewValue<float>(1000f);
        public static readonly ValueParameterKey<float> zNear = ParameterKeys.NewValue<float>(0.1f);
        public static readonly ObjectParameterKey<Texture> DepthTexture = ParameterKeys.NewObject<Texture>();
    }
}
