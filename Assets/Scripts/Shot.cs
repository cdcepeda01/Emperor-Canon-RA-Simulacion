using System;
using UnityEngine;

public class Shot : MonoBehaviour
{
   public float timeToLive = 2f;
   private float _expireTimer = 0f;

   public void Update()
   {
      _expireTimer += Time.deltaTime;
      if (_expireTimer >= timeToLive)
      {
         Destroy(gameObject);
      }
   }
}
