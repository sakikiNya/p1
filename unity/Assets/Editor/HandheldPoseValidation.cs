using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR.ARSubsystems;

public static class HandheldPoseValidation
{
    [Serializable]
    class Result
    {
        public bool legacyBindsHandheld, positionBindsHandheld, rotationBindsHandheld;
        public bool positionValuePassed, rotationValuePassed;
        public Quaternion expectedRotation, actionRotation, deviceRotation;
    }

    [MenuItem("Inspection/Validate Handheld Pose Input")]
    public static void Validate()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Enter Play mode before validating pose input values.");
        var device = InputSystem.AddDevice<HandheldARInputDevice>();
        using (var legacy = new InputAction("Legacy", InputActionType.Value, "<XRHMD>/centerEyePosition"))
        using (var position = InspectionPoseBindings.Position())
        using (var rotation = InspectionPoseBindings.Rotation())
        {
            try
            {
                legacy.Enable(); position.Enable(); rotation.Enable();
                var result = new Result {
                    legacyBindsHandheld = legacy.controls.Any(c => c.device == device),
                    positionBindsHandheld = position.controls.Any(c => c == device.devicePosition),
                    rotationBindsHandheld = rotation.controls.Any(c => c == device.deviceRotation)
                };
                position.ApplyBindingOverride(0, device.devicePosition.path);
                position.ApplyBindingOverride(1, string.Empty);
                rotation.ApplyBindingOverride(0, device.deviceRotation.path);
                rotation.ApplyBindingOverride(1, string.Empty);
                var expectedPosition = new Vector3(12,8,-6);
                var expectedRotation = Quaternion.Euler(10,35,5);
                InputSystem.QueueDeltaStateEvent(device.devicePosition,expectedPosition);
                InputSystem.QueueDeltaStateEvent(device.deviceRotation,expectedRotation);
                InputSystem.Update();
                result.expectedRotation = expectedRotation;
                result.actionRotation = rotation.ReadValue<Quaternion>();
                result.deviceRotation = device.deviceRotation.ReadValue();
                result.positionValuePassed = Vector3.Distance(position.ReadValue<Vector3>(),expectedPosition)<.001f;
                result.rotationValuePassed = Quaternion.Angle(rotation.ReadValue<Quaternion>(),expectedRotation)<.1f;
                Directory.CreateDirectory("Builds");
                File.WriteAllText("Builds/handheld-pose-validation.json",JsonUtility.ToJson(result,true));
                if (result.legacyBindsHandheld || !result.positionBindsHandheld || !result.rotationBindsHandheld
                    || !result.positionValuePassed || !result.rotationValuePassed)
                    throw new Exception("Handheld pose input validation failed: "+JsonUtility.ToJson(result));
                Debug.Log("Handheld pose input validation PASS: "+JsonUtility.ToJson(result));
            }
            finally { InputSystem.RemoveDevice(device); }
        }
    }
}
