using System;
using TMPro;
using UnityEngine;

public class ScoreZone : MonoBehaviour
{
   public TMP_Text scoreRemainingText;
   private int _remainingItems = 0;

   private void Awake()
   {
      _remainingItems = GameObject.FindGameObjectsWithTag("Target").Length;
      UpdateText();
   }

   private void UpdateText()
   {
      scoreRemainingText.text = _remainingItems.ToString();
   }

   private void OnTriggerEnter(Collider other)
   {
      if (!other.CompareTag("Target")) return;

      _remainingItems--;
      UpdateText();
      Destroy(other.gameObject);
   }
}
