using System;
using UnityEngine;

public class ExplosiveShot : Shot
{
   public GameObject explosionFXPrefab;
   public float explosionRadius = 5f;
   public float explosionForce = 30f;
   public float upwardForce = 1f;

   private void OnCollisionEnter(Collision other)
   {
      var explosion = Instantiate(explosionFXPrefab, transform.position, transform.rotation);
      var rigidBodies = Physics.OverlapSphere(other.contacts[0].point, explosionRadius);

      foreach (var rb in rigidBodies)
      {
         if (rb.CompareTag("Target"))
         {
            var ragdoll = rb.GetComponent<Ragdoll>();
            if (ragdoll != null)
            {
               ragdoll.EnableRagdoll();
               foreach (var ragdollRigidbody in ragdoll._rigidbodies)
               {
                  ragdollRigidbody.AddExplosionForce(explosionForce, other.contacts[0].point, explosionRadius, upwardForce, ForceMode.Impulse);
               }
            }
            rb.GetComponent<Rigidbody>().AddExplosionForce(explosionForce, other.contacts[0].point, explosionRadius, upwardForce, ForceMode.Impulse);
         }
      }

      Destroy(gameObject);
   }
}
