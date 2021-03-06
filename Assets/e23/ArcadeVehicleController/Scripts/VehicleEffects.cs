﻿using UnityEngine;

namespace e23.VehicleController
{
    public class VehicleEffects : MonoBehaviour
    {
        [Tooltip("If true, the particle system will always emit when the vehicle is moving")]
        [SerializeField] bool alwaysSmoke; 
        [SerializeField] float skidSpeedThreshold = 1.25f;
        [SerializeField] float skidAngleThreshold = 20.0f;

        private VehicleBehaviour vehicleBehaviour;
        private ParticleSystem[] exhaustEffect;
        private TrailRenderer[] trails;
        private bool shouldEmmit = false;

        private void Awake()
        {
            GetRequiredComponents();
        }

        private void Update()
        {
            Effects();
        }

        private void LateUpdate()
        {
            UpdateEmitting();
        }

        private void GetRequiredComponents()
        {
            vehicleBehaviour = GetComponent<VehicleBehaviour>();
            exhaustEffect = GetComponentsInChildren<ParticleSystem>();
            trails = GetComponentsInChildren<TrailRenderer>();
        }

        private void Effects()
        {
            Exhaust();

            for (int i = 0; i < trails.Length; i++)
            {
                Trail(trails[i], shouldEmmit);
            }
        }

        private void UpdateEmitting()
        {
            shouldEmmit = vehicleBehaviour.OnGround && 
            vehicleBehaviour.GetVehicleVelocitySqrMagnitude > (vehicleBehaviour.MaxSpeed / skidSpeedThreshold) && 
            (Vector3.Angle(vehicleBehaviour.GetVehicleVelocity, vehicleBehaviour.VehicleModel.forward) > skidAngleThreshold || alwaysSmoke);
        }

        private void Exhaust()
        {
            for (int i = 0; i < exhaustEffect.Length; i++)
            {
                ParticleSystem.EmissionModule smokeEmission = exhaustEffect[i].emission;
                smokeEmission.enabled = shouldEmmit;
            }
        }

        private void Trail(TrailRenderer trail, bool active)
        {
            trail.emitting = shouldEmmit;
        }
    }
}