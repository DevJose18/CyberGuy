using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LifeUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image healthBar;
    [SerializeField] private Color MaxLife = Color.white;
    [SerializeField] private Color MinLife  = Color.white;
    
    [Space(5)]
    [Header("AnimationSpeed")]
    [SerializeField] private float animationSpeed = 1f;
    
    private Coroutine _healthBarCoroutine;
    private float currentHealth;
    private void Start()
    {
        healthBar.color = MaxLife;
        currentHealth = 1f;
        _healthBarCoroutine = StartCoroutine(HealthBarAnimation());
    }

    private void OnEnable()
    {
        PlayerHealth.OnHealthChanged += OnPlayerHealthChanged;
    }

    private void OnDisable()
    {
        PlayerHealth.OnHealthChanged -= OnPlayerHealthChanged;
    }

    private void OnPlayerHealthChanged(float currentHealth)
    {
        healthBar.color = Color.Lerp(MaxLife, MinLife, Mathf.Clamp01(currentHealth));
        this.currentHealth = currentHealth;
    }


    IEnumerator HealthBarAnimation()
    {
        while (currentHealth > 0)
        {
            yield return new WaitForSeconds(animationSpeed);
            healthBar.gameObject.SetActive(true);
            yield return new WaitForSeconds(animationSpeed);
            healthBar.gameObject.SetActive(false);
            yield return null;
        }
    }
}
