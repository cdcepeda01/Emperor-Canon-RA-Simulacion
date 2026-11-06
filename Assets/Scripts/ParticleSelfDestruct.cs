using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ParticleSelfDestruct : MonoBehaviour
{
    private ParticleSystem ps;

    private void Awake()
    {
        TryGetComponent<ParticleSystem>(out ps);
        if (ps == null) Destroy(gameObject);
    }

    private void Update()
    {
        if (!ps.IsAlive())
        {
            Destroy(gameObject);
        }
    }
}
