using UnityEngine;

public static class PhotoBlurPhysics
{
    public struct Solution
    {
        public float ExposureSeconds;
        public Vector2 BlurVectorPixels;
        public Vector2 CameraBlurVectorPixels;
        public float TrajectoryScale;

        public float BlurLengthPixels => BlurVectorPixels.magnitude;
        public float SamplingBlurLengthPixels =>
            Mathf.Max(
                BlurVectorPixels.magnitude,
                CameraBlurVectorPixels.magnitude);
    }

    public static Solution Solve(
        Vector3 cameraPosition,
        Quaternion cameraRotation,
        Vector3 cameraLinearVelocity,
        Vector3 cameraAngularVelocityDegrees,
        Vector3 subjectPosition,
        Vector3 subjectLinearVelocity,
        float focalLengthMillimeters,
        float shutterSpeed,
        float pixelPitchMicrometers,
        float effectiveSensorHeightMillimeters,
        int outputHeight,
        float calibrationScale)
    {
        float exposureSeconds = 1f / Mathf.Max(1f, shutterSpeed);
        float pitchMillimeters =
            Mathf.Max(0.1f, pixelPitchMicrometers) * 0.001f;
        float focalLength =
            Mathf.Max(1f, focalLengthMillimeters);
        float calibration = Mathf.Max(0.1f, calibrationScale);

        Vector3 cameraRight = cameraRotation * Vector3.right;
        Vector3 cameraUp = cameraRotation * Vector3.up;
        Vector3 cameraForward = cameraRotation * Vector3.forward;
        Vector3 toSubject = subjectPosition - cameraPosition;
        float subjectDistance = Mathf.Max(
            0.01f,
            Vector3.Dot(toSubject, cameraForward));

        Vector3 relativeLinearVelocity =
            subjectLinearVelocity - cameraLinearVelocity;
        float subjectAngularX =
            Vector3.Dot(relativeLinearVelocity, cameraRight) /
            subjectDistance;
        float subjectAngularY =
            Vector3.Dot(relativeLinearVelocity, cameraUp) /
            subjectDistance;

        Vector3 cameraAngularVelocity =
            cameraAngularVelocityDegrees * Mathf.Deg2Rad;
        float cameraYaw =
            Vector3.Dot(cameraAngularVelocity, cameraUp);
        float cameraPitch =
            Vector3.Dot(cameraAngularVelocity, cameraRight);
        float cameraRoll =
            Vector3.Dot(cameraAngularVelocity, cameraForward);

        float subjectAngleX =
            Vector3.Dot(toSubject, cameraRight) /
            subjectDistance;
        float subjectAngleY =
            Vector3.Dot(toSubject, cameraUp) /
            subjectDistance;

        Vector2 imageAngularVelocity = new Vector2(
            subjectAngularX - cameraYaw + cameraRoll * subjectAngleY,
            subjectAngularY + cameraPitch - cameraRoll * subjectAngleX);
        float focalLengthInSensorPixels =
            focalLength / pitchMillimeters;
        Vector2 blurVectorPixels =
            imageAngularVelocity *
            focalLengthInSensorPixels *
            exposureSeconds *
            calibration;
        Vector2 cameraBlurVectorPixels = new Vector2(
            -cameraYaw,
            cameraPitch) *
            focalLengthInSensorPixels *
            exposureSeconds *
            calibration;

        float trajectoryScale =
            Mathf.Max(0.001f, effectiveSensorHeightMillimeters) /
            (pitchMillimeters * Mathf.Max(1, outputHeight)) *
            calibration;

        return new Solution
        {
            ExposureSeconds = exposureSeconds,
            BlurVectorPixels = blurVectorPixels,
            CameraBlurVectorPixels = cameraBlurVectorPixels,
            TrajectoryScale = trajectoryScale
        };
    }

    public static Quaternion IntegrateAngularVelocity(
        Quaternion rotation,
        Vector3 angularVelocityDegrees,
        float elapsedSeconds)
    {
        float angularSpeed = angularVelocityDegrees.magnitude;
        if (angularSpeed < 0.0001f)
        {
            return rotation;
        }

        Quaternion delta = Quaternion.AngleAxis(
            angularSpeed * elapsedSeconds,
            angularVelocityDegrees / angularSpeed);
        return delta * rotation;
    }
}
