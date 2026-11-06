using System;
using System.Collections.Generic;
using UnityEngine;

public class Ragdoll : MonoBehaviour
{
    private Animator _animator;
    public CapsuleCollider capsuleCollider;
    private AudioSource _audioSource;
    
    public List<Rigidbody> _rigidbodies = new List<Rigidbody>();
    private List<Collider> _colliders = new List<Collider>();
    public bool isRagdoll = false;

    private void Awake()
    {
        TryGetComponent(out _animator);
        TryGetComponent(out capsuleCollider);
        TryGetComponent(out _audioSource);

        if (_animator == null) { return; }

        GetComponentsInChildren(_rigidbodies);
        GetComponentsInChildren(_colliders);

        for (int i = 0; i < _rigidbodies.Count; i++)
        {
            _rigidbodies[i].isKinematic = true;
            _colliders[i].isTrigger = true;
        }

        capsuleCollider.isTrigger = false;
    }

    public void EnableRagdoll()
    {
        _audioSource.Play();
        
        if(isRagdoll) {return;}
        
        isRagdoll = !isRagdoll;
        
        for (int i = 0; i < _rigidbodies.Count; i++)
        {
            _rigidbodies[i].isKinematic = false;
            _rigidbodies[i].linearVelocity = Vector3.zero;
            _colliders[i].isTrigger = false;
        }

        capsuleCollider.isTrigger = true;
        _animator.enabled = false;
    }
}
