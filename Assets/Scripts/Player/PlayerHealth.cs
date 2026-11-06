using UnityEngine;
using UnityEngine.Events;

public class PlayerHealth : MonoBehaviour
{
    [Header("Estadísticas de Vida")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth;

    [Header("Invulnerabilidad Temporal tras Golpe")]
    [SerializeField] private float invulnerabilityDuration = 0.5f;

    [Header("Eventos")]
    public UnityEvent<float, float> OnHealthChanged; // (vidaActual, vidaMaxima)
    public UnityEvent OnPlayerDeath;

    private bool _isDead = false;
    private float _invulnerabilityTimer = 0f;

    public bool IsDead => _isDead;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    private void Update()
    {
        if (_invulnerabilityTimer > 0f)
        {
            _invulnerabilityTimer -= Time.deltaTime;
        }
    }

    public void TakeDamage(float amount, Vector3 hitDirection, float knockbackForce = 0f)
    {
        if (_isDead || _invulnerabilityTimer > 0f) return;

        currentHealth -= amount;
        currentHealth = Mathf.Max(0f, currentHealth);
        _invulnerabilityTimer = invulnerabilityDuration;

        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        // Si el jugador tiene Rigidbody, aplicar retroceso físico opcional
        if (knockbackForce > 0f && TryGetComponent(out Rigidbody rb))
        {
            rb.AddForce(hitDirection.normalized * knockbackForce, ForceMode.Impulse);
        }

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        _isDead = true;
        OnPlayerDeath?.Invoke();
        Debug.Log("[PlayerHealth] ¡El jugador ha muerto!");
    }

    public void Heal(float amount)
    {
        if (_isDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }
}