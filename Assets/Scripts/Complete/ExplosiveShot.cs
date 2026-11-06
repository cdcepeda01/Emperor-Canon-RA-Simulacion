using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Complete
{
    public class ExplosiveShot : Shot
    {
        public GameObject explosionPrefab;
        public float explosionRadius = 5f;
        public float explosionForce = 30f;
        public float upwardForce = 1f;

        private void OnCollisionEnter(Collision collision)
        {
            var explosion = Instantiate(explosionPrefab, transform.position, transform.rotation);

            var rigidBodies = Physics.OverlapSphere(collision.contacts[0].point, explosionRadius);

            foreach (var rb in rigidBodies)
            {
                if (rb.CompareTag("Target"))
                {
                    var ragdoll = rb.GetComponent<Ragdoll>();
                    if (ragdoll != null)
                    {
                        ragdoll.EnableRagdoll();
                        foreach (var _rb in ragdoll.rigidbodies)
                        {
                            _rb.AddExplosionForce(explosionForce, collision.contacts[0].point, explosionRadius, upwardForce, ForceMode.Impulse);
                        }
                    }
                    rb.GetComponent<Rigidbody>().AddExplosionForce(explosionForce, collision.contacts[0].point, explosionRadius, upwardForce, ForceMode.Impulse);
                }
            }

            Destroy(gameObject);
        }
    }
}