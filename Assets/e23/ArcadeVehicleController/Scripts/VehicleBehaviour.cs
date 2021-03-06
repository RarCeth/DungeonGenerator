﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace e23.VehicleController
{
    public class VehicleBehaviour : MonoBehaviour
    {
        [Header("Components")]
        [Tooltip("Parent for the vehicle model.")]
        [SerializeField] Transform vehicleModel;
        [Tooltip("Assign the sphere collider which is on the same GameObject as the rigidbody. TIP: Use the Vehicle Builder window to have this auto assigned when creating a vehicle.")]
        [SerializeField] Rigidbody physicsSphere;

        [Header("Vehicle")]
        [Tooltip("Assign the parent transform which makes up the body of the vehicle.")]
        [SerializeField] Transform vehicleBody;

        [Header("Vehicle Type")]
        [Tooltip("Choose how many wheels the vehicle has.")]
        [SerializeField] VehicleType vehicleType;

        [Header("Wheels")]
        [Tooltip("Assign the transform of the front left wheel. TIP: If you are seeing incorrect rotations when driving, check the docs for guides on troubleshooting.")]
        [SerializeField] Transform frontLeftWheel;
        [Tooltip("Assign the transform of the front right wheel. TIP: If you are seeing incorrect rotations when driving, check the docs for guides on troubleshooting.")]
        [SerializeField] Transform frontRightWheel;
        [Tooltip("Assign the transform of the back left wheel. TIP: If you are seeing incorrect rotations when driving, check the docs for guides on troubleshooting.")]
        [SerializeField] Transform backLeftWheel;
        [Tooltip("Assign the transform of the back right wheel. TIP: If you are seeing incorrect rotations when driving, check the docs for guides on troubleshooting.")]
        [SerializeField] Transform backRightWheel;

        [Header("Settings")]
        [Tooltip("Create and assign a Vehicle Settings ScriptableObject, this object holds the vehicle data (Acceleration, MaxSpeed, Drift, etc). TIP: Clicking the button below, in play mode, allows you to tweak and test values at runtime.")]
        [SerializeField] VehicleBehaviourSettings vehicleSettings;

        private Transform container, wheelFrontLeftParent, wheelFrontRightParent;

        private float speed, speedTarget, currentSpeed;
        private float rotate, tiltTarget;
        private float strafeSpeed, strafeTarget, strafeTilt;
        private float wheelRadius;
        private float rayMaxDistance;

        private bool isBoosting;

        private Vector3 containerBase;
        private Vector3 modelHeightOffGround;
        private List<Transform> vehicleWheels;

        public VehicleType VehicleWheelCount { get { return vehicleType; } set { vehicleType = value; } }
        public Transform VehicleModel { get { return vehicleModel; } set { vehicleModel = value; } }
        public Rigidbody PhysicsSphere { get { return physicsSphere; } set { physicsSphere = value; } }

        public Transform VehicleBody { get { return vehicleBody; } set { vehicleBody = value; } }
        public Transform FrontLeftWheel { get { return frontLeftWheel; } set { frontLeftWheel = value; } }
        public Transform FrontRightWheel { get { return frontRightWheel; } set { frontRightWheel = value; } }
        public Transform BackLeftWheel { get { return backLeftWheel; } set { backLeftWheel = value; } }
        public Transform BackRightWheel { get { return backRightWheel; } set { backRightWheel = value; } }

        public VehicleBehaviourSettings VehicleSettings { get { return vehicleSettings; } set { vehicleSettings = value; } }

        public float Acceleration { get; set; }
        public float MaxSpeed { get; set; }
        public float BreakSpeed { get; set; }
        public float BoostSpeed { get; set; }
        public float MaxSpeedToStartReverse { get; set; }
        public float Steering { get; set; }
        public float MaxStrafingSpeed { get; set; }
        public float Gravity { get; set; }
        public float Drift { get; set; }
        public float VehicleBodyTilt { get; set; }
        public float ForwardTilt { get; set; }
        public bool TurnInAir { get; set; }
        public bool TwoWheelTilt { get; set; }
        public bool StopSlopeSlide { get; set; }
        public float RotateTarget { get; private set; }
        public bool NearGround { get; private set; }
        public bool OnGround { get; private set; }
        public float DefaultMaxSpeed => VehicleSettings.maxSpeed;
        public float DefaultSteering => VehicleSettings.steering;

        public float GetVehicleVelocitySqrMagnitude { get { return physicsSphere.velocity.sqrMagnitude; } }
        public Vector3 GetVehicleVelocity { get { return physicsSphere.velocity; } }

        private void Awake()
        {
            GetRequiredComponents();
            CreateWheelList();
            SetVehicleSettings();
        }

        private void GetRequiredComponents()
        {
            if (vehicleBody == null) { Debug.LogError("Vehicle body has not been assigned on the VehicleBehaviour", gameObject); }

            if (frontLeftWheel != null)
            {
                wheelFrontLeftParent = frontLeftWheel.parent;
                GetWheelRadius();
            }

            if (frontRightWheel != null) { wheelFrontRightParent = frontRightWheel.parent; }

            container = VehicleModel.GetChild(0);
            containerBase = container.localPosition;

            modelHeightOffGround = new Vector3(0, transform.localPosition.y, 0);
        }

        private void CreateWheelList()
        {
            if (frontLeftWheel != null || frontRightWheel != null || backLeftWheel != null || BackRightWheel != null)
            {
                vehicleWheels = new List<Transform>();

                if (frontLeftWheel != null) { vehicleWheels.Add(frontLeftWheel); }
                if (frontRightWheel != null) { vehicleWheels.Add(frontRightWheel); }
                if (backLeftWheel != null) { vehicleWheels.Add(backLeftWheel); }
                if (backRightWheel != null) { vehicleWheels.Add(backRightWheel); }
            }
        }

        private void GetWheelRadius()
        {
            Bounds wheelBounds = frontLeftWheel.GetComponentInChildren<Renderer>().bounds;
            wheelRadius = wheelBounds.size.y;
        }

        public void SetVehicleSettings()
        {
            if (VehicleSettings == null)
            {
                Debug.LogError("Vehicle is missing Vehicle Settings asset.", gameObject);
                return;
            }

            Acceleration = VehicleSettings.acceleration;
            MaxSpeed = VehicleSettings.maxSpeed;
            BreakSpeed = VehicleSettings.breakSpeed;
            BoostSpeed = VehicleSettings.boostSpeed;
            MaxSpeedToStartReverse = VehicleSettings.maxSpeedToStartReverse;
            Steering = VehicleSettings.steering;
            MaxStrafingSpeed = VehicleSettings.maxStrafingSpeed;
            Gravity = VehicleSettings.gravity;
            Drift = VehicleSettings.drift;
            VehicleBodyTilt = VehicleSettings.vehicleBodyTilt;
            ForwardTilt = VehicleSettings.forwardTilt;
            TurnInAir = VehicleSettings.turnInAir;
            TwoWheelTilt = VehicleSettings.twoWheelTilt;
            StopSlopeSlide = VehicleSettings.stopSlopeSlide;

            rayMaxDistance = Mathf.Abs(VehicleModel.localPosition.y);
        }

        private void Update()
        {
            Accelerate();
            Strafe();

            if (vehicleType == VehicleType.FourWheels || vehicleType == VehicleType.TwoWheels)
            {
                SpinWheels();
            }

            Turn();
            WheelAndBodyTilt();
            VehicleTilt();
            GroundVehicle();
        }

        private void FixedUpdate()
        {
            RaycastHit hitOn;
            RaycastHit hitNear;

            OnGround = Physics.Raycast(transform.position, Vector3.down, out hitOn, rayMaxDistance);
            NearGround = Physics.Raycast(transform.position, Vector3.down, out hitNear, rayMaxDistance + 0.8f);

            VehicleModel.up = Vector3.Lerp(VehicleModel.up, hitNear.normal, Time.deltaTime * 8.0f);
            VehicleModel.Rotate(0, transform.eulerAngles.y, 0);

            if (NearGround)
            {
                PhysicsSphere.AddForce(transform.forward * speedTarget, ForceMode.Acceleration);
                PhysicsSphere.AddForce(transform.right * strafeTarget, ForceMode.Acceleration);
            }
            else
            {
                PhysicsSphere.AddForce(transform.forward * (speedTarget / 10), ForceMode.Acceleration);
                PhysicsSphere.AddForce(Vector3.down * Gravity, ForceMode.Acceleration);
            }

            Vector3 localVelocity = transform.InverseTransformVector(physicsSphere.velocity);
            localVelocity.x *= 0.9f + (Drift / 10);

            if (NearGround)
            {
                PhysicsSphere.velocity = transform.TransformVector(localVelocity);
            }

            if (StopSlopeSlide) { CounterSlopes(hitNear.normal); }
        }

        private void LateUpdate()
        {
            transform.position = physicsSphere.transform.position + modelHeightOffGround;
        }

        private void Accelerate()
        {
            speedTarget = Mathf.SmoothStep(speedTarget, speed, Time.deltaTime * Acceleration);
            speed = 0f;
        }

        private void Turn()
        {
            RotateTarget = Mathf.Lerp(RotateTarget, rotate, Time.deltaTime * 4f);
            CalculateTilt(rotate);
            rotate = 0f;

            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(new Vector3(0, transform.eulerAngles.y + RotateTarget, 0)), Time.deltaTime * 2.0f);
        }

        private void Strafe()
        {
            strafeTarget = Mathf.SmoothStep(strafeTarget, strafeSpeed, Time.deltaTime * Acceleration);
            strafeSpeed = 0;

            CalculateTilt(strafeTilt);
            strafeTilt = 0;
        }

        private void CalculateTilt(float tilt)
        {
            tiltTarget = Mathf.Lerp(tiltTarget, tilt, Time.deltaTime * 4);
        }

        private void WheelAndBodyTilt()
        {
            if (wheelFrontLeftParent != null) { wheelFrontLeftParent.localRotation = Quaternion.Euler(wheelFrontLeftParent.localRotation.x, RotateTarget / 2, 0); }
            if (wheelFrontRightParent != null) { wheelFrontRightParent.localRotation = Quaternion.Euler(wheelFrontRightParent.localRotation.x, RotateTarget / 2, 0); }

            vehicleBody.localRotation = Quaternion.Slerp(vehicleBody.localRotation, Quaternion.Euler(new Vector3(speedTarget / ForwardTilt, 0, RotateTarget / 6)), Time.deltaTime * 4.0f);
        }

        private void VehicleTilt()
        {
            VehicleBodyTilt = 0.0f;

            if (TwoWheelTilt) { VehicleBodyTilt = -tiltTarget / 1.5f; }

            container.localPosition = containerBase + new Vector3(0, Mathf.Abs(VehicleBodyTilt) / 2000, 0);
            container.localRotation = Quaternion.Slerp(container.localRotation, Quaternion.Euler(0, RotateTarget / 8, VehicleBodyTilt), Time.deltaTime * 10.0f);
        }

        private void SpinWheels()
        {
            currentSpeed = Vector3.Dot(transform.forward, physicsSphere.velocity);

            float distanceTraveled = currentSpeed * Time.deltaTime;
            float rotationInRadians = distanceTraveled / wheelRadius;
            float rotationInDegrees = rotationInRadians * Mathf.Rad2Deg;

            for (int i = 0; i < vehicleWheels.Count; i++)
            {
                vehicleWheels[i].Rotate(rotationInDegrees, 0, 0);
            }
        }

        private void GroundVehicle()
        {
            // Keeps vehicle grounded when standing still
            if (speed == 0 && GetVehicleVelocitySqrMagnitude < 4f)
            {
                PhysicsSphere.velocity = Vector3.Lerp(PhysicsSphere.velocity, Vector3.zero, Time.deltaTime * 2.0f);
            }
        }

        private void CounterSlopes(Vector3 groundNormal)
        {
            Vector3 carForward = transform.right;
            Vector3 gravity = Physics.gravity;
            Vector3 directionOfFlat = Vector3.Cross(-gravity, groundNormal).normalized; //the direction that if you head in you wouldnt change altitude
            Vector3 directionOfSlope = Vector3.Cross(directionOfFlat, groundNormal); //the direction down the slope
            float affectOfGravity = Vector3.Dot(gravity, directionOfSlope); // returns 1 on a cliff face, 0 on a plane
            float affectOfWheelAlignment = Mathf.Abs(Vector3.Dot(carForward, directionOfSlope)); // returns 1 if facing down or up the slope, 0 if 90 degrees to slope
            PhysicsSphere.AddForce(-directionOfSlope * affectOfWheelAlignment * affectOfGravity, ForceMode.Acceleration);
        }

        /// <summary>
        /// Change the MaxSpeed of the vehicle. Use DefaultMaxSpeed to return to the original MaxSpeed
        /// </summary>
        /// <param name="speedPenalty"></param>
        public void MovementPenalty(float speedPenalty)
        {
            MaxSpeed = speedPenalty;
        }

        /// <summary>
        /// Change the Steering speed of the vehicle. Use DefaultSteering to return to the original Steering
        /// </summary>
        /// <param name="steerPenalty"></param>
        public void SteeringPenalty(float steerPenalty)
        {
            Steering = steerPenalty;
        }

        // Input controls	

        /// <summary>
        /// Move the vehicle foward
        /// </summary>
        public void ControlAcceleration()
        {
            if (!isBoosting)
            {
                speed = MaxSpeed;
            }
            else
            {
                speed = BoostSpeed;
            }
        }

        /// <summary>
        /// Slow down and reverse
        /// </summary>
        public void ControlBrake()
        {
            if (GetVehicleVelocitySqrMagnitude > MaxSpeedToStartReverse)
            {
                speed -= BreakSpeed;
            }
            else
            {
                speed = -MaxSpeed;
            }
        }

        /// <summary>
        /// Turn left (int -1) or right (int 1). 
        /// </summary>
        /// <param name="direction"></param>
        public void ControlTurning(int direction)
        {
            if (NearGround || TurnInAir)
            {
                rotate = Steering * direction;
            }
        }

        /// <summary>
        /// Move sideways, left (int -1) or right (int 1)
        /// </summary>
        /// <param name="direction"></param>
        public void ControlStrafing(int direction)
        {
            strafeSpeed = MaxStrafingSpeed * (direction * 2);
            strafeTilt = Steering * direction;
        }

        /// <summary>
        /// Sets isBoosting to true. Set your boost speed in the VehicleSettings asset
        /// </summary>
        public void Boost()
        {
            isBoosting = true;
        }

        /// <summary>
        /// Performs a timed boost, pass in a float for how long the boost should last in seconds
        /// </summary>
        /// <param name="boostLength"></param>
        public void OneShotBoost(float boostLength)
        {
            if (isBoosting == false)
            {
                StartCoroutine(BoostTimer(boostLength));
            }
        }

        private IEnumerator BoostTimer(float boostLength)
        {
            Boost();

            yield return new WaitForSeconds(boostLength);

            StopBoost();
        }

        /// <summary>
        /// Sets isBoosting to false
        /// </summary>
        public void StopBoost()
        {
            isBoosting = false;
        }

        /// <summary>
        /// Set the position and rotation of the vehicle. This will also set the speed and turning to 0
        /// </summary>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        public void SetPosition(Vector3 position, Quaternion rotation)
        {
            speed = rotate = 0.0f;

            physicsSphere.velocity = Vector3.zero;
            physicsSphere.position = position;

            transform.SetPositionAndRotation(position, rotation);
        }
    }
}