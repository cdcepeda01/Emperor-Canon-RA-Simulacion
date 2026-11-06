using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Complete
{
    public class Ragdoll : MonoBehaviour
    {
        Animator animator;
        public List<Rigidbody> rigidbodies = new List<Rigidbody>();
        List<Collider> colliders = new List<Collider>();
        public bool isRagdoll = false;

        public CapsuleCollider capsuleCollider;

        public AudioSource audioSource;

        private void Awake()
        {
            TryGetComponent<Animator>(out animator);
            TryGetComponent<CapsuleCollider>(out capsuleCollider);
            TryGetComponent<AudioSource>(out audioSource);

            if (animator == null) { return; }

            GetComponentsInChildren(rigidbodies);
            GetComponentsInChildren(colliders);

            for (int i = 0; i < rigidbodies.Count; i++)
            {
                rigidbodies[i].isKinematic = true;
                colliders[i].isTrigger = true;
            }

            capsuleCollider.isTrigger = false;
        }

        public void EnableRagdoll()
        {
            audioSource.Play();

            if (isRagdoll) { return; }

            isRagdoll = !isRagdoll;

            for (int i = 0; i < rigidbodies.Count; i++)
            {
                rigidbodies[i].isKinematic = false;
                rigidbodies[i].linearVelocity = Vector3.zero;
                colliders[i].isTrigger = false;
            }
            capsuleCollider.isTrigger = true;

            animator.enabled = false;
        }
    }
}