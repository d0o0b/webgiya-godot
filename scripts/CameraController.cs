using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public partial class Main : Node3D
{
    private void SetOrbitFromCamera(Vector3 position, Vector3 target)
    {
        _target = target;
        var offset = position - target;
        _distance = Mathf.Max(0.25f, offset.Length());
        _yaw = Mathf.Atan2(offset.X, offset.Z);
        _pitch = Mathf.Asin(offset.Y / _distance);
        UpdateCameraTransform();
    }

    private void ResetCameraToCurrentPreset()
    {
        var preset = _presets[_currentPresetIndex];
        SetOrbitFromCamera(preset.CameraPosition, preset.CameraTarget);
    }

    private void UpdateCameraTransform()
    {
        var cp = Mathf.Cos(_pitch);
        var offset = new Vector3(
            _distance * cp * Mathf.Sin(_yaw),
            _distance * Mathf.Sin(_pitch),
            _distance * cp * Mathf.Cos(_yaw));
        _camera.Position = _target + offset;
        _camera.LookAt(_target, Vector3.Up);
        MarkCameraDrivenSurfelsDirty();
    }

    private void UpdateOrbitFromCurrentCamera()
    {
        var offset = _camera.Position - _target;
        _distance = Mathf.Max(0.25f, offset.Length());
        _yaw = Mathf.Atan2(offset.X, offset.Z);
        _pitch = Mathf.Asin(Mathf.Clamp(offset.Y / _distance, -1.0f, 1.0f));
    }

    private void LookCamera(Vector2 pixels)
    {
        _yaw -= pixels.X * 0.0045f;
        _pitch = Mathf.Clamp(_pitch + pixels.Y * 0.0045f, -1.45f, 1.45f);

        var cp = Mathf.Cos(_pitch);
        var forward = new Vector3(
            -cp * Mathf.Sin(_yaw),
            -Mathf.Sin(_pitch),
            -cp * Mathf.Cos(_yaw)).Normalized();

        _target = _camera.Position + forward * _distance;
        _camera.LookAt(_target, Vector3.Up);
        MarkCameraDrivenSurfelsDirty();
    }

    private void PanCamera(Vector2 pixels)
    {
        var right = _camera.GlobalTransform.Basis.X;
        var up = _camera.GlobalTransform.Basis.Y;
        var scale = _distance * 0.0015f;
        _target += (-right * pixels.X + up * pixels.Y) * scale;
        UpdateCameraTransform();
    }

    private void UpdateKeyboardMovement(float delta)
    {
        var movement = Vector3.Zero;
        var right = _camera.GlobalTransform.Basis.X;
        var forward = -_camera.GlobalTransform.Basis.Z;
        var up = Vector3.Up;

        if (Input.IsKeyPressed(Key.W)) movement += forward;
        if (Input.IsKeyPressed(Key.S)) movement -= forward;
        if (Input.IsKeyPressed(Key.D)) movement += right;
        if (Input.IsKeyPressed(Key.A)) movement -= right;
        if (Input.IsKeyPressed(Key.E)) movement += up;
        if (Input.IsKeyPressed(Key.Q)) movement -= up;

        if (movement.LengthSquared() > 0.0001f)
        {
            var speed = (_distance * 0.65f + 1.0f) * (Input.IsKeyPressed(Key.Shift) ? 4.0f : 1.0f);
            var deltaMove = movement.Normalized() * speed * delta;
            _camera.Position += deltaMove;
            _target += deltaMove;
            UpdateOrbitFromCurrentCamera();
            _camera.LookAt(_target, Vector3.Up);
            MarkCameraDrivenSurfelsDirty();
        }
    }
}
