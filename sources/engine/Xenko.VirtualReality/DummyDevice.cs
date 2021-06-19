// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Linq;
using Xenko.Core;
using Xenko.Core.Diagnostics;
using Xenko.Core.Mathematics;
using Xenko.Games;
using Xenko.Graphics;
using Xenko.Input;

namespace Xenko.VirtualReality
{
    public class DummyDevice : VRDevice
    {
        private const float HalfIpd = 0.06f;

        /// <summary>
        /// Use the gyroscope of the device to simulate device orientation changes.
        /// </summary>
        public bool UseGyroscope = true;

        public override Size2 ActualRenderFrameSize => optimalRenderFrameSize;

        public override float RenderFrameScaling { get; set; }

        public override DeviceState State => DeviceState.Valid;

        public override Vector3 HeadPosition => Vector3.Zero;

        public override Quaternion HeadRotation => headRotation;

        public override Vector3 HeadLinearVelocity => Vector3.Zero;

        public override Vector3 HeadAngularVelocity => angularVelocity;

        public override TouchController LeftHand => null;

        public override TouchController RightHand => null;

        public override ulong PoseCount => 0;

        public override bool CanInitialize => true;

        private Quaternion headRotation = Quaternion.Identity;

        private Vector3 angularVelocity = Vector3.Zero;

        private IOrientationSensor orientationSensor;
        private IGyroscopeSensor gyroscopeSensor;

        private bool orientationInitialized;
        private Quaternion orientationOffset = Quaternion.Identity;
        
        private static readonly Quaternion LandscapeLeftSpaceChange = Quaternion.RotationMatrix(new Matrix(0, 0, 1, 0,
                                                                                                           1, 0, 0, 0,
                                                                                                           0, 1, 0, 0,
                                                                                                           0, 0, 0, 1));

        private static readonly Quaternion LandscapeRightSpaceChange = Quaternion.RotationMatrix(new Matrix(+0, 0, -1, 0,
                                                                                                            -1, 0,  0, 0,
                                                                                                            +0, 1,  0, 0,
                                                                                                            +0, 0,  0, 1));

        private static readonly Quaternion PortraitSpaceChange = Quaternion.RotationMatrix(new Matrix(1, 0,  0, 0,
                                                                                                      0, 0, -1, 0,
                                                                                                      0, 1,  0, 0,
                                                                                                      0, 0,  0, 1));

        private Size2 optimalRenderFrameSize;

        private GameWindow window;

        public static Logger Logger = GlobalLogger.GetLogger("DummyVRDevice");

        public override void UpdatePositions(GameTime gameTime) { }

        public DummyDevice(IServiceRegistry services)
        {
            VRApi = VRApi.Dummy;

            var input = services.GetService<InputManager>();
            if (input != null)
            {
                orientationSensor = input.Sensors.OfType<IOrientationSensor>().FirstOrDefault();
                gyroscopeSensor = input.Sensors.OfType<IGyroscopeSensor>().FirstOrDefault();
            }

            window = services.GetService<IGame>().Window;
        }

        public override void Enable(GraphicsDevice device, GraphicsDeviceManager graphicsDeviceManager, bool requireMirror)
        {
            optimalRenderFrameSize = new Size2(2560, 1440);
        }

        public override void ReadEyeParameters(Eyes eye, float near, float far, ref Vector3 cameraPosition, ref Matrix cameraRotation, bool ignoreHeadRotation, bool ignoreHeadPosition, out Matrix view, out Matrix projection)
        {
            // As generated by Occulus VR
            projection = new Matrix(1.19034183f, 0, 0, 0, 0, 0.999788344f, 0, 0, (eye == Eyes.Left ? -1.0f : 1.0f) * 0.148591548f, -0.110690169f, -1.0001f, -1, 0, 0, -0.10001f, 0);

            // Adjust position from camera to eye
            var eyeLocal = new Vector3((eye == Eyes.Left ? -HalfIpd : HalfIpd) * 0.5f, 0.0f, 0.0f) * BodyScaling;
            Vector3 eyeWorld;
            Matrix fullRotation;
            var headRotationMatrix = ignoreHeadRotation ? Matrix.Identity : Matrix.RotationQuaternion(headRotation);
            Matrix.MultiplyTo(ref headRotationMatrix, ref cameraRotation, out fullRotation);
            Vector3.TransformCoordinate(ref eyeLocal, ref fullRotation, out eyeWorld);
            var pos = cameraPosition + eyeWorld;

            // Transpose ViewMatrix (rotation only, so equivalent to inversing it)
            Matrix.Transpose(ref fullRotation, out view);

            // Rotate our translation so that we can inject it in the view matrix directly
            Vector3.TransformCoordinate(ref pos, ref view, out pos);

            // Apply inverse of translation (equivalent to opposite)
            view.TranslationVector = -pos;
        }

        public override void Commit(CommandList commandList, Texture renderFrame)
        {
        }

        public override void Update(GameTime gameTime)
        {
            //nothing needed
        }

        public override void Draw(GameTime gameTime)
        {
            if (UseGyroscope && gyroscopeSensor != null && orientationSensor != null)
            {
                gyroscopeSensor.IsEnabled = true;
                orientationSensor.IsEnabled = true;

                if (!orientationInitialized)
                {
                    orientationInitialized = true;
                    Recenter();
                }

                UpdateHeadRotation();

                var ratePhone = gyroscopeSensor.RotationRate;
                angularVelocity = new Vector3(ratePhone.Z, ratePhone.X, ratePhone.Y);
            }
            else
            {
                orientationInitialized = false;
                headRotation = Quaternion.Identity;
                angularVelocity = Vector3.Zero;
            }
        }
        private void UpdateHeadRotation()
        {
            var spaceChangeRotation = GetSpaceChangeRotation();
            var sensorOrientation = orientationSensor.Quaternion;
            Quaternion.Multiply(ref spaceChangeRotation, ref sensorOrientation, out headRotation);
            Quaternion.Multiply(ref headRotation, ref orientationOffset, out headRotation);
        }

        public override void Recenter()
        {
            if (orientationSensor == null || !UseGyroscope)
            {
                headRotation = Quaternion.Identity;
                return;
            }
            
            var unitZTransform = Vector3.Transform(Vector3.UnitZ, GetSpaceChangeRotation() * orientationSensor.Quaternion);
            var unitZProjected = new Vector3(unitZTransform.X, 0, unitZTransform.Z);
            var directionAdjustmentAngle = unitZProjected.Length() > MathUtil.ZeroTolerance ? -Math.Sign(unitZProjected.X) * (float)Math.Acos(unitZProjected.Z / unitZProjected.Length()) : 0;
            orientationOffset = Quaternion.RotationY(directionAdjustmentAngle);

            // update the head rotation immediately so that it is updated from the current of the frame.
            UpdateHeadRotation();
        }

        public Quaternion GetSpaceChangeRotation()
        {
            if (window == null)
                return Quaternion.Identity;

            switch (window.CurrentOrientation)
            {
                case DisplayOrientation.LandscapeLeft:
                    return LandscapeLeftSpaceChange;
                case DisplayOrientation.LandscapeRight:
                    return LandscapeRightSpaceChange;
                case DisplayOrientation.Default:
                case DisplayOrientation.Portrait:
                    return PortraitSpaceChange;
                default:
                    Logger.Error($"Unknown screen orientation type [{window.CurrentOrientation}].");
                    return Quaternion.Identity;
            }
        }
    }
}
