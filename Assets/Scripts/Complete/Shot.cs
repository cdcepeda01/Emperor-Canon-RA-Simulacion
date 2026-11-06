using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Complete
{
    public class Shot : MonoBehaviour
    {
        public float timeToLive = 2f;
        private float expireTimer = 0;

        private void Update()
        {
            expireTimer += Time.deltaTime;
            if (expireTimer >= timeToLive)
            {
                Destroy(gameObject);
            }
        }
    }

}