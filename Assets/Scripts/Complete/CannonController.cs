using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Complete
{
    public class CannonController : MonoBehaviour
    {
        public Transform barrel;
        public Transform barrelSpawnLocation;

        public float aimSpeed = 10f;
        public float aimRotationMin = -10f;
        public float aimRotationMax = 10f;

        public float turnSpeed = 10f;
        public float turnRotationMin = -10f;
        public float turnRotationMax = 10f;

        SimpleControls m_Contols;
        private Vector2 m_AimRotation = Vector2.zero;
        private Vector2 m_TurnRotation = Vector2.zero;
        private Vector2 m_WheelRotation = Vector2.zero;

        [SerializeField] private Transform m_WheelFrontRight;
        [SerializeField] private Transform m_WheelFrontLeft;
        [SerializeField] private Transform m_WheelBackRight;
        [SerializeField] private Transform m_WheelBackLeft;

        public GameObject projectilePrefab;
        public float projectileForce = 30f;

        public float shotDelay = 0.5f;
        private float timeSinceLastShot = 0f;

        private void Awake()
        {
            m_Contols = new SimpleControls();
            timeSinceLastShot = shotDelay;
        }

        private void OnEnable()
        {
            m_Contols.Enable();
            m_Contols.gameplay.fire.performed += ctx =>
            {
                Fire();
            };
            
            /*
             * The following has been commented out because adding the menu action is part of the course, once it has been added it is safe to re-enable the code.
             * /
             
            /*m_Contols.gameplay.menu.performed += ctx =>
            {
                ToggleMenu();
            };*/
        }

        private void ToggleMenu()
        {
            if (MenuManager.Instance != null) MenuManager.Instance.ToggleMenu();
        }

        private void Fire()
        {
            if (timeSinceLastShot >= shotDelay)
            {
                timeSinceLastShot = 0f;
                var cannonBall = Instantiate(projectilePrefab, barrelSpawnLocation.position, barrelSpawnLocation.rotation);
                var rb = cannonBall.GetComponent<Rigidbody>();
                rb.AddForce(cannonBall.transform.forward * projectileForce, ForceMode.Impulse);
            }
        }

        private void OnDisable()
        {
            m_Contols.Disable();
        }

        private void Update()
        {
            timeSinceLastShot += Time.deltaTime;
            var move = m_Contols.gameplay.move.ReadValue<Vector2>();
            Aim(move.y);
            Turn(move.x);
        }

        private void Turn(float turnDirection)
        {
            var scaledTurnSpeed = turnSpeed * Time.deltaTime;
            var turnAmount = turnDirection * scaledTurnSpeed;
            m_TurnRotation.y = Mathf.Clamp(m_TurnRotation.y + turnAmount, turnRotationMin, turnRotationMax);
            transform.localEulerAngles = m_TurnRotation;

            m_WheelRotation.x = m_TurnRotation.y;
            m_WheelFrontLeft.localEulerAngles = m_WheelRotation;
            m_WheelBackLeft.localEulerAngles = m_WheelRotation;
            m_WheelFrontRight.localEulerAngles = -m_WheelRotation;
            m_WheelBackRight.localEulerAngles = -m_WheelRotation;
        }

        private void Aim(float aimDirection)
        {
            var scaledAimSpeed = aimSpeed * Time.deltaTime;
            var aimAmount = aimDirection * scaledAimSpeed;
            m_AimRotation.x = Mathf.Clamp(m_AimRotation.x + aimAmount, aimRotationMin, aimRotationMax);
            barrel.localEulerAngles = m_AimRotation;
        }
    }
}