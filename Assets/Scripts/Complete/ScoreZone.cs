using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Complete
{
    public class ScoreZone : MonoBehaviour
    {
        public TMP_Text scoreRemaining;
        private int remainingItems = 0;

        private void Awake()
        {
            remainingItems = GameObject.FindGameObjectsWithTag("Target").Length;
            UpdateText();
        }

        private void UpdateText()
        {
            scoreRemaining.text = remainingItems.ToString();
        }
    }
}