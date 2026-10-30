using UnityEngine.InputSystem;

public static class InspectionPoseBindings
{
    public static InputAction Position()
    {
        var action = new InputAction("Position", InputActionType.Value, expectedControlType: "Vector3");
        action.AddBinding("<HandheldARInputDevice>/devicePosition");
        action.AddBinding("<XRHMD>/centerEyePosition");
        return action;
    }

    public static InputAction Rotation()
    {
        var action = new InputAction("Rotation", InputActionType.Value, expectedControlType: "Quaternion");
        action.AddBinding("<HandheldARInputDevice>/deviceRotation");
        action.AddBinding("<XRHMD>/centerEyeRotation");
        return action;
    }
}
